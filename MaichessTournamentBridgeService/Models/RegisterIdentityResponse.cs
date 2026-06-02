using System.Text.Json.Serialization;

namespace MaichessTournamentBridgeService.Models;

internal sealed record RegisterIdentityResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("token")] string Token);
