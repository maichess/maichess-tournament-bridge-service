using MaichessTournamentBridgeService.Models;

namespace MaichessTournamentBridgeService.Services;

internal static class GameDriver
{
    // The tournament server interleaves a JSON heartbeat line
    // (`{"type":"heartbeat"}`) into every NDJSON stream every ~10s to keep idle
    // connections alive. The contract says clients must ignore it. A heartbeat
    // carries no move/turn/status, so feeding it through ApplyGameEvent leaves the
    // state unchanged — and DetermineAction would then re-fire RequestEngineMove
    // for a move we have already submitted (the echo of which has not yet arrived),
    // double-submitting an out-of-turn move that the server rejects and killing the
    // game drive. Skipping non-actionable events before deciding avoids that race.
    internal static bool IsActionable(GameEvent evt) => evt.Type != "heartbeat";

    internal static GameDriverAction DetermineAction(GameDriverState state) =>
        state.IsFinished ? GameDriverAction.FinalizeMatch
        : state.IsPending ? GameDriverAction.WaitForOpponent
        : state.IsOurTurn ? GameDriverAction.RequestEngineMove
        : GameDriverAction.WaitForOpponent;

    internal static GameDriverState ApplyGameEvent(GameDriverState state, GameEvent evt)
    {
        string fen = evt.Fen ?? state.CurrentFen;
        List<string> moves = NextMoves(state, evt);

        string status = evt.Type switch
        {
            "gameFinish" or "gameEnd" => MapWinnerToStatus(evt.Winner),
            _ => evt.Status ?? state.Status,
        };

        string turnColor = evt.Turn ?? state.TurnColor;

        long whiteTimeMs = evt.Clock is not null ? (long)(evt.Clock.WhiteTime * 1000) : state.WhiteTimeMs;
        long blackTimeMs = evt.Clock is not null ? (long)(evt.Clock.BlackTime * 1000) : state.BlackTimeMs;

        return state with
        {
            Moves = moves,
            CurrentFen = fen,
            Status = status,
            TurnColor = turnColor,
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

    // The tournament server's per-move events ("move") carry only the single
    // `uci` just played, while a full "gameState" snapshot carries the whole
    // `moves` string. Replace from a snapshot, otherwise append the new move.
    private static List<string> NextMoves(GameDriverState state, GameEvent evt) =>
        !string.IsNullOrEmpty(evt.Moves) ? [.. evt.Moves.Split(' ', StringSplitOptions.RemoveEmptyEntries)]
        : !string.IsNullOrEmpty(evt.Uci) ? [.. state.Moves, evt.Uci]
        : state.Moves;
}
