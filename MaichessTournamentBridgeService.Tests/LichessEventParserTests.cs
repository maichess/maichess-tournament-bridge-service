using System.Text.Json;
using MaichessTournamentBridgeService.Chess;
using MaichessTournamentBridgeService.Models;
using MaichessTournamentBridgeService.Providers.Lichess;
using Xunit;

namespace MaichessTournamentBridgeService.Tests;

public sealed class LichessEventParserTests
{
    private const string GameFull =
        """
        {"type":"gameFull","id":"abc","white":{"id":"ourbot","name":"OurBot"},
        "black":{"id":"villain","name":"Villain"},"initialFen":"startpos",
        "state":{"type":"gameState","moves":"","wtime":300000,"btime":300000,
        "winc":2000,"binc":2000,"status":"started"}}
        """;

    private static JsonElement Root(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    [Fact]
    internal void ResolveColor_WhiteSeatIsOurs_ReturnsWhite() =>
        Assert.Equal("white", LichessEventParser.ResolveColor(Root(GameFull), "ourbot"));

    [Fact]
    internal void ResolveColor_WhiteSeatIsOurs_CaseInsensitive() =>
        Assert.Equal("white", LichessEventParser.ResolveColor(Root(GameFull), "OURBOT"));

    [Fact]
    internal void ResolveColor_WeAreNotWhite_ReturnsBlack() =>
        Assert.Equal("black", LichessEventParser.ResolveColor(Root(GameFull), "villain"));

    [Fact]
    internal void ResolveColor_AiWhiteSeatHasNoId_ReturnsBlack()
    {
        const string json = """{"type":"gameFull","white":{"aiLevel":4},"black":{"id":"ourbot"}}""";
        Assert.Equal("black", LichessEventParser.ResolveColor(Root(json), "ourbot"));
    }

    [Fact]
    internal void ResolveColor_NoWhiteSeat_ReturnsBlack()
    {
        const string json = """{"type":"gameFull","black":{"id":"ourbot"}}""";
        Assert.Equal("black", LichessEventParser.ResolveColor(Root(json), "ourbot"));
    }

    [Fact]
    internal void ResolveInitialFen_Startpos_ReturnsStandardStart() =>
        Assert.Equal(ChessPosition.StartFen, LichessEventParser.ResolveInitialFen(Root(GameFull)));

    [Fact]
    internal void ResolveInitialFen_CustomFen_ReturnsIt()
    {
        const string fen = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1";
        string json = $$"""{"type":"gameFull","initialFen":"{{fen}}"}""";
        Assert.Equal(fen, LichessEventParser.ResolveInitialFen(Root(json)));
    }

    [Fact]
    internal void ResolveInitialFen_Missing_ReturnsStandardStart()
    {
        const string json = """{"type":"gameFull"}""";
        Assert.Equal(ChessPosition.StartFen, LichessEventParser.ResolveInitialFen(Root(json)));
    }

    [Fact]
    internal void ResolveOpponentName_UsesOtherSeatName() =>
        Assert.Equal("Villain", LichessEventParser.ResolveOpponentName(Root(GameFull), "white"));

    [Fact]
    internal void ResolveOpponentName_WhenWeAreBlack_UsesWhiteName() =>
        Assert.Equal("OurBot", LichessEventParser.ResolveOpponentName(Root(GameFull), "black"));

    [Fact]
    internal void ResolveOpponentName_AiOpponent_DescribesLevel()
    {
        const string json = """{"type":"gameFull","white":{"id":"ourbot"},"black":{"aiLevel":6}}""";
        Assert.Equal("Stockfish level 6", LichessEventParser.ResolveOpponentName(Root(json), "white"));
    }

    [Fact]
    internal void ResolveOpponentName_AnonymousOpponent_FallsBack()
    {
        const string json = """{"type":"gameFull","white":{"id":"ourbot"},"black":{}}""";
        Assert.Equal("Lichess opponent", LichessEventParser.ResolveOpponentName(Root(json), "white"));
    }

    [Fact]
    internal void ResolveOpponentName_MissingSeat_FallsBack()
    {
        const string json = """{"type":"gameFull","white":{"id":"ourbot"}}""";
        Assert.Equal("Lichess opponent", LichessEventParser.ResolveOpponentName(Root(json), "white"));
    }

    [Fact]
    internal void Parse_GameFull_UsesNestedStateAndStandardStart()
    {
        GameUpdate? update = LichessEventParser.Parse(
            GameFull, ChessPosition.StartFen, "white", "Villain");

        Assert.NotNull(update);
        Assert.Empty(update!.Moves);
        Assert.Equal(ChessPosition.StartFen, update.Fen);
        Assert.Equal("white", update.Turn);
        Assert.Equal("ongoing", update.Status);
        Assert.False(update.IsFinished);
        Assert.True(update.IsOurTurn);
        Assert.Equal(300000, update.WhiteTimeMs);
        Assert.Equal(300000, update.BlackTimeMs);
        Assert.Equal("white", update.OurColor);
        Assert.Equal("Villain", update.OpponentName);
    }

    [Fact]
    internal void Parse_GameState_DerivesFenAndTurnFromMoves()
    {
        const string json =
            """{"type":"gameState","moves":"e2e4","wtime":299000,"btime":300000,"status":"started"}""";

        GameUpdate? update = LichessEventParser.Parse(json, ChessPosition.StartFen, "black", "Villain");

        Assert.NotNull(update);
        Assert.Equal(["e2e4"], update!.Moves);
        Assert.Equal(
            "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1", update.Fen);
        Assert.Equal("black", update.Turn);
        Assert.True(update.IsOurTurn);
        // ms passed through verbatim — no seconds→ms conversion.
        Assert.Equal(299000, update.WhiteTimeMs);
        Assert.Equal(300000, update.BlackTimeMs);
    }

    [Theory]
    [InlineData("mate", "white", "white_won")]
    [InlineData("resign", "black", "black_won")]
    [InlineData("outoftime", "white", "white_won")]
    [InlineData("stalemate", null, "draw")]
    [InlineData("draw", null, "draw")]
    internal void Parse_FinishedGame_MapsStatus(string status, string? winner, string expected)
    {
        string winnerField = winner is null ? string.Empty : $",\"winner\":\"{winner}\"";
        string json =
            $$"""{"type":"gameState","moves":"e2e4 e7e5","wtime":1000,"btime":1000,"status":"{{status}}"{{winnerField}}}""";

        GameUpdate? update = LichessEventParser.Parse(json, ChessPosition.StartFen, "white", "Villain");

        Assert.NotNull(update);
        Assert.Equal(expected, update!.Status);
        Assert.Equal(status, update.RawStatus);
        Assert.True(update.IsFinished);
    }

    [Fact]
    internal void Parse_MissingClocks_DefaultToZero()
    {
        const string json = """{"type":"gameState","moves":"e2e4","status":"started"}""";

        GameUpdate? update = LichessEventParser.Parse(json, ChessPosition.StartFen, "white", "Villain");

        Assert.NotNull(update);
        Assert.Equal(0, update!.WhiteTimeMs);
        Assert.Equal(0, update.BlackTimeMs);
    }

    [Fact]
    internal void Parse_MissingMovesAndStatus_TreatedAsOngoingStart()
    {
        const string json = """{"type":"gameState","wtime":1000,"btime":1000}""";

        GameUpdate? update = LichessEventParser.Parse(json, ChessPosition.StartFen, "white", "Villain");

        Assert.NotNull(update);
        Assert.Empty(update!.Moves);
        Assert.Equal("ongoing", update.Status);
        Assert.Equal("started", update.RawStatus);
    }

    [Fact]
    internal void Parse_NullValuedFields_FallBackToDefaults()
    {
        const string json =
            """{"type":"gameState","moves":null,"status":null,"wtime":1000,"btime":1000}""";

        GameUpdate? update = LichessEventParser.Parse(json, ChessPosition.StartFen, "white", "Villain");

        Assert.NotNull(update);
        Assert.Empty(update!.Moves);
        Assert.Equal("ongoing", update.Status);
        Assert.Equal("started", update.RawStatus);
    }

    [Fact]
    internal void Parse_NullType_IsIgnored()
    {
        const string json = """{"type":null}""";
        Assert.Null(LichessEventParser.Parse(json, ChessPosition.StartFen, "white", "Villain"));
    }

    [Fact]
    internal void ResolveOpponentName_NonStringName_FallsBack()
    {
        const string json = """{"type":"gameFull","white":{"id":"ourbot"},"black":{"name":42}}""";
        Assert.Equal("Lichess opponent", LichessEventParser.ResolveOpponentName(Root(json), "white"));
    }

    [Fact]
    internal void Parse_ChatLine_IsIgnored()
    {
        const string json =
            """{"type":"chatLine","username":"villain","text":"good luck","room":"player"}""";
        Assert.Null(LichessEventParser.Parse(json, ChessPosition.StartFen, "white", "Villain"));
    }

    [Fact]
    internal void Parse_OpponentGone_IsIgnored()
    {
        const string json = """{"type":"opponentGone","gone":true}""";
        Assert.Null(LichessEventParser.Parse(json, ChessPosition.StartFen, "white", "Villain"));
    }
}
