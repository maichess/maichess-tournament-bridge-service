using System.Text.Json.Serialization;

namespace MaichessTournamentBridgeService.Models;

internal sealed record RegisteredBotsResponse(
    [property: JsonPropertyName("bots")] List<RegisteredBot> Bots);
