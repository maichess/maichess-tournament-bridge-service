using System.Text.Json.Serialization;

namespace MaichessTournamentBridgeService.Models;

internal sealed record TournamentEvent(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("round")] int Round = 0,
    [property: JsonPropertyName("gameId")] string? GameId = null,
    [property: JsonPropertyName("color")] string? Color = null,
    [property: JsonPropertyName("fen")] string? Fen = null,
    [property: JsonPropertyName("winner")] BotRef? Winner = null);
