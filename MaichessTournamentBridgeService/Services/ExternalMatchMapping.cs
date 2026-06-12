using Maichess.MatchManager.V1;

namespace MaichessTournamentBridgeService.Services;

// Pure mapping from the bridge's normalized status vocabulary to match-manager proto
// enums, shared by the mirror implementation.
internal static class ExternalMatchMapping
{
    internal static MatchStatus ToMatchStatus(string status) => status switch
    {
        "white_won" => MatchStatus.WhiteWon,
        "black_won" => MatchStatus.BlackWon,
        "draw" => MatchStatus.Draw,
        _ => MatchStatus.Ongoing,
    };
}
