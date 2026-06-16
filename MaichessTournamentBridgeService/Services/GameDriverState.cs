namespace MaichessTournamentBridgeService.Services;

internal sealed record GameDriverState(
    string TournamentId,
    string GameId,
    string MatchDbMatchId,
    string OurColor,
    string OurBotToken,
    List<string> Moves,
    string CurrentFen,
    string Status,
    string TurnColor,
    long WhiteTimeMs,
    long BlackTimeMs)
{
    internal bool IsOurTurn => TurnColor == OurColor;

    // A game the tournament server created but has not yet activated (held back by
    // the round's maxConcurrentGames cap). It is neither finished nor playable —
    // moves against it are rejected — so the driver must simply wait. (Added with
    // the tournament-server "pending games" API update.)
    internal bool IsPending => Status is "pending";

    internal bool IsFinished => Status is not "ongoing" and not "started" and not "pending";
}
