using System.Text.Json.Serialization;

namespace MaichessTournamentBridgeService.Models;

internal sealed record TournamentClock(
    [property: JsonPropertyName("limit")] int Limit,
    [property: JsonPropertyName("increment")] int Increment);
