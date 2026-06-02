using System.Text.Json.Serialization;

namespace MaichessTournamentBridgeService.Models;

internal sealed record TournamentResult(
    [property: JsonPropertyName("rank")] int Rank,
    [property: JsonPropertyName("points")] double Points,
    [property: JsonPropertyName("tieBreak")] double TieBreak,
    [property: JsonPropertyName("bot")] BotRef Bot,
    [property: JsonPropertyName("nbGames")] int NbGames,
    [property: JsonPropertyName("wins")] int Wins,
    [property: JsonPropertyName("draws")] int Draws,
    [property: JsonPropertyName("losses")] int Losses);
