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
    long WhiteTimeMs,
    long BlackTimeMs)
{
    internal bool IsOurTurn
    {
        get
        {
            bool whiteToMove = Moves.Count % 2 == 0;
            return OurColor == "white" ? whiteToMove : !whiteToMove;
        }
    }

    internal bool IsFinished => Status is not "ongoing" and not "started";
}
