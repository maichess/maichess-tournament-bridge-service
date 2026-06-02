using System.Text.Json.Serialization;

namespace MaichessTournamentBridgeService.Models;

internal sealed record BotRef(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name);
