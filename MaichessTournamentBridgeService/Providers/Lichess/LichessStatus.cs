using Maichess.MatchManager.V1;
using MaichessTournamentBridgeService.Services;

namespace MaichessTournamentBridgeService.Providers.Lichess;

// Maps Lichess game statuses (https://lichess.org/api#tag/Bot) onto the bridge's
// vocabularies. Pure.
internal static class LichessStatus
{
    // Lichess statuses that mean the game is still in progress.
    internal static bool IsOngoing(string status) => status is "created" or "started";

    // Normalized result status used by GameDriverState ("ongoing" | "white_won" |
    // "black_won" | "draw"). For finished games we reuse GameDriver's winner mapping.
    internal static string ToStatus(string status, string? winner) =>
        IsOngoing(status) ? "ongoing" : GameDriver.MapWinnerToStatus(winner);

    // Proto end reason for SyncExternalMatch when the game has finished.
    internal static EndReason ToEndReason(string status) => status switch
    {
        "mate" => EndReason.Checkmate,
        "resign" => EndReason.Resignation,
        "stalemate" => EndReason.Stalemate,
        "timeout" or "outoftime" => EndReason.Timeout,
        "draw" => EndReason.DrawAgreement,
        _ => EndReason.Unspecified,
    };
}
