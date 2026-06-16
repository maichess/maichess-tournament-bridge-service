using System.Text.Json.Serialization;

namespace MaichessTournamentBridgeService.Models;

internal sealed record OpeningsResponse(
    [property: JsonPropertyName("openings")] List<Opening> Openings);
