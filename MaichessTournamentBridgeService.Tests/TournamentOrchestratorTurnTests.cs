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
}
