namespace MaichessTournamentBridgeService.Services;

// Outcome of a Lichess registration attempt. On success MatchId is the watchable
// match-db id; otherwise Error carries a client-facing message.
internal sealed record LichessRegistrationResult(
    LichessRegistrationOutcome Outcome,
    string? MatchId,
    string? Error);
