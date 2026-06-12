using System.Text.Json;
using MaichessTournamentBridgeService.Chess;
using MaichessTournamentBridgeService.Models;

namespace MaichessTournamentBridgeService.Providers.Lichess;

// Pure parsing of the Lichess bot game stream (NDJSON) into the bridge's GameUpdate.
// The stream opens with one `gameFull` (carrying players + initialFen + the opening
// `state`), then emits a `gameState` per move; `chatLine`/`opponentGone`/etc. are
// ignored. Lichess never sends a FEN, so the current position is reconstructed from
// initialFen + the UCI move list via ChessPosition. Clocks are milliseconds already —
// passed through verbatim, with no seconds→ms conversion.
internal static class LichessEventParser
{
    // Our colour, by matching our bot account's Lichess id against the white seat.
    internal static string ResolveColor(JsonElement gameFull, string ourAccountId) =>
        gameFull.TryGetProperty("white", out JsonElement white)
        && white.TryGetProperty("id", out JsonElement id)
        && string.Equals(id.GetString(), ourAccountId, StringComparison.OrdinalIgnoreCase)
            ? "white"
            : "black";

    internal static string ResolveInitialFen(JsonElement gameFull) =>
        ChessPosition.NormalizeFen(
            gameFull.TryGetProperty("initialFen", out JsonElement fen) ? fen.GetString() : null);

    // Display name of the opponent (the seat that is not ours), falling back for AI or
    // anonymous opponents that carry no name.
    internal static string ResolveOpponentName(JsonElement gameFull, string ourColor)
    {
        string opponentSeat = ourColor == "white" ? "black" : "white";
        if (gameFull.TryGetProperty(opponentSeat, out JsonElement seat))
        {
            if (seat.TryGetProperty("name", out JsonElement name)
                && name.ValueKind == JsonValueKind.String)
            {
                return name.GetString()!;
            }

            if (seat.TryGetProperty("aiLevel", out JsonElement ai))
            {
                return $"Stockfish level {ai.GetInt32()}";
            }
        }

        return "Lichess opponent";
    }

    // Parses one NDJSON line. Returns null for events that do not change game state
    // (chat, presence, etc.). gameFull is resolved against its nested `state`.
    internal static GameUpdate? Parse(
        string line, string initialFen, string ourColor, string opponentName)
    {
        using var doc = JsonDocument.Parse(line);
        JsonElement root = doc.RootElement;
        string type = root.GetProperty("type").GetString() ?? string.Empty;

        return type switch
        {
            "gameFull" => BuildUpdate(
                root.GetProperty("state"), initialFen, ourColor, opponentName),
            "gameState" => BuildUpdate(root, initialFen, ourColor, opponentName),
            _ => null,
        };
    }

    // Builds a GameUpdate from a `gameState` object (also the `state` child of gameFull).
    internal static GameUpdate BuildUpdate(
        JsonElement state, string initialFen, string ourColor, string opponentName)
    {
        string movesText = state.TryGetProperty("moves", out JsonElement moves)
            ? moves.GetString() ?? string.Empty
            : string.Empty;
        string[] moveList = movesText.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var position = ChessPosition.Replay(initialFen, moveList);

        string status = state.TryGetProperty("status", out JsonElement st)
            ? st.GetString() ?? "started"
            : "started";
        string? winner = state.TryGetProperty("winner", out JsonElement w)
            ? w.GetString()
            : null;

        return new GameUpdate(
            Moves: moveList,
            Fen: position.ToFen(),
            Turn: position.SideToMove,
            Status: LichessStatus.ToStatus(status, winner),
            RawStatus: status,
            WhiteTimeMs: ReadClock(state, "wtime"),
            BlackTimeMs: ReadClock(state, "btime"),
            OurColor: ourColor,
            OpponentName: opponentName);
    }

    private static long ReadClock(JsonElement state, string field) =>
        state.TryGetProperty(field, out JsonElement value) ? value.GetInt64() : 0L;
}
