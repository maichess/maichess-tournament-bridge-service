using System.Text.Json.Serialization;

namespace MaichessTournamentBridgeService.Models;

// Request body for POST /api/bots — permanently register a bot in the tournament
// server's catalog. Only `name` is required; the optional metadata fields are
// analytics-grouping hints carried through into the server's analytics export.
// Null fields are omitted from the wire payload (DefaultIgnoreCondition).
internal sealed record RegisterBotRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("endpoint")] string? Endpoint = null,
    [property: JsonPropertyName("family")] string? Family = null,
    [property: JsonPropertyName("strategyType")] string? StrategyType = null,
    [property: JsonPropertyName("engineType")] string? EngineType = null,
    [property: JsonPropertyName("modelVersion")] string? ModelVersion = null);
