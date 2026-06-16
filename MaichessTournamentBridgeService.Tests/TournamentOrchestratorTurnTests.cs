using MaichessTournamentBridgeService.Models;
using MaichessTournamentBridgeService.Services;
using Xunit;

namespace MaichessTournamentBridgeService.Tests;

public sealed class TournamentOrchestratorTurnTests
{
    [Theory]
    [InlineData("white", "white", true)]
    [InlineData("white", "black", false)]
    [InlineData("black", "black", true)]
    [InlineData("black", "white", false)]
    internal void IsOurTurn_UsesEventTurnField(string eventTurn, string ourColor, bool expected)
    {
        var evt = new GameEvent(Type: "move", Uci: "e2e4", Turn: eventTurn);
        Assert.Equal(expected, TournamentOrchestrator.IsOurTurn(evt, ourColor));
    }

    [Fact]
    internal void IsOurTurn_MissingTurn_IsNotOurTurn()
    {
        var evt = new GameEvent(Type: "move", Uci: "e2e4");
        Assert.False(TournamentOrchestrator.IsOurTurn(evt, "white"));
    }

    [Theory]
    [InlineData(300, 0, "5+0")]
    [InlineData(300, 3, "5+3")]
    [InlineData(600, 5, "10+5")]
    [InlineData(60, 1, "1+1")]
    internal void FormatTimeId_FormatsMinutesAndIncrement(int limit, int increment, string expected)
    {
        Assert.Equal(expected, TournamentOrchestrator.FormatTimeId(new TournamentClock(limit, increment)));
    }

    [Theory]
    [InlineData(60, 0, "bullet")]
    [InlineData(180, 0, "blitz")]
    [InlineData(300, 0, "blitz")]
    [InlineData(600, 0, "rapid")]
    [InlineData(1800, 0, "classical")]
    [InlineData(120, 2, "blitz")]
    internal void CategoryFor_ClassifiesByEstimatedDuration(int limit, int increment, string expected)
    {
        Assert.Equal(expected, TournamentOrchestrator.CategoryFor(new TournamentClock(limit, increment)));
    }
}
