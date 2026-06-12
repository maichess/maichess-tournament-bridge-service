using Maichess.MatchManager.V1;
using MaichessTournamentBridgeService.Providers.Lichess;
using Xunit;

namespace MaichessTournamentBridgeService.Tests;

public sealed class LichessStatusTests
{
    [Theory]
    [InlineData("created", true)]
    [InlineData("started", true)]
    [InlineData("mate", false)]
    [InlineData("draw", false)]
    internal void IsOngoing_OnlyCreatedAndStarted(string status, bool expected) =>
        Assert.Equal(expected, LichessStatus.IsOngoing(status));

    [Theory]
    [InlineData("started", null, "ongoing")]
    [InlineData("created", null, "ongoing")]
    [InlineData("mate", "white", "white_won")]
    [InlineData("resign", "black", "black_won")]
    [InlineData("stalemate", null, "draw")]
    [InlineData("draw", null, "draw")]
    internal void ToStatus_MapsLichessStatus(string status, string? winner, string expected) =>
        Assert.Equal(expected, LichessStatus.ToStatus(status, winner));

    [Theory]
    [InlineData("mate", EndReason.Checkmate)]
    [InlineData("resign", EndReason.Resignation)]
    [InlineData("stalemate", EndReason.Stalemate)]
    [InlineData("timeout", EndReason.Timeout)]
    [InlineData("outoftime", EndReason.Timeout)]
    [InlineData("draw", EndReason.DrawAgreement)]
    [InlineData("aborted", EndReason.Unspecified)]
    internal void ToEndReason_MapsLichessStatus(string status, EndReason expected) =>
        Assert.Equal(expected, LichessStatus.ToEndReason(status));
}
