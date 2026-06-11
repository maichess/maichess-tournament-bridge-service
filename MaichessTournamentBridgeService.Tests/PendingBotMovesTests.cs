using MaichessTournamentBridgeService.Kafka;
using Xunit;

namespace MaichessTournamentBridgeService.Tests;

public sealed class PendingBotMovesTests
{
    [Fact]
    internal async Task Complete_ResolvesTheRegisteredWaiter()
    {
        var pending = new PendingBotMoves();
        Task<string> reply = pending.Register("r1");

        bool delivered = pending.Complete("r1", "e2e4");

        Assert.True(delivered);
        Assert.Equal("e2e4", await reply);
    }

    [Fact]
    internal void Complete_UnknownRequestId_ReturnsFalse()
    {
        var pending = new PendingBotMoves();

        Assert.False(pending.Complete("missing", "e2e4"));
    }

    [Fact]
    internal void Complete_Twice_OnlyFirstDelivers()
    {
        var pending = new PendingBotMoves();
        _ = pending.Register("r1");

        Assert.True(pending.Complete("r1", "e2e4"));
        Assert.False(pending.Complete("r1", "d2d4"));
    }

    [Fact]
    internal void Cancel_PreventsLaterCompletion()
    {
        var pending = new PendingBotMoves();
        Task<string> reply = pending.Register("r1");

        pending.Cancel("r1");

        Assert.False(pending.Complete("r1", "e2e4"));
        Assert.False(reply.IsCompleted);
    }
}
