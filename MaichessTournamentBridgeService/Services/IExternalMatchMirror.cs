namespace MaichessTournamentBridgeService.Services;

// Mirrors an external game into match-db. Deliberately exposes only create + sync and
// no result-recording: external games are unrated by construction (RecordMatchResult
// is never reachable through this seam). Backed by Match Manager gRPC in production;
// faked in tests so the bridge loop is verifiable without a live match-manager.
internal interface IExternalMatchMirror
{
    // Creates the read-only EXTERNAL match and returns its match-db id.
    Task<string> CreateAsync(ExternalMatchInfo info, CancellationToken ct);

    // Pushes the latest external game state to match-db.
    Task SyncAsync(ExternalMatchSync sync, CancellationToken ct);
}
