using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using MaichessTournamentBridgeService.Models;
using MaichessTournamentBridgeService.Services;

namespace MaichessTournamentBridgeService.Rest;

// REST surface for external (non-tournament) games. Currently just Lichess: register a
// maichess bot to drive an existing Lichess game and mirror it into match-db. Thin
// adapter over LichessRegistrationService — excluded from coverage.
[ExcludeFromCodeCoverage]
internal static class ExternalGameEndpoints
{
    internal static void MapExternalGameEndpoints(this WebApplication app)
    {
        app.MapPost("/external/lichess", RegisterLichess).RequireAuthorization();
        app.MapPost("/external/lichess/challenge", ChallengeLichess).RequireAuthorization();
    }

    private static async Task<IResult> RegisterLichess(
        HttpRequest httpRequest,
        LichessRegistrationService service,
        CancellationToken ct)
    {
        using JsonDocument body = await JsonDocument.ParseAsync(httpRequest.Body, cancellationToken: ct);
        JsonElement root = body.RootElement;

        LichessRegistrationResult result = await service.RegisterAsync(
            GetString(root, "bot_id"),
            GetString(root, "lichess_token"),
            GetString(root, "game_id"),
            ct);

        return ToResult(result);
    }

    private static async Task<IResult> ChallengeLichess(
        HttpRequest httpRequest,
        LichessRegistrationService service,
        CancellationToken ct)
    {
        using JsonDocument body = await JsonDocument.ParseAsync(httpRequest.Body, cancellationToken: ct);
        JsonElement root = body.RootElement;

        var challenge = new LichessChallenge(
            Opponent: GetString(root, "opponent") ?? string.Empty,
            ClockLimitSeconds: GetInt(root, "clock_limit", 300),
            ClockIncrementSeconds: GetInt(root, "clock_increment", 0),
            Rated: GetBool(root, "rated", false),
            Level: GetInt(root, "level", 1));

        LichessRegistrationResult result = await service.ChallengeAsync(
            GetString(root, "bot_id"),
            GetString(root, "lichess_token"),
            challenge,
            ct);

        return ToResult(result);
    }

    private static IResult ToResult(LichessRegistrationResult result) => result.Outcome switch
    {
        LichessRegistrationOutcome.Created =>
            Results.Ok(new { match_id = result.MatchId, provider = "lichess" }),
        LichessRegistrationOutcome.ProviderError =>
            Results.Json(new { error = result.Error }, statusCode: 502),
        _ => Results.BadRequest(new { error = result.Error }),
    };

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int GetInt(JsonElement root, string name, int fallback) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : fallback;

    private static bool GetBool(JsonElement root, string name, bool fallback) =>
        root.TryGetProperty(name, out JsonElement value)
        && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;
}
