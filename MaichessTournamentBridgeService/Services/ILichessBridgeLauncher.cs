namespace MaichessTournamentBridgeService.Services;

// Starts driving one Lichess game and returns the match-db match id as soon as the
// mirror match exists (so the caller can return a watchable id immediately, while the
// game keeps playing in the background). A seam so the registration service is testable
// without a live provider.
internal interface ILichessBridgeLauncher
{
    Task<string> StartAsync(string botId, string token, string gameId, CancellationToken ct);
}
