using MaichessTournamentBridgeService.Models;
using MaichessTournamentBridgeService.Providers.Lichess;

namespace MaichessTournamentBridgeService.Services;

// Validates a Lichess request and, if valid, starts driving the game. `RegisterAsync`
// attaches to an existing game id; `ChallengeAsync` first creates the game by challenging
// an opponent (a user, or "ai" for Stockfish), then drives the returned game. Pure
// orchestration over the catalog + challenger + launcher seams (no transport), so it is
// fully unit-testable. The REST endpoint maps the outcome to an HTTP status.
internal sealed class LichessRegistrationService(
    IBotCatalog bots, ILichessChallenger challenger, ILichessBridgeLauncher launcher)
{
    internal async Task<LichessRegistrationResult> RegisterAsync(
        string? botId, string? token, string? gameId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(gameId))
        {
            return Fail(LichessRegistrationOutcome.MissingGame, "game_id is required");
        }

        LichessRegistrationResult? invalid = await ValidateBotAndTokenAsync(botId, token, ct);
        return invalid ?? await DriveAsync(botId!, token!, gameId, ct);
    }

    internal async Task<LichessRegistrationResult> ChallengeAsync(
        string? botId, string? token, LichessChallenge challenge, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(challenge.Opponent))
        {
            return Fail(LichessRegistrationOutcome.MissingOpponent, "opponent is required");
        }

        LichessRegistrationResult? invalid = await ValidateBotAndTokenAsync(botId, token, ct);
        if (invalid is not null)
        {
            return invalid;
        }

        string gameId;
        try
        {
            gameId = await challenger.CreateChallengeAsync(token!, challenge, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Fail(
                LichessRegistrationOutcome.ProviderError,
                $"Lichess challenge could not be created: {ex.Message}");
        }

        return await DriveAsync(botId!, token!, gameId, ct);
    }

    private static LichessRegistrationResult Fail(LichessRegistrationOutcome outcome, string error) =>
        new(outcome, null, error);

    private async Task<LichessRegistrationResult?> ValidateBotAndTokenAsync(
        string? botId, string? token, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(botId)
            ? Fail(LichessRegistrationOutcome.MissingBot, "bot_id is required")
            : string.IsNullOrWhiteSpace(token)
                ? Fail(LichessRegistrationOutcome.MissingToken, "lichess_token is required")
                : !await bots.ExistsAsync(botId, ct)
                    ? Fail(LichessRegistrationOutcome.UnknownBot, $"Unknown bot: {botId}")
                    : null;

    private async Task<LichessRegistrationResult> DriveAsync(
        string botId, string token, string gameId, CancellationToken ct)
    {
        try
        {
            string matchId = await launcher.StartAsync(botId, token, gameId, ct);
            return new LichessRegistrationResult(LichessRegistrationOutcome.Created, matchId, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The game stream failed before the mirror match was created (e.g. an unknown
            // game id, a revoked token, or a challenge that was never accepted). Surfaced
            // as a gateway error.
            return Fail(
                LichessRegistrationOutcome.ProviderError,
                $"Lichess game could not be started: {ex.Message}");
        }
    }
}
