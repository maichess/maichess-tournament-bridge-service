using System.Globalization;
using System.Text.Json;
using MaichessTournamentBridgeService.Models;

namespace MaichessTournamentBridgeService.Providers.Lichess;

// Pure construction of a Lichess challenge request and extraction of the resulting game
// id. Two shapes: `POST /api/challenge/ai` (play Stockfish — the game starts immediately
// and the response is a full game object) and `POST /api/challenge/{username}` (challenge
// a user/bot — the response is the challenge, whose id becomes the game id once accepted).
internal static class LichessChallengeBuilder
{
    internal static bool IsAi(LichessChallenge challenge) =>
        challenge.Opponent.Equals("ai", StringComparison.OrdinalIgnoreCase);

    internal static string BuildPath(LichessChallenge challenge) =>
        IsAi(challenge) ? "/api/challenge/ai" : $"/api/challenge/{challenge.Opponent}";

    internal static Dictionary<string, string> BuildForm(LichessChallenge challenge)
    {
        var form = new Dictionary<string, string>
        {
            ["clock.limit"] = challenge.ClockLimitSeconds.ToString(CultureInfo.InvariantCulture),
            ["clock.increment"] = challenge.ClockIncrementSeconds.ToString(CultureInfo.InvariantCulture),
            ["color"] = "random",
        };

        if (IsAi(challenge))
        {
            form["level"] = Math.Clamp(challenge.Level, 1, 8).ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            form["rated"] = challenge.Rated ? "true" : "false";
        }

        return form;
    }

    // Pulls the game id from a challenge/game response, tolerating the AI shape
    // (top-level `id`), the user-challenge shape (`challenge.id`), and a `game.id` wrapper.
    internal static string ParseGameId(string json)
    {
        using var doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        return TryReadId(root, out string? topLevel) ? topLevel
            : root.TryGetProperty("challenge", out JsonElement challenge)
              && TryReadId(challenge, out string? challengeId) ? challengeId
            : root.TryGetProperty("game", out JsonElement game)
              && TryReadId(game, out string? gameId) ? gameId
            : throw new InvalidOperationException(
                "Lichess challenge response did not contain a game id");
    }

    private static bool TryReadId(JsonElement element, out string id)
    {
        if (element.TryGetProperty("id", out JsonElement value)
            && value.ValueKind == JsonValueKind.String)
        {
            id = value.GetString()!;
            return true;
        }

        id = string.Empty;
        return false;
    }
}
