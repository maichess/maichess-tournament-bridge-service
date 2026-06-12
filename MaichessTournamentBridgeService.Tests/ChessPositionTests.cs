using MaichessTournamentBridgeService.Chess;
using Xunit;

namespace MaichessTournamentBridgeService.Tests;

public sealed class ChessPositionTests
{
    [Theory]
    [InlineData("startpos")]
    [InlineData("standard")]
    [InlineData("")]
    [InlineData(null)]
    internal void NormalizeFen_TreatsAliasesAsStart(string? alias) =>
        Assert.Equal(ChessPosition.StartFen, ChessPosition.NormalizeFen(alias));

    [Fact]
    internal void NormalizeFen_PassesThroughRealFen()
    {
        const string fen = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1";
        Assert.Equal(fen, ChessPosition.NormalizeFen(fen));
    }

    [Fact]
    internal void Replay_NoMoves_IsStartPosition()
    {
        ChessPosition position = ChessPosition.Replay("startpos", []);
        Assert.Equal(ChessPosition.StartFen, position.ToFen());
        Assert.Equal("white", position.SideToMove);
    }

    [Fact]
    internal void Replay_DoublePawnPush_SetsEnPassantAndSwitchesSide()
    {
        ChessPosition position = ChessPosition.Replay("startpos", ["e2e4"]);
        Assert.Equal(
            "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1",
            position.ToFen());
        Assert.Equal("black", position.SideToMove);
        Assert.False(position.WhiteToMove);
    }

    [Fact]
    internal void Replay_TwoMoves_IncrementsFullMoveAndClearsEnPassant()
    {
        ChessPosition position = ChessPosition.Replay("startpos", ["e2e4", "e7e5"]);
        Assert.Equal(
            "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq e6 0 2",
            position.ToFen());
    }

    [Fact]
    internal void Replay_KnightMoves_IncrementHalfMoveClock()
    {
        ChessPosition position = ChessPosition.Replay("startpos", ["g1f3", "g8f6"]);
        Assert.Equal(
            "rnbqkb1r/pppppppp/5n2/8/8/5N2/PPPPPPPP/RNBQKB1R w KQkq - 2 2",
            position.ToFen());
    }

    [Fact]
    internal void Replay_Capture_ResetsHalfMoveClock()
    {
        // 1. e4 d5 2. exd5 — the capture resets the half-move clock to 0.
        ChessPosition position = ChessPosition.Replay("startpos", ["e2e4", "d7d5", "e4d5"]);
        Assert.Equal(
            "rnbqkbnr/ppp1pppp/8/3P4/8/8/PPPP1PPP/RNBQKBNR b KQkq - 0 2",
            position.ToFen());
    }

    [Fact]
    internal void Replay_EnPassantCapture_RemovesCapturedPawn()
    {
        // 1. e4 a6 2. e5 d5 3. exd6 e.p.
        ChessPosition position = ChessPosition.Replay(
            "startpos", ["e2e4", "a7a6", "e4e5", "d7d5", "e5d6"]);
        Assert.Equal(
            "rnbqkbnr/1pp1pppp/p2P4/8/8/8/PPPP1PPP/RNBQKBNR b KQkq - 0 3",
            position.ToFen());
    }

    [Fact]
    internal void Replay_WhiteKingSideCastle_MovesRookAndClearsRights()
    {
        const string fen = "rnbqk2r/pppp1ppp/5n2/2b1p3/2B1P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 4 4";
        ChessPosition position = ChessPosition.Replay(fen, ["e1g1"]);
        Assert.Equal(
            "rnbqk2r/pppp1ppp/5n2/2b1p3/2B1P3/5N2/PPPP1PPP/RNBQ1RK1 b kq - 5 4",
            position.ToFen());
    }

    [Fact]
    internal void Replay_WhiteQueenSideCastle_MovesRook()
    {
        const string fen = "r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1";
        ChessPosition position = ChessPosition.Replay(fen, ["e1c1"]);
        Assert.Equal("r3k2r/8/8/8/8/8/8/2KR3R b kq - 1 1", position.ToFen());
    }

    [Fact]
    internal void Replay_BlackKingSideCastle_MovesRook()
    {
        const string fen = "r3k2r/8/8/8/8/8/8/R3K2R b KQkq - 0 1";
        ChessPosition position = ChessPosition.Replay(fen, ["e8g8"]);
        Assert.Equal("r4rk1/8/8/8/8/8/8/R3K2R w KQ - 1 2", position.ToFen());
    }

    [Fact]
    internal void Replay_BlackKingRookMove_ClearsKingSideRightOnly()
    {
        const string fen = "r3k2r/8/8/8/8/8/8/R3K2R b KQkq - 0 1";
        ChessPosition position = ChessPosition.Replay(fen, ["h8h7"]);
        Assert.Equal("r3k3/7r/8/8/8/8/8/R3K2R w KQq - 1 2", position.ToFen());
    }

    [Fact]
    internal void Replay_WhiteQueenRookMove_ClearsQueenSideRightOnly()
    {
        const string fen = "r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1";
        ChessPosition position = ChessPosition.Replay(fen, ["a1b1"]);
        Assert.Equal("r3k2r/8/8/8/8/8/8/1R2K2R b Kkq - 1 1", position.ToFen());
    }

    [Fact]
    internal void Replay_FenWithoutMoveCounters_DefaultsThem()
    {
        ChessPosition position = ChessPosition.Replay("k6K/8/8/8/8/8/8/8 w - -", []);
        Assert.Equal("k6K/8/8/8/8/8/8/8 w - - 0 1", position.ToFen());
    }

    [Fact]
    internal void Replay_BlackQueenSideCastle_MovesRook()
    {
        const string fen = "r3kbnr/pppqpppp/2np4/1B6/3P4/2N1B3/PPP1PPPP/R2QK1NR b KQkq - 6 5";
        ChessPosition position = ChessPosition.Replay(fen, ["e8c8"]);
        Assert.Equal(
            "2kr1bnr/pppqpppp/2np4/1B6/3P4/2N1B3/PPP1PPPP/R2QK1NR w KQ - 7 6",
            position.ToFen());
    }

    [Fact]
    internal void Replay_RookMove_ClearsOnlyThatSideCastlingRight()
    {
        const string fen = "r3k2r/pppppppp/8/8/8/8/PPPPPPPP/R3K2R w KQkq - 0 1";
        ChessPosition position = ChessPosition.Replay(fen, ["h1g1"]);
        Assert.Equal(
            "r3k2r/pppppppp/8/8/8/8/PPPPPPPP/R3K1R1 b Qkq - 1 1",
            position.ToFen());
    }

    [Fact]
    internal void Replay_RookCapturedOnHomeSquare_ClearsOpponentRight()
    {
        // White bishop on b7 captures the a8 rook; black loses queen-side castling.
        const string fen = "r3k2r/pB6/8/8/8/8/8/R3K2R w KQkq - 0 1";
        ChessPosition position = ChessPosition.Replay(fen, ["b7a8"]);
        Assert.Equal(
            "B3k2r/p7/8/8/8/8/8/R3K2R b KQk - 0 1",
            position.ToFen());
    }

    [Fact]
    internal void Replay_KingMove_ClearsBothRights()
    {
        const string fen = "r3k2r/pppp1ppp/8/8/8/8/PPPPPPPP/R3K2R b KQkq - 0 1";
        ChessPosition position = ChessPosition.Replay(fen, ["e8e7"]);
        Assert.Equal(
            "r6r/ppppkppp/8/8/8/8/PPPPPPPP/R3K2R w KQ - 1 2",
            position.ToFen());
    }

    [Fact]
    internal void Replay_Promotion_PlacesPromotedPiece()
    {
        const string fen = "8/P7/8/8/8/8/8/k6K w - - 0 1";
        ChessPosition position = ChessPosition.Replay(fen, ["a7a8q"]);
        Assert.Equal("Q7/8/8/8/8/8/8/k6K b - - 0 1", position.ToFen());
    }

    [Fact]
    internal void Replay_BlackPromotionToKnight_UsesLowerCase()
    {
        const string fen = "K6k/8/8/8/8/8/p7/8 b - - 0 1";
        ChessPosition position = ChessPosition.Replay(fen, ["a2a1n"]);
        Assert.Equal("K6k/8/8/8/8/8/8/n7 w - - 0 2", position.ToFen());
    }

    [Fact]
    internal void FromFen_PreservesEnPassantSquare()
    {
        const string fen = "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq e6 0 2";
        Assert.Equal(fen, ChessPosition.FromFen(fen).ToFen());
    }

    [Fact]
    internal void FromFen_RoundTripsCustomPosition()
    {
        const string fen = "r1bqkbnr/pppp1ppp/2n5/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 2 3";
        Assert.Equal(fen, ChessPosition.FromFen(fen).ToFen());
    }
}
