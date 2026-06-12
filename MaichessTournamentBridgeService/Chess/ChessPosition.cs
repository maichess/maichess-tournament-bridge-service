using System.Text;

namespace MaichessTournamentBridgeService.Chess;

// Pure chess-board state that replays UCI moves into a FEN. The Lichess bot stream
// (unlike the tournament server) never sends a FEN — only the running UCI move list
// plus the game's initial position — so the bridge must reconstruct the position
// itself before it can ask the Engine for a move. Moves are trusted to be legal
// (Lichess already validated them); this type only does the mechanical board update
// and the FEN bookkeeping (castling rights, en passant, half/full-move counters).
internal sealed class ChessPosition
{
    internal const string StartFen =
        "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    private readonly char[] _board;

    private ChessPosition(
        char[] board,
        bool whiteToMove,
        bool whiteKingSide,
        bool whiteQueenSide,
        bool blackKingSide,
        bool blackQueenSide,
        int enPassant,
        int halfMoveClock,
        int fullMoveNumber)
    {
        _board = board;
        WhiteToMove = whiteToMove;
        WhiteKingSide = whiteKingSide;
        WhiteQueenSide = whiteQueenSide;
        BlackKingSide = blackKingSide;
        BlackQueenSide = blackQueenSide;
        EnPassant = enPassant;
        HalfMoveClock = halfMoveClock;
        FullMoveNumber = fullMoveNumber;
    }

    internal bool WhiteToMove { get; private set; }

    internal string SideToMove => WhiteToMove ? "white" : "black";

    private bool WhiteKingSide { get; set; }

    private bool WhiteQueenSide { get; set; }

    private bool BlackKingSide { get; set; }

    private bool BlackQueenSide { get; set; }

    private int EnPassant { get; set; }

    private int HalfMoveClock { get; set; }

    private int FullMoveNumber { get; set; }

    // Replays the UCI move list from startFen and returns the resulting position.
    // startFen may be "startpos", "standard", or empty — all of which mean the
    // standard initial position (matching Lichess's gameFull.initialFen).
    internal static ChessPosition Replay(string? startFen, IEnumerable<string> uciMoves)
    {
        ChessPosition position = FromFen(NormalizeFen(startFen));
        foreach (string move in uciMoves)
        {
            position.Apply(move);
        }

        return position;
    }

    internal static string NormalizeFen(string? fen) =>
        string.IsNullOrWhiteSpace(fen) || fen is "startpos" or "standard" ? StartFen : fen;

    internal static ChessPosition FromFen(string fen)
    {
        string[] fields = fen.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        char[] board = new char[64];
        Array.Fill(board, '.');

        string[] ranks = fields[0].Split('/');
        for (int r = 0; r < 8; r++)
        {
            // FEN lists rank 8 first; board index 0 is a1 (rank 1).
            int rank = 7 - r;
            int file = 0;
            foreach (char c in ranks[r])
            {
                if (char.IsDigit(c))
                {
                    file += c - '0';
                }
                else
                {
                    board[(rank * 8) + file] = c;
                    file++;
                }
            }
        }

        bool whiteToMove = fields[1] == "w";
        string castling = fields[2];
        int enPassant = fields[3] == "-" ? -1 : SquareToIndex(fields[3]);
        int halfMove = fields.Length > 4 ? int.Parse(fields[4]) : 0;
        int fullMove = fields.Length > 5 ? int.Parse(fields[5]) : 1;

        return new ChessPosition(
            board,
            whiteToMove,
            castling.Contains('K'),
            castling.Contains('Q'),
            castling.Contains('k'),
            castling.Contains('q'),
            enPassant,
            halfMove,
            fullMove);
    }

    internal void Apply(string uci)
    {
        int from = SquareToIndex(uci[..2]);
        int to = SquareToIndex(uci.Substring(2, 2));
        char promotion = uci.Length >= 5 ? uci[4] : '\0';

        char piece = _board[from];
        bool isPawn = char.ToLowerInvariant(piece) == 'p';
        bool isCapture = _board[to] != '.';

        if (isPawn && to == EnPassant && _board[to] == '.')
        {
            // En passant: the captured pawn sits on the mover's rank, target file.
            int capturedSquare = (from / 8 * 8) + (to % 8);
            _board[capturedSquare] = '.';
            isCapture = true;
        }

        char placed = promotion != '\0'
            ? (WhiteToMove ? char.ToUpperInvariant(promotion) : char.ToLowerInvariant(promotion))
            : piece;
        _board[to] = placed;
        _board[from] = '.';

        if (char.ToLowerInvariant(piece) == 'k' && Math.Abs((to % 8) - (from % 8)) == 2)
        {
            MoveCastlingRook(to);
        }

        UpdateCastlingRights(piece, from, to);

        EnPassant = isPawn && Math.Abs((to / 8) - (from / 8)) == 2
            ? (from + to) / 2
            : -1;

        HalfMoveClock = isPawn || isCapture ? 0 : HalfMoveClock + 1;

        if (!WhiteToMove)
        {
            FullMoveNumber++;
        }

        WhiteToMove = !WhiteToMove;
    }

    internal string ToFen()
    {
        var sb = new StringBuilder();
        for (int rank = 7; rank >= 0; rank--)
        {
            int empty = 0;
            for (int file = 0; file < 8; file++)
            {
                char c = _board[(rank * 8) + file];
                if (c == '.')
                {
                    empty++;
                }
                else
                {
                    if (empty > 0)
                    {
                        sb.Append(empty);
                        empty = 0;
                    }

                    sb.Append(c);
                }
            }

            if (empty > 0)
            {
                sb.Append(empty);
            }

            if (rank > 0)
            {
                sb.Append('/');
            }
        }

        sb.Append(WhiteToMove ? " w " : " b ");
        sb.Append(CastlingField());
        sb.Append(' ');
        sb.Append(EnPassant == -1 ? "-" : IndexToSquare(EnPassant));
        sb.Append(' ');
        sb.Append(HalfMoveClock);
        sb.Append(' ');
        sb.Append(FullMoveNumber);
        return sb.ToString();
    }

    private static int SquareToIndex(string square)
    {
        int file = square[0] - 'a';
        int rank = square[1] - '1';
        return (rank * 8) + file;
    }

    private static string IndexToSquare(int index)
    {
        char file = (char)('a' + (index % 8));
        char rank = (char)('1' + (index / 8));
        return $"{file}{rank}";
    }

    private void MoveCastlingRook(int kingTo)
    {
        switch (kingTo)
        {
            case 6: // g1
                _board[5] = _board[7];
                _board[7] = '.';
                break;
            case 2: // c1
                _board[3] = _board[0];
                _board[0] = '.';
                break;
            case 62: // g8
                _board[61] = _board[63];
                _board[63] = '.';
                break;
            case 58: // c8
                _board[59] = _board[56];
                _board[56] = '.';
                break;
        }
    }

    private void UpdateCastlingRights(char piece, int from, int to)
    {
        switch (piece)
        {
            case 'K':
                WhiteKingSide = false;
                WhiteQueenSide = false;
                break;
            case 'k':
                BlackKingSide = false;
                BlackQueenSide = false;
                break;
        }

        // A rook leaving — or being captured on — its home square voids that right.
        foreach (int square in new[] { from, to })
        {
            switch (square)
            {
                case 0: WhiteQueenSide = false; break;
                case 7: WhiteKingSide = false; break;
                case 56: BlackQueenSide = false; break;
                case 63: BlackKingSide = false; break;
            }
        }
    }

    private string CastlingField()
    {
        var sb = new StringBuilder();
        if (WhiteKingSide)
        {
            sb.Append('K');
        }

        if (WhiteQueenSide)
        {
            sb.Append('Q');
        }

        if (BlackKingSide)
        {
            sb.Append('k');
        }

        if (BlackQueenSide)
        {
            sb.Append('q');
        }

        return sb.Length == 0 ? "-" : sb.ToString();
    }
}
