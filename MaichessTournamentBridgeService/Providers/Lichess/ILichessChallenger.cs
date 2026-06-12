using MaichessTournamentBridgeService.Models;

namespace MaichessTournamentBridgeService.Providers.Lichess;

// Creates a Lichess game by issuing a challenge, returning the game id to drive. Separate
// from IExternalProvider (which is the generic, provider-agnostic game-drive seam) because
// challenge creation is Lichess-specific. Faked in the registration tests.
internal interface ILichessChallenger
{
    Task<string> CreateChallengeAsync(string token, LichessChallenge challenge, CancellationToken ct);
}
