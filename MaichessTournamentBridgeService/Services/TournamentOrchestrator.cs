using System.Collections.Concurrent;
using Grpc.Core;
using Maichess.MatchManager.V1;
using MaichessTournamentBridgeService.Clients;
using MaichessTournamentBridgeService.Models;
using Microsoft.Extensions.Logging;

namespace MaichessTournamentBridgeService.Services;

internal sealed class TournamentOrchestrator(
    TournamentServerClient tournamentClient,
    Matches.MatchesClient matchManagerClient,
    IEngineMoveSource engineMoveSource,
    RegistrationStore registrationStore,
    ILogger<TournamentOrchestrator> logger)
{
    private readonly Dictionary<string, CancellationTokenSource> _activeTournaments = [];
    private readonly ConcurrentDictionary<string, string> _gameOwners = new();
    private readonly ConcurrentDictionary<string, (string WhiteBotId, string BlackBotId)> _roundPairings = new();
    private readonly ConcurrentDictionary<string, TournamentClock> _tournamentClocks = new();

    internal static bool IsOurTurn(GameEvent evt, string ourColor) => evt.Turn == ourColor;

    internal static string FormatTimeId(TournamentClock clock) =>
        $"{clock.Limit / 60}+{clock.Increment}";

    internal static string CategoryFor(TournamentClock clock)
    {
        int estimatedSeconds = clock.Limit + (40 * clock.Increment);
        return estimatedSeconds < 180 ? "bullet"
            : estimatedSeconds < 480 ? "blitz"
            : estimatedSeconds < 1500 ? "rapid"
            : "classical";
    }

    internal Task StartDriving(Registration registration)
    {
        if (_activeTournaments.ContainsKey(registration.Id))
        {
            return Task.CompletedTask;
        }

        var cts = new CancellationTokenSource();
        _activeTournaments[registration.Id] = cts;

        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = Task.Run(() => DriveAsync(registration, connected, cts.Token));
        return connected.Task;
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

    private async Task DriveAsync(
        Registration registration, TaskCompletionSource connected, CancellationToken ct)
    {
        try
        {
            logger.LogInformation(
                "Starting tournament drive for {TournamentId} bot {BotId} on {ServerUrl}",
                registration.TournamentId,
                registration.MaichessBotId,
                registration.ServerUrl);

            await foreach (TournamentEvent evt in tournamentClient.StreamTournamentAsync(
                registration.ServerUrl,
                registration.BotToken,
                registration.TournamentId,
                ct,
                () => connected.TrySetResult()))
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
            connected.TrySetResult();
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
            case "roundStarted":
                await LoadRoundPairingsAsync(registration, evt.Round, ct);
                break;

            case "gameStart":
                if (evt.GameId is not null && evt.Color is not null)
                {
                    if (!IsGameForBot(registration, evt.GameId, evt.Color))
                    {
                        logger.LogDebug(
                            "Ignoring gameStart for {GameId} color {Color} — not for bot {BotId}",
                            evt.GameId,
                            evt.Color,
                            registration.TournamentBotId);
                        break;
                    }

                    _ = Task.Run(
                        () => HandleGameStartAsync(registration, evt.GameId, evt.Color, ct),
                        CancellationToken.None);
                }

                break;

            case "tournamentFinished":
                logger.LogInformation(
                    "Tournament {TournamentId} finished", registration.TournamentId);
                break;
        }
    }

    private async Task LoadRoundPairingsAsync(
        Registration registration, int round, CancellationToken ct)
    {
        try
        {
            RoundPairingsResponse pairings = await tournamentClient.GetRoundPairingsAsync(
                registration.ServerUrl, registration.TournamentId, round, ct);

            foreach (TournamentPairing pairing in pairings.Pairings)
            {
                if (pairing.Matches.Count > 0)
                {
                    string gameId = pairing.Matches[0].GameId;
                    _roundPairings[gameId] = (pairing.White.Id, pairing.Black.Id);
                    logger.LogInformation(
                        "Round {Round} pairing: {GameId} white={White} black={Black}",
                        round,
                        gameId,
                        pairing.White.Id,
                        pairing.Black.Id);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load round {Round} pairings", round);
        }
    }

    private bool IsGameForBot(Registration registration, string gameId, string color)
    {
        if (!_roundPairings.TryGetValue(
            gameId, out (string WhiteBotId, string BlackBotId) pairing))
        {
            logger.LogWarning(
                "No pairing data for game {GameId}, allowing bot {BotId} to proceed",
                gameId,
                registration.TournamentBotId);
            return true;
        }

        string expectedBotId = color == "white" ? pairing.WhiteBotId : pairing.BlackBotId;
        return expectedBotId == registration.TournamentBotId;
    }

    private async Task HandleGameStartAsync(
        Registration registration, string gameId, string ourColor, CancellationToken ct)
    {
        // Fetch the real game so the driver starts from the actual position, clock,
        // and turn the tournament server created — never assume a standard 5+0 game.
        // (The old hardcoded standard FEN made the opening move illegal for custom
        // openings, so it was rejected and the game timed out without a single move.)
        GameState game = await tournamentClient.GetGameAsync(
            registration.ServerUrl, registration.TournamentId, gameId, ct);

        bool isOwner = _gameOwners.TryAdd(gameId, registration.Id);

        Registration? opponentReg = FindOpponentRegistration(registration, gameId, ourColor);
        string opponentBotId = opponentReg?.MaichessBotId ?? string.Empty;

        if (isOwner)
        {
            logger.LogInformation(
                "Game {GameId} started, bot {BotId} playing as {Color} (owner)",
                gameId,
                registration.MaichessBotId,
                ourColor);

            TournamentClock clock = await GetTournamentClockAsync(registration, ct);
            string matchDbId = await CreateExternalMatchAsync(
                registration, gameId, ourColor, opponentBotId, clock, ct);

            registrationStore.AddGameMapping(
                registration.Id,
                new GameMapping { TournamentGameId = gameId, MatchDbMatchId = matchDbId });

            if (opponentReg is not null)
            {
                registrationStore.AddGameMapping(
                    opponentReg.Id,
                    new GameMapping { TournamentGameId = gameId, MatchDbMatchId = matchDbId });
            }

            var state = new GameDriverState(
                registration.TournamentId,
                gameId,
                matchDbId,
                ourColor,
                registration.BotToken,
                [.. game.Moves.Split(' ', StringSplitOptions.RemoveEmptyEntries)],
                game.Fen,
                game.Status,
                game.Turn,
                (long)(game.Clock.WhiteTime * 1000),
                (long)(game.Clock.BlackTime * 1000));

            await DriveGameAsync(registration, state, ct);
        }
        else
        {
            logger.LogInformation(
                "Game {GameId} started, bot {BotId} playing as {Color} (driven by sibling)",
                gameId,
                registration.MaichessBotId,
                ourColor);

            await DriveNonOwnerGameAsync(registration, gameId, ourColor, ct);
        }
    }

    private async Task<TournamentClock> GetTournamentClockAsync(
        Registration registration, CancellationToken ct)
    {
        if (_tournamentClocks.TryGetValue(registration.TournamentId, out TournamentClock? cached))
        {
            return cached;
        }

        Tournament tournament = await tournamentClient.GetTournamentAsync(
            registration.ServerUrl, registration.TournamentId, ct);
        _tournamentClocks[registration.TournamentId] = tournament.Clock;
        return tournament.Clock;
    }

    private Registration? FindOpponentRegistration(
        Registration self, string gameId, string ourColor)
    {
        if (!_roundPairings.TryGetValue(
            gameId, out (string WhiteBotId, string BlackBotId) pairing))
        {
            return null;
        }

        string opponentTournamentBotId = ourColor == "white"
            ? pairing.BlackBotId
            : pairing.WhiteBotId;

        return registrationStore.FindByTournamentBotId(
            self.ServerUrl, self.TournamentId, opponentTournamentBotId);
    }

    private async Task DriveNonOwnerGameAsync(
        Registration registration, string gameId, string ourColor, CancellationToken ct)
    {
        try
        {
            // No opening-move shortcut: the game stream emits the full gameState
            // snapshot on connect, so when it is our turn (including move one) we
            // react to that event with the real position and clock.
            await foreach (GameEvent evt in tournamentClient.StreamGameAsync(
                registration.ServerUrl,
                registration.BotToken,
                registration.TournamentId,
                gameId,
                ct))
            {
                if (!GameDriver.IsActionable(evt))
                {
                    continue;
                }

                if (evt.Type == "gameState" || evt.Type == "move")
                {
                    bool isOurTurn = IsOurTurn(evt, ourColor);
                    if (isOurTurn)
                    {
                        string fen = evt.Fen ?? "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
                        long remainingMs = ourColor == "white"
                            ? (long)((evt.Clock?.WhiteTime ?? 300) * 1000)
                            : (long)((evt.Clock?.BlackTime ?? 300) * 1000);
                        int moveCount = evt.Moves?.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length ?? 0;
                        long timeLimitMs = GameDriver.ComputeTimeLimitMs(remainingMs, moveCount);

                        string move = await engineMoveSource.GetBestMoveAsync(
                            registration.MaichessBotId, fen, (int)timeLimitMs, ct);

                        await tournamentClient.SubmitMoveAsync(
                            registration.ServerUrl,
                            registration.BotToken,
                            registration.TournamentId,
                            gameId,
                            move,
                            ct);
                    }
                }

                if (evt.Type == "gameEnd")
                {
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Non-owner game drive failed for {GameId} bot {BotId}",
                gameId,
                registration.MaichessBotId);
        }
    }

    private async Task DriveGameAsync(
        Registration registration, GameDriverState state, CancellationToken ct)
    {
        try
        {
            // The game stream emits the full gameState snapshot on connect, which
            // drives the first move (when it is our turn) from the real position —
            // no separate opening-move submission is needed.
            await foreach (GameEvent evt in tournamentClient.StreamGameAsync(
                registration.ServerUrl,
                registration.BotToken,
                registration.TournamentId,
                state.GameId,
                ct))
            {
                if (!GameDriver.IsActionable(evt))
                {
                    continue;
                }

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
        finally
        {
            _gameOwners.TryRemove(state.GameId, out _);
        }
    }

    private async Task<string> GetEngineMoveAsync(
        string botId, GameDriverState state, CancellationToken ct)
    {
        long remainingMs = state.OurColor == "white" ? state.WhiteTimeMs : state.BlackTimeMs;
        long timeLimitMs = GameDriver.ComputeTimeLimitMs(remainingMs, state.Moves.Count);

        return await engineMoveSource.GetBestMoveAsync(
            botId, state.CurrentFen, (int)timeLimitMs, ct);
    }

    private async Task<string> CreateExternalMatchAsync(
        Registration registration,
        string gameId,
        string ourColor,
        string opponentBotId,
        TournamentClock clock,
        CancellationToken ct)
    {
        bool opponentIsOurs = !string.IsNullOrEmpty(opponentBotId);

        Player white;
        Player black;

        if (ourColor == "white")
        {
            white = new Player { BotId = registration.MaichessBotId };
            black = opponentIsOurs
                ? new Player { BotId = opponentBotId }
                : new Player { ExternalName = "Opponent" };
        }
        else
        {
            white = opponentIsOurs
                ? new Player { BotId = opponentBotId }
                : new Player { ExternalName = "Opponent" };
            black = new Player { BotId = registration.MaichessBotId };
        }

        CreateMatchResponse response = await matchManagerClient.CreateMatchAsync(
            new CreateMatchRequest
            {
                White = white,
                Black = black,
                TimeFormat = new Maichess.MatchManager.V1.TimeFormat
                {
                    Id = FormatTimeId(clock),
                    BaseMs = clock.Limit * 1000L,
                    IncrementMs = clock.Increment * 1000L,
                    Category = CategoryFor(clock),
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
