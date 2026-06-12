namespace MaichessTournamentBridgeService.Providers;

// Identifies one external game to a provider. ServerUrl/TournamentId are only used by
// the tournament-server provider; the Lichess provider needs just the game id and the
// per-game bot OAuth token.
internal sealed record ExternalGameRef(
    string GameId,
    string Token,
    string ServerUrl = "",
    string TournamentId = "");
