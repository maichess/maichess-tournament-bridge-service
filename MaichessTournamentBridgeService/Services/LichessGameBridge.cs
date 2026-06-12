using Maichess.MatchManager.V1;
using MaichessTournamentBridgeService.Models;
using MaichessTournamentBridgeService.Providers;
using MaichessTournamentBridgeService.Providers.Lichess;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MaichessTournamentBridgeService.Services;

// Drives one Lichess game with the engine-drives/we-mirror model, reusing the pure
// GameDriver decision logic. Streams the game via IExternalProvider, asks the Engine
// for a move on our turn, submits it back to the provider, and mirrors every state into
// match-db. Clocks stay in milliseconds end to end — Lichess is ms-native, so unlike
// the tournament-server path there is no seconds→ms conversion.
internal sealed class LichessGameBridge(
    IExternalProvider provider,
    IEngineMoveSource engine,
    IExternalMatchMirror mirror,
    IHostApplicationLifetime lifetime,
    ILogger<LichessGameBridge> logger) : ILichessBridgeLauncher
{
    public Task<string> StartAsync(string botId, string token, string gameId, CancellationToken ct)
    {
        var ready = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var game = new ExternalGameRef(gameId, token);

        // The game outlives the HTTP request that started it. Drive it under the
        // application lifetime, NOT the request's CancellationToken — that token is
        // aborted the instant the registration response is sent, which would kill the
        // game after a move or two. The request `ct` only bounds the wait for the
        // mirror match below (so an aborted caller stops waiting; the game keeps going).
        _ = Task.Run(
            () => DriveAsync(game, botId, ready, lifetime.ApplicationStopping),
            CancellationToken.None);
        return ready.Task.WaitAsync(ct);
    }

    // The drive loop. Exposed internally so it can be awaited directly in tests with a
    // scripted provider. The match-db id is published through `ready` the moment the
    // mirror match is created; failures before that point fault `ready` so the caller's
    // StartAsync surfaces them.
    internal async Task DriveAsync(
        ExternalGameRef game, string botId, TaskCompletionSource<string> ready, CancellationToken ct)
    {
        GameDriverState? state = null;
        try
        {
            await foreach (GameUpdate update in provider.StreamGameAsync(game, ct))
            {
                if (state is null)
                {
                    string matchId = await mirror.CreateAsync(ToMatchInfo(provider.Name, botId, game, update), ct);
                    state = NewState(matchId, botId, game, update);
                    ready.TrySetResult(matchId);
                    logger.LogInformation(
                        "Mirroring Lichess game {GameId} as match {MatchId} (we are {Color})",
                        game.GameId,
                        matchId,
                        update.OurColor);
                }

                state = Fold(state, update);

                switch (GameDriver.DetermineAction(state))
                {
                    case GameDriverAction.RequestEngineMove:
                        await PlayMoveAsync(game, botId, state, ct);
                        break;

                    case GameDriverAction.FinalizeMatch:
                        await mirror.SyncAsync(ToSync(state, update, finished: true), ct);
                        return;

                    default:
                        await mirror.SyncAsync(ToSync(state, update, finished: false), ct);
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Lichess drive cancelled for game {GameId}", game.GameId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Lichess drive failed for game {GameId}", game.GameId);
            ready.TrySetException(ex);
        }
        finally
        {
            ready.TrySetException(
                new InvalidOperationException($"Lichess game {game.GameId} ended before it started"));
        }
    }

    private static GameDriverState NewState(
        string matchId, string botId, ExternalGameRef game, GameUpdate update) =>
        new(
            TournamentId: string.Empty,
            GameId: game.GameId,
            MatchDbMatchId: matchId,
            OurColor: update.OurColor,
            OurBotToken: game.Token,
            Moves: [],
            CurrentFen: update.Fen,
            Status: "ongoing",
            TurnColor: update.Turn,
            WhiteTimeMs: update.WhiteTimeMs,
            BlackTimeMs: update.BlackTimeMs);

    private static GameDriverState Fold(GameDriverState state, GameUpdate update) =>
        state with
        {
            Moves = [.. update.Moves],
            CurrentFen = update.Fen,
            Status = update.Status,
            TurnColor = update.Turn,
            WhiteTimeMs = update.WhiteTimeMs,
            BlackTimeMs = update.BlackTimeMs,
        };

    // The first update's remaining clock is the per-side base time (no moves played yet).
    private static ExternalMatchInfo ToMatchInfo(
        string providerName, string botId, ExternalGameRef game, GameUpdate first) =>
        new(
            ProviderName: providerName,
            ExternalRef: game.GameId,
            OurColor: first.OurColor,
            OurBotId: botId,
            OpponentName: first.OpponentName,
            BaseMs: first.OurColor == "white" ? first.WhiteTimeMs : first.BlackTimeMs,
            IncrementMs: 0);

    private static ExternalMatchSync ToSync(GameDriverState state, GameUpdate update, bool finished) =>
        new(
            MatchId: state.MatchDbMatchId,
            Fen: state.CurrentFen,
            Status: state.Status,
            Moves: state.Moves,
            WhiteTimeMs: state.WhiteTimeMs,
            BlackTimeMs: state.BlackTimeMs,
            Finished: finished,
            EndReason: finished ? LichessStatus.ToEndReason(update.RawStatus) : EndReason.Unspecified);

    private async Task PlayMoveAsync(
        ExternalGameRef game, string botId, GameDriverState state, CancellationToken ct)
    {
        long remaining = state.OurColor == "white" ? state.WhiteTimeMs : state.BlackTimeMs;
        long timeLimit = GameDriver.ComputeTimeLimitMs(remaining, state.Moves.Count);
        string move = await engine.GetBestMoveAsync(botId, state.CurrentFen, (int)timeLimit, ct);
        await provider.SubmitMoveAsync(game, move, ct);
    }
}
