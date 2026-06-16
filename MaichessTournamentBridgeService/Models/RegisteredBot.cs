using System.Text.Json.Serialization;

namespace MaichessTournamentBridgeService.Models;

// A bot permanently registered in the tournament server's /api/bots catalog.
// Its id is auth-backed, so the bridge can drive it after it joins a tournament.
internal sealed record RegisteredBot(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("endpoint")] string? Endpoint = null);
