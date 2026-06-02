using System.Text.Json.Serialization;

namespace MaichessTournamentBridgeService.Models;

internal sealed record TournamentPairing(
    [property: JsonPropertyName("white")] BotRef White,
    [property: JsonPropertyName("black")] BotRef Black,
    [property: JsonPropertyName("gameId")] string GameId,
    [property: JsonPropertyName("winner")] string? Winner = null);
