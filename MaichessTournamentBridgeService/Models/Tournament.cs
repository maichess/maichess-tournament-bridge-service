using System.Text.Json.Serialization;

namespace MaichessTournamentBridgeService.Models;

internal sealed record Tournament(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("fullName")] string FullName,
    [property: JsonPropertyName("clock")] TournamentClock Clock,
    [property: JsonPropertyName("nbPlayers")] int NbPlayers,
    [property: JsonPropertyName("nbRounds")] int NbRounds,
    [property: JsonPropertyName("format")] string Format,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("round")] int Round,
    [property: JsonPropertyName("standing")] TournamentStanding? Standing = null);
