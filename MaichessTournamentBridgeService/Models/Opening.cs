using System.Text.Json.Serialization;

namespace MaichessTournamentBridgeService.Models;

internal sealed record Opening(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("fen")] string Fen);
