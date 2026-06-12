using Maichess.MatchManager.V1;

namespace MaichessTournamentBridgeService.Services;

// A single state update pushed to match-db for an external match. Status is the
// bridge's normalized vocabulary ("ongoing" | "white_won" | "black_won" | "draw");
// EndReason is only meaningful when Finished.
internal sealed record ExternalMatchSync(
    string MatchId,
    string Fen,
    string Status,
    IReadOnlyList<string> Moves,
    long WhiteTimeMs,
    long BlackTimeMs,
    bool Finished,
    EndReason EndReason);
