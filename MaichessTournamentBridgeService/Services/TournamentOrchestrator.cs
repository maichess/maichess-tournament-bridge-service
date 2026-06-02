using Grpc.Core;
using Maichess.Engine.V1;
using Maichess.MatchManager.V1;
using MaichessTournamentBridgeService.Clients;
using MaichessTournamentBridgeService.Models;
using Microsoft.Extensions.Logging;

namespace MaichessTournamentBridgeService.Services;

internal sealed class TournamentOrchestrator(
    TournamentServerClient tournamentClient,
    Matches.MatchesClient matchManagerClient,
    Bots.BotsClient engineClient,
    RegistrationStore registrationStore,
    ILogger<TournamentOrchestrator> logger)
{
    private readonly Dictionary<string, CancellationTokenSource> _activeTournaments = [];

    internal void StartDriving(Registration registration)
    {
        if (_activeTournaments.ContainsKey(registration.Id))
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _activeTournaments[registration.Id] = cts;

        _ = Task.Run(() => DriveAsync(registration, cts.Token));
    }

    internal void StopDriving(string registrationId)
    {
        if (_activeTournaments.Remove(registrationId, out CancellationTokenSource? cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    internal bool IsActive(string registrationId) => _activeTournaments.ContainsKey(registrationId);

    private async Task DriveAsync(Registration registration, CancellationToken ct)
    {
        try
        {
            logger.LogInformation(
                "Starting tournament drive for {TournamentId} on {ServerUrl}",
                registration.TournamentId,
                registration.ServerUrl);

            await foreach (TournamentEvent evt in tournamentClient.StreamTournamentAsync(
                registration.ServerUrl,
                registration.BotToken,
                registration.TournamentId,
                ct))
            {
                await HandleTournamentEventAsync(registration, evt, ct);
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Tournament drive cancelled for {RegistrationId}", registration.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Tournament drive failed for {RegistrationId}", registration.Id);
        }
        finally
        {
            registration.Status = "finished";
            registrationStore.Save(registration);
            _activeTournaments.Remove(registration.Id, out _);
        }
    }

    private async Task HandleTournamentEventAsync(
        Registration registration, TournamentEvent evt, CancellationToken ct)
    {
        switch (evt.Type)
        {
            case "gameStart":
                if (evt.GameId is not null && evt.Color is not null)
                {
                    await HandleGameStartAsync(registration, evt.GameId, evt.Color, ct);
                }

                break;

            case "tournamentFinished":
                logger.LogInformation(
                    "Tournament {TournamentId} finished", registration.TournamentId);
                break;
        }
    }

    private async Task HandleGameStartAsync(
        Registration registration, string gameId, string ourColor, CancellationToken ct)
    {
        logger.LogInformation(
            "Game {GameId} started, playing as {Color}", gameId, ourColor);

        string matchDbId = await CreateExternalMatchAsync(registration, gameId, ourColor, ct);
        registrationStore.AddGameMapping(
            registration.Id,
            new GameMapping { TournamentGameId = gameId, MatchDbMatchId = matchDbId });

        var state = new GameDriverState(
            registration.TournamentId,
            gameId,
            matchDbId,
            ourColor,
            registration.BotToken,
            [],
            "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
            "ongoing",
            300_000,
            300_000);

        await DriveGameAsync(registration, state, ct);
    }

    private async Task DriveGameAsync(
        Registration registration, GameDriverState state, CancellationToken ct)
    {
        try
        {
            await foreach (GameEvent evt in tournamentClient.StreamGameAsync(
                registration.ServerUrl,
                registration.BotToken,
                registration.TournamentId,
                state.GameId,
                ct))
            {
                state = GameDriver.ApplyGameEvent(state, evt);
                GameDriverAction action = GameDriver.DetermineAction(state);

                switch (action)
                {
                    case GameDriverAction.RequestEngineMove:
                        string move = await GetEngineMoveAsync(registration.MaichessBotId, state, ct);
                        await tournamentClient.SubmitMoveAsync(
                            registration.ServerUrl,
                            state.OurBotToken,
                            state.TournamentId,
                            state.GameId,
                            move,
                            ct);
                        break;

                    case GameDriverAction.FinalizeMatch:
                        await SyncMatchStateAsync(state, ct);
                        return;

                    case GameDriverAction.SyncToMatchDb:
                    case GameDriverAction.WaitForOpponent:
                        await SyncMatchStateAsync(state, ct);
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Game drive failed for {GameId}", state.GameId);
        }
    }

    private async Task<string> GetEngineMoveAsync(
        string botId, GameDriverState state, CancellationToken ct)
    {
        long remainingMs = state.OurColor == "white" ? state.WhiteTimeMs : state.BlackTimeMs;
        long timeLimitMs = GameDriver.ComputeTimeLimitMs(remainingMs, state.Moves.Count);

        GetBestMoveResponse response = await engineClient.GetBestMoveAsync(
            new GetBestMoveRequest
            {
                BotId = botId,
                Fen = state.CurrentFen,
                TimeLimitMs = (uint)timeLimitMs,
            },
            cancellationToken: ct);

        return response.Move;
    }

    private async Task<string> CreateExternalMatchAsync(
        Registration registration, string gameId, string ourColor, CancellationToken ct)
    {
        Player white = ourColor == "white"
            ? new Player { BotId = registration.MaichessBotId }
            : new Player { ExternalName = "Opponent" };
        Player black = ourColor == "black"
            ? new Player { BotId = registration.MaichessBotId }
            : new Player { ExternalName = "Opponent" };

        CreateMatchResponse response = await matchManagerClient.CreateMatchAsync(
            new CreateMatchRequest
            {
                White = white,
                Black = black,
                TimeFormat = new Maichess.MatchManager.V1.TimeFormat
                {
                    Id = "5+0",
                    BaseMs = 300_000,
                    IncrementMs = 0,
                    Category = "blitz",
                },
                Source = MatchSource.External,
                ExternalProvider = "tournament-server",
                ExternalRef = gameId,
                CreatedBy = new Player { BotId = registration.MaichessBotId },
            },
            cancellationToken: ct);

        return response.Match.Id;
    }

    private async Task SyncMatchStateAsync(GameDriverState state, CancellationToken ct)
    {
        MatchStatus protoStatus = state.Status switch
        {
            "white_won" => MatchStatus.WhiteWon,
            "black_won" => MatchStatus.BlackWon,
            "draw" => MatchStatus.Draw,
            _ => MatchStatus.Ongoing,
        };

        EndReason endReason = state.Status switch
        {
            "white_won" or "black_won" => EndReason.Checkmate,
            "draw" => EndReason.DrawAgreement,
            _ => EndReason.Checkmate,
        };

        var request = new SyncExternalMatchRequest
        {
            MatchId = state.MatchDbMatchId,
            CurrentFen = state.CurrentFen,
            Status = protoStatus,
            WhiteTimeMs = state.WhiteTimeMs,
            BlackTimeMs = state.BlackTimeMs,
            EndReason = endReason,
        };
        request.Moves.AddRange(state.Moves);

        if (state.IsFinished)
        {
            request.FinishedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        try
        {
            await matchManagerClient.SyncExternalMatchAsync(request, cancellationToken: ct);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            logger.LogWarning("Match {MatchId} not found during sync", state.MatchDbMatchId);
        }
    }
}
