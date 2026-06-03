using MaichessTournamentBridgeService.Models;

namespace MaichessTournamentBridgeService.Services;

internal static class GameDriver
{
    internal static GameDriverAction DetermineAction(GameDriverState state) =>
        state.IsFinished ? GameDriverAction.FinalizeMatch
        : state.IsOurTurn ? GameDriverAction.RequestEngineMove
        : GameDriverAction.WaitForOpponent;

    internal static GameDriverState ApplyGameEvent(GameDriverState state, GameEvent evt)
    {
        string fen = evt.Fen ?? state.CurrentFen;
        List<string> moves = string.IsNullOrEmpty(evt.Moves)
            ? state.Moves
            : [.. evt.Moves.Split(' ', StringSplitOptions.RemoveEmptyEntries)];

        string status = evt.Type switch
        {
            "gameFinish" or "gameEnd" => MapWinnerToStatus(evt.Winner),
            _ => evt.Status ?? state.Status,
        };

        long whiteTimeMs = evt.Clock is not null ? (long)(evt.Clock.WhiteTime * 1000) : state.WhiteTimeMs;
        long blackTimeMs = evt.Clock is not null ? (long)(evt.Clock.BlackTime * 1000) : state.BlackTimeMs;

        return state with
        {
            Moves = moves,
            CurrentFen = fen,
            Status = status,
            WhiteTimeMs = whiteTimeMs,
            BlackTimeMs = blackTimeMs,
        };
    }

    internal static string MapWinnerToStatus(string? winner) => winner switch
    {
        "white" => "white_won",
        "black" => "black_won",
        _ => "draw",
    };

    internal static long ComputeTimeLimitMs(long remainingMs, int moveCount)
    {
        int estimatedMovesLeft = Math.Max(1, 40 - (moveCount / 2));
        long limit = remainingMs / estimatedMovesLeft;
        return Math.Max(500, Math.Min(limit, remainingMs / 2));
    }
}
