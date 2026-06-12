using Maichess.MatchManager.V1;
using MaichessTournamentBridgeService.Services;
using Xunit;

namespace MaichessTournamentBridgeService.Tests;

public sealed class ExternalMatchMappingTests
{
    [Theory]
    [InlineData("ongoing", MatchStatus.Ongoing)]
    [InlineData("white_won", MatchStatus.WhiteWon)]
    [InlineData("black_won", MatchStatus.BlackWon)]
    [InlineData("draw", MatchStatus.Draw)]
    [InlineData("anything-else", MatchStatus.Ongoing)]
    internal void ToMatchStatus_Maps(string status, MatchStatus expected) =>
        Assert.Equal(expected, ExternalMatchMapping.ToMatchStatus(status));
}
