using System.Diagnostics.CodeAnalysis;
using Maichess.Engine.V1;

namespace MaichessTournamentBridgeService.Services;

// Engine ListBots gRPC implementation of the bot catalog. Live gRPC I/O — excluded
// from coverage.
[ExcludeFromCodeCoverage]
internal sealed class EngineBotCatalog(Bots.BotsClient client) : IBotCatalog
{
    public async Task<bool> ExistsAsync(string botId, CancellationToken ct)
    {
        ListBotsResponse response = await client.ListBotsAsync(
            new ListBotsRequest(), cancellationToken: ct);
        return response.Bots.Any(b => b.Id == botId);
    }
}
