using System.Text.Json.Serialization;

namespace MaichessTournamentBridgeService.Models;

internal sealed record TournamentInfo(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("fullName")] string FullName,
    [property: JsonPropertyName("clock")] TournamentClock Clock,
    [property: JsonPropertyName("nbPlayers")] int NbPlayers,
    [property: JsonPropertyName("nbRounds")] int NbRounds,
    [property: JsonPropertyName("format")] string Format,
    [property: JsonPropertyName("matchesPerPairing")] int MatchesPerPairing,
    [property: JsonPropertyName("startPosition")] string StartPosition,
    [property: JsonPropertyName("createdBy")] string CreatedBy,
    [property: JsonPropertyName("status")] string? Status = null);
