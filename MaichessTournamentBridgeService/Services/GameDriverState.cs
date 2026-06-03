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

    internal bool IsFinished => Status is not "ongoing" and not "started";
}
