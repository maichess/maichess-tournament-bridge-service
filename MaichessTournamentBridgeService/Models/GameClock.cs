using System.Text.Json.Serialization;

namespace MaichessTournamentBridgeService.Models;

internal sealed record GameClock(
    [property: JsonPropertyName("whiteTime")] double WhiteTime,
    [property: JsonPropertyName("blackTime")] double BlackTime);
