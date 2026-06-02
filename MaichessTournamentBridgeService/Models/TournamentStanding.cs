using System.Text.Json.Serialization;

namespace MaichessTournamentBridgeService.Models;

internal sealed record TournamentStanding(
    [property: JsonPropertyName("page")] int Page,
    [property: JsonPropertyName("players")] List<TournamentResult> Players);
