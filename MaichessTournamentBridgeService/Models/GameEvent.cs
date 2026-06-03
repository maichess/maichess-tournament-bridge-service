using System.Text.Json.Serialization;

namespace MaichessTournamentBridgeService.Models;

internal sealed record GameEvent(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("fen")] string? Fen = null,
    [property: JsonPropertyName("moves")] string? Moves = null,
    [property: JsonPropertyName("uci")] string? Uci = null,
    [property: JsonPropertyName("turn")] string? Turn = null,
    [property: JsonPropertyName("status")] string? Status = null,
    [property: JsonPropertyName("winner")] string? Winner = null,
    [property: JsonPropertyName("clock")] GameClock? Clock = null);
