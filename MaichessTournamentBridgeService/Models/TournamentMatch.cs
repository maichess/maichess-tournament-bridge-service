using System.Text.Json.Serialization;

namespace MaichessTournamentBridgeService.Models;

internal sealed record TournamentMatch(
    [property: JsonPropertyName("gameId")] string GameId,
    [property: JsonPropertyName("outcome")] string? Outcome = null,
    [property: JsonPropertyName("moves")] string? Moves = null);
