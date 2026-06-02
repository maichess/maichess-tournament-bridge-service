using System.Text.Json.Serialization;

namespace MaichessTournamentBridgeService.Models;

internal sealed record RegisterIdentityRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("isBot")] bool IsBot);
