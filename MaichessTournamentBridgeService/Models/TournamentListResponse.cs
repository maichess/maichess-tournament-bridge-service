using System.Text.Json.Serialization;

namespace MaichessTournamentBridgeService.Models;

internal sealed record TournamentListResponse(
    [property: JsonPropertyName("created")] List<TournamentInfo> Created,
    [property: JsonPropertyName("started")] List<TournamentInfo> Started,
    [property: JsonPropertyName("finished")] List<TournamentInfo> Finished);
