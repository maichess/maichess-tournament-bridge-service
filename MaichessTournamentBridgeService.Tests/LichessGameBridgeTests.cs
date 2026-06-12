using Maichess.MatchManager.V1;
using MaichessTournamentBridgeService.Chess;
using MaichessTournamentBridgeService.Models;
using MaichessTournamentBridgeService.Providers;
using MaichessTournamentBridgeService.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MaichessTournamentBridgeService.Tests;

public sealed class LichessGameBridgeTests
{
    private static readonly ExternalGameRef Game = new("game-7", "lip_token");

    private static GameUpdate Update(
        IReadOnlyList<string> moves,
        string fen,
        string turn,
        string ourColor,
        string status = "ongoing",
        string raw = "started") =>
        new(moves, fen, turn, status, raw, 300_000, 300_000, ourColor, "Villain");

    private static LichessGameBridge Bridge(
        IExternalProvider provider, FakeEngineMoveSource engine, FakeMatchMirror mirror) =>
        new(provider, engine, mirror, new FakeApplicationLifetime(), NullLogger<LichessGameBridge>.Instance);

    [Fact]
    internal async Task Drive_AsWhite_CreatesMatchPlaysAndMirrors()
    {
        const string afterE4 = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1";
        var provider = new FakeExternalProvider(
            Update([], ChessPosition.StartFen, "white", "white"),
            Update(["e2e4"], afterE4, "black", "white"),
            Update(["e2e4", "e7e5"], afterE4, "white", "white", "white_won", "mate"));
        var engine = new FakeEngineMoveSource("e2e4");
        var mirror = new FakeMatchMirror("match-1");
        var ready = new TaskCompletionSource<string>();

        await Bridge(provider, engine, mirror).DriveAsync(Game, "blitz-3", ready, CancellationToken.None);

        Assert.Equal("match-1", await ready.Task);

        // The mirror match is created exactly once, with our bot on the white side.
        ExternalMatchInfo info = Assert.Single(mirror.Created);
        Assert.Equal("lichess", info.ProviderName);
        Assert.Equal("game-7", info.ExternalRef);
        Assert.Equal("white", info.OurColor);
        Assert.Equal("blitz-3", info.OurBotId);
        Assert.Equal("Villain", info.OpponentName);
        Assert.Equal(300_000, info.BaseMs);

        // Engine asked once, from the start position, with the computed budget.
        (string botId, string fen, int time) = Assert.Single(engine.Calls);
        Assert.Equal("blitz-3", botId);
        Assert.Equal(ChessPosition.StartFen, fen);
        Assert.Equal(7_500, time);
        Assert.Equal(["e2e4"], provider.Submitted);

        // Two syncs: the opponent-turn state and the final result.
        Assert.Equal(2, mirror.Synced.Count);
        Assert.False(mirror.Synced[0].Finished);
        Assert.Equal(["e2e4"], mirror.Synced[0].Moves);

        ExternalMatchSync final = mirror.Synced[1];
        Assert.True(final.Finished);
        Assert.Equal("white_won", final.Status);
        Assert.Equal(EndReason.Checkmate, final.EndReason);
    }

    [Fact]
    internal async Task Drive_AsWhite_UsesWhiteClockForBaseTimeAndBudget()
    {
        // Asymmetric clocks so the white/black selection is observable.
        var first = new GameUpdate(
            [], ChessPosition.StartFen, "white", "ongoing", "started",
            WhiteTimeMs: 240_000, BlackTimeMs: 300_000, OurColor: "white", OpponentName: "Villain");
        var provider = new FakeExternalProvider(first);
        var engine = new FakeEngineMoveSource("e2e4");
        var mirror = new FakeMatchMirror("match-c");
        var ready = new TaskCompletionSource<string>();

        await Bridge(provider, engine, mirror).DriveAsync(Game, "blitz-3", ready, CancellationToken.None);

        Assert.Equal(240_000, Assert.Single(mirror.Created).BaseMs);
        // ComputeTimeLimitMs(240_000, 0) = 240_000 / 40 = 6_000 (white clock, not black's).
        Assert.Equal(6_000, Assert.Single(engine.Calls).TimeLimitMs);
    }

    [Fact]
    internal async Task Drive_AsBlack_OpponentToMove_DoesNotCallEngine()
    {
        var provider = new FakeExternalProvider(
            Update([], ChessPosition.StartFen, "white", "black"));
        var engine = new FakeEngineMoveSource("should-not-be-used");
        var mirror = new FakeMatchMirror("match-2");
        var ready = new TaskCompletionSource<string>();

        await Bridge(provider, engine, mirror).DriveAsync(Game, "blitz-3", ready, CancellationToken.None);

        Assert.Equal("match-2", await ready.Task);
        Assert.Empty(engine.Calls);
        Assert.Empty(provider.Submitted);
        ExternalMatchInfo info = Assert.Single(mirror.Created);
        Assert.Equal("black", info.OurColor);
        Assert.Single(mirror.Synced);
        Assert.False(mirror.Synced[0].Finished);
    }

    [Fact]
    internal async Task Drive_AsBlack_OurTurn_AsksEngineWithBlackClock()
    {
        const string afterE4 = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1";
        // Asymmetric clocks so the black/white selection is observable.
        var first = new GameUpdate(
            ["e2e4"], afterE4, "black", "ongoing", "started",
            WhiteTimeMs: 300_000, BlackTimeMs: 180_000, OurColor: "black", OpponentName: "Villain");
        var provider = new FakeExternalProvider(first);
        var engine = new FakeEngineMoveSource("e7e5");
        var mirror = new FakeMatchMirror("match-b");
        var ready = new TaskCompletionSource<string>();

        await Bridge(provider, engine, mirror).DriveAsync(Game, "blitz-3", ready, CancellationToken.None);

        (string botId, string fen, int time) = Assert.Single(engine.Calls);
        Assert.Equal(afterE4, fen);
        // ComputeTimeLimitMs(180_000, 1) = 180_000 / 40 = 4_500 (black clock, not white's).
        Assert.Equal(4_500, time);
        Assert.Equal(["e7e5"], provider.Submitted);

        // BaseMs is taken from our (black) side too.
        Assert.Equal(180_000, Assert.Single(mirror.Created).BaseMs);
    }

    [Fact]
    internal async Task Drive_StreamFailsMidGame_DoesNotThrowAndKeepsMatch()
    {
        var provider = new FailingProvider(
            new HttpRequestException("stream dropped"),
            Update([], ChessPosition.StartFen, "white", "black"));
        var mirror = new FakeMatchMirror("match-x");
        var ready = new TaskCompletionSource<string>();

        // The match was created before the failure, so ready resolved and the
        // exception is swallowed (logged) rather than rethrown.
        await Bridge(provider, new FakeEngineMoveSource("x"), mirror)
            .DriveAsync(Game, "blitz-3", ready, CancellationToken.None);

        Assert.Equal("match-x", await ready.Task);
    }

    [Fact]
    internal async Task Drive_StreamFailsBeforeGameStart_FaultsReady()
    {
        var provider = new FailingProvider(new HttpRequestException("game 404"));
        var mirror = new FakeMatchMirror("unused");
        var ready = new TaskCompletionSource<string>();

        await Bridge(provider, new FakeEngineMoveSource("x"), mirror)
            .DriveAsync(Game, "blitz-3", ready, CancellationToken.None);

        await Assert.ThrowsAsync<HttpRequestException>(async () => await ready.Task);
        Assert.Empty(mirror.Created);
    }

    [Fact]
    internal async Task Drive_StreamEndsBeforeGameStart_FaultsReady()
    {
        var provider = new FakeExternalProvider();
        var mirror = new FakeMatchMirror("unused");
        var ready = new TaskCompletionSource<string>();

        await Bridge(provider, new FakeEngineMoveSource("x"), mirror)
            .DriveAsync(Game, "blitz-3", ready, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await ready.Task);
        Assert.Empty(mirror.Created);
    }

    [Fact]
    internal async Task Start_ReturnsMatchIdOnceMirrorCreated()
    {
        var provider = new FakeExternalProvider(
            Update([], ChessPosition.StartFen, "white", "black"));
        var mirror = new FakeMatchMirror("match-3");

        string matchId = await Bridge(provider, new FakeEngineMoveSource("x"), mirror)
            .StartAsync("blitz-3", "lip_token", "game-7", CancellationToken.None);

        Assert.Equal("match-3", matchId);
    }
}
