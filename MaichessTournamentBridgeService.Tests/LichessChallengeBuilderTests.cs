using MaichessTournamentBridgeService.Models;
using MaichessTournamentBridgeService.Providers.Lichess;
using Xunit;

namespace MaichessTournamentBridgeService.Tests;

public sealed class LichessChallengeBuilderTests
{
    private static LichessChallenge Challenge(
        string opponent, int limit = 300, int inc = 2, bool rated = false, int level = 3) =>
        new(opponent, limit, inc, rated, level);

    [Theory]
    [InlineData("ai", true)]
    [InlineData("AI", true)]
    [InlineData("Maia1", false)]
    internal void IsAi_DetectsAiOpponentCaseInsensitive(string opponent, bool expected) =>
        Assert.Equal(expected, LichessChallengeBuilder.IsAi(Challenge(opponent)));

    [Fact]
    internal void BuildPath_Ai_UsesAiEndpoint() =>
        Assert.Equal("/api/challenge/ai", LichessChallengeBuilder.BuildPath(Challenge("ai")));

    [Fact]
    internal void BuildPath_User_UsesUsernameEndpoint() =>
        Assert.Equal("/api/challenge/Maia1", LichessChallengeBuilder.BuildPath(Challenge("Maia1")));

    [Fact]
    internal void BuildForm_Ai_IncludesClampedLevelAndClock()
    {
        Dictionary<string, string> form = LichessChallengeBuilder.BuildForm(
            Challenge("ai", limit: 180, inc: 1, level: 12));

        Assert.Equal("180", form["clock.limit"]);
        Assert.Equal("1", form["clock.increment"]);
        Assert.Equal("random", form["color"]);
        Assert.Equal("8", form["level"]); // clamped to the 1–8 range
        Assert.False(form.ContainsKey("rated"));
    }

    [Fact]
    internal void BuildForm_Ai_ClampsLevelLowerBound() =>
        Assert.Equal("1", LichessChallengeBuilder.BuildForm(Challenge("ai", level: 0))["level"]);

    [Fact]
    internal void BuildForm_User_IncludesRatedNotLevel()
    {
        Dictionary<string, string> form = LichessChallengeBuilder.BuildForm(
            Challenge("Maia1", rated: true));

        Assert.Equal("true", form["rated"]);
        Assert.False(form.ContainsKey("level"));
    }

    [Fact]
    internal void BuildForm_User_UnratedByDefault() =>
        Assert.Equal("false", LichessChallengeBuilder.BuildForm(Challenge("Maia1", rated: false))["rated"]);

    [Fact]
    internal void ParseGameId_AiShape_ReadsTopLevelId() =>
        Assert.Equal("abcd1234", LichessChallengeBuilder.ParseGameId("""{"id":"abcd1234","speed":"blitz"}"""));

    [Fact]
    internal void ParseGameId_UserChallengeShape_ReadsNestedId() =>
        Assert.Equal(
            "ch9981xy",
            LichessChallengeBuilder.ParseGameId("""{"challenge":{"id":"ch9981xy","status":"created"}}"""));

    [Fact]
    internal void ParseGameId_GameWrapperShape_ReadsNestedId() =>
        Assert.Equal(
            "g77",
            LichessChallengeBuilder.ParseGameId("""{"game":{"id":"g77"}}"""));

    [Fact]
    internal void ParseGameId_NoId_Throws() =>
        Assert.Throws<InvalidOperationException>(
            () => LichessChallengeBuilder.ParseGameId("""{"error":"nope"}"""));

    [Fact]
    internal void ParseGameId_NonStringId_Throws() =>
        Assert.Throws<InvalidOperationException>(
            () => LichessChallengeBuilder.ParseGameId("""{"id":123}"""));
}
