namespace MaichessTournamentBridgeService.Services;

// Looks up whether a maichess bot exists. Backed by Engine ListBots gRPC in
// production; faked in registration tests.
internal interface IBotCatalog
{
    Task<bool> ExistsAsync(string botId, CancellationToken ct);
}
