using MaichessTournamentBridgeService.Models;
using MaichessTournamentBridgeService.Services;
using Xunit;

namespace MaichessTournamentBridgeService.Tests;

public sealed class GameDriverTests
{
    private const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    private static GameDriverState NewState(string ourColor, string turnColor = "white") => new(
        TournamentId: "t1",
        GameId: "g1",
        MatchDbMatchId: "m1",
        OurColor: ourColor,
        OurBotToken: "tok",
        Moves: [],
        CurrentFen: StartFen,
        Status: "ongoing",
        TurnColor: turnColor,
        WhiteTimeMs: 300_000,
        BlackTimeMs: 300_000);

    [Theory]
    [InlineData("white", "white", true)]
    [InlineData("white", "black", false)]
    [InlineData("black", "black", true)]
    [InlineData("black", "white", false)]
    internal void IsOurTurn_FollowsTurnColor(string ourColor, string turnColor, bool expected)
    {
        GameDriverState state = NewState(ourColor, turnColor);
        Assert.Equal(expected, state.IsOurTurn);
    }

    [Fact]
    internal void DetermineAction_FinishedGame_Finalizes()
    {
        GameDriverState state = NewState("white") with { Status = "white_won" };
        Assert.Equal(GameDriverAction.FinalizeMatch, GameDriver.DetermineAction(state));
    }

    [Fact]
    internal void DetermineAction_OurTurn_RequestsMove()
    {
        GameDriverState state = NewState("white", turnColor: "white");
        Assert.Equal(GameDriverAction.RequestEngineMove, GameDriver.DetermineAction(state));
    }

    [Fact]
    internal void DetermineAction_OpponentTurn_Waits()
    {
        GameDriverState state = NewState("white", turnColor: "black");
        Assert.Equal(GameDriverAction.WaitForOpponent, GameDriver.DetermineAction(state));
    }

    // Regression test for the deadlock: a "move" event from the tournament
    // server carries only `uci` + `turn` (no `moves` string). The driver must
    // still recognise that it is now our turn. Before the fix, turn was
    // inferred from a move-count parity that never advanced past the opening,
    // so a black bot never saw its turn after White's first move.
    [Fact]
    internal void ApplyGameEvent_MoveEvent_AdvancesTurnForBlack()
    {
        GameDriverState state = NewState("black", turnColor: "white");

        var whiteOpening = new GameEvent(
            Type: "move",
            Fen: "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1",
            Uci: "e2e4",
            Turn: "black",
            Clock: new GameClock(299.0, 300.0));

        GameDriverState next = GameDriver.ApplyGameEvent(state, whiteOpening);

        Assert.Equal(["e2e4"], next.Moves);
        Assert.Equal("black", next.TurnColor);
        Assert.True(next.IsOurTurn);
        Assert.Equal(GameDriverAction.RequestEngineMove, GameDriver.DetermineAction(next));
    }

    [Fact]
    internal void ApplyGameEvent_MoveEvent_AppendsUciAndUpdatesClock()
    {
        GameDriverState state = NewState("white") with { Moves = ["e2e4"], TurnColor = "black" };

        var blackReply = new GameEvent(
            Type: "move",
            Fen: "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2",
            Uci: "e7e5",
            Turn: "white",
            Clock: new GameClock(299.0, 298.5));

        GameDriverState next = GameDriver.ApplyGameEvent(state, blackReply);

        Assert.Equal(["e2e4", "e7e5"], next.Moves);
        Assert.Equal("white", next.TurnColor);
        Assert.True(next.IsOurTurn);
        Assert.Equal(298_500, next.BlackTimeMs);
        Assert.Equal(299_000, next.WhiteTimeMs);
    }

    [Fact]
    internal void ApplyGameEvent_GameStateSnapshot_ReplacesMoveList()
    {
        GameDriverState state = NewState("white") with { Moves = ["stale"] };

        var snapshot = new GameEvent(
            Type: "gameState",
            Fen: "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2",
            Moves: "e2e4 e7e5",
            Turn: "white",
            Status: "ongoing");

        GameDriverState next = GameDriver.ApplyGameEvent(state, snapshot);

        Assert.Equal(["e2e4", "e7e5"], next.Moves);
        Assert.Equal("white", next.TurnColor);
    }

    [Fact]
    internal void ApplyGameEvent_EventWithoutMoveData_KeepsState()
    {
        GameDriverState state = NewState("white") with { Moves = ["e2e4"], TurnColor = "black" };

        var noData = new GameEvent(Type: "move");

        GameDriverState next = GameDriver.ApplyGameEvent(state, noData);

        Assert.Equal(["e2e4"], next.Moves);
        Assert.Equal("black", next.TurnColor);
        Assert.Equal(StartFen, next.CurrentFen);
    }

    [Theory]
    [InlineData("white", "white_won")]
    [InlineData("black", "black_won")]
    [InlineData(null, "draw")]
    internal void ApplyGameEvent_GameEnd_MapsWinnerToStatus(string? winner, string expectedStatus)
    {
        GameDriverState state = NewState("white") with { Moves = ["e2e4"], TurnColor = "black" };

        var end = new GameEvent(Type: "gameEnd", Winner: winner);

        GameDriverState next = GameDriver.ApplyGameEvent(state, end);

        Assert.Equal(expectedStatus, next.Status);
        Assert.True(next.IsFinished);
        // gameEnd carries no turn; the prior turn is preserved untouched.
        Assert.Equal("black", next.TurnColor);
        Assert.Equal(GameDriverAction.FinalizeMatch, GameDriver.DetermineAction(next));
    }

    [Theory]
    [InlineData("white", "white_won")]
    [InlineData("black", "black_won")]
    [InlineData("none", "draw")]
    [InlineData(null, "draw")]
    internal void MapWinnerToStatus_Maps(string? winner, string expected) =>
        Assert.Equal(expected, GameDriver.MapWinnerToStatus(winner));

    [Fact]
    internal void ComputeTimeLimitMs_EarlyGame_BudgetsAcrossEstimatedMoves()
    {
        long limit = GameDriver.ComputeTimeLimitMs(300_000, 0);
        Assert.Equal(7_500, limit);
    }

    [Fact]
    internal void ComputeTimeLimitMs_NeverBelowFloor()
    {
        long limit = GameDriver.ComputeTimeLimitMs(200, 0);
        Assert.Equal(500, limit);
    }

    [Fact]
    internal void ComputeTimeLimitMs_NeverSpendsMoreThanHalfRemaining()
    {
        long limit = GameDriver.ComputeTimeLimitMs(1_000, 100);
        Assert.Equal(500, limit);
    }
}
