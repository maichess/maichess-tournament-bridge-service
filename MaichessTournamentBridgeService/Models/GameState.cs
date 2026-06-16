using System.Text.Json.Serialization;

namespace MaichessTournamentBridgeService.Models;

// Full game snapshot returned by GET /api/tournament/{id}/game/{gameId}. Used to
// seed the driver with the real starting position, clock, and turn instead of
// assuming a standard 5+0 game (which breaks custom openings).
internal sealed record GameState(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("fen")] string Fen,
    [property: JsonPropertyName("moves")] string Moves,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("turn")] string Turn,
    [property: JsonPropertyName("clock")] GameClock Clock,
    [property: JsonPropertyName("winner")] string? Winner = null,
    [property: JsonPropertyName("startPosition")] string? StartPosition = null);
