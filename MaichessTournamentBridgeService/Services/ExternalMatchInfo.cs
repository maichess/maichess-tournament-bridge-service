namespace MaichessTournamentBridgeService.Services;

// Everything needed to create the read-only mirror match in match-db for an external
// game. The opponent is always an external (non-maichess) participant.
internal sealed record ExternalMatchInfo(
    string ProviderName,
    string ExternalRef,
    string OurColor,
    string OurBotId,
    string OpponentName,
    long BaseMs,
    long IncrementMs);
