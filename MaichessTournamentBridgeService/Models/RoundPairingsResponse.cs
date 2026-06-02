using System.Text.Json.Serialization;

namespace MaichessTournamentBridgeService.Models;

internal sealed record RoundPairingsResponse(
    [property: JsonPropertyName("round")] int Round,
    [property: JsonPropertyName("pairings")] List<TournamentPairing> Pairings);
