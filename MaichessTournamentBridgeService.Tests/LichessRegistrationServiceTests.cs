using MaichessTournamentBridgeService.Models;
using MaichessTournamentBridgeService.Services;
using Xunit;

namespace MaichessTournamentBridgeService.Tests;

public sealed class LichessRegistrationServiceTests
{
    private static LichessChallenge AiChallenge(string opponent = "ai") =>
        new(Opponent: opponent, ClockLimitSeconds: 300, ClockIncrementSeconds: 0, Rated: false, Level: 3);

    private static LichessRegistrationService Service(
        bool botExists = true,
        FakeBridgeLauncher? launcher = null,
        FakeChallenger? challenger = null) =>
        new(
            new FakeBotCatalog(botExists),
            challenger ?? new FakeChallenger("game-from-challenge"),
            launcher ?? new FakeBridgeLauncher("match-default"));

    // --- RegisterAsync (attach to an existing game) ---

    [Fact]
    internal async Task Register_ValidRequest_StartsBridgeAndReturnsMatchId()
    {
        var launcher = new FakeBridgeLauncher("match-42");
        var service = Service(launcher: launcher);

        LichessRegistrationResult result = await service.RegisterAsync(
            "blitz-3", "lip_token", "game-7", CancellationToken.None);

        Assert.Equal(LichessRegistrationOutcome.Created, result.Outcome);
        Assert.Equal("match-42", result.MatchId);
        Assert.Null(result.Error);

        // Provider routing: the request is forwarded verbatim to the bridge.
        Assert.Equal("blitz-3", launcher.BotId);
        Assert.Equal("lip_token", launcher.Token);
        Assert.Equal("game-7", launcher.GameId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    internal async Task Register_MissingBot_Fails(string? botId)
    {
        LichessRegistrationResult result = await Service().RegisterAsync(
            botId, "lip_token", "game-7", CancellationToken.None);

        Assert.Equal(LichessRegistrationOutcome.MissingBot, result.Outcome);
        Assert.Null(result.MatchId);
        Assert.NotNull(result.Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    internal async Task Register_MissingToken_Fails(string? token)
    {
        LichessRegistrationResult result = await Service().RegisterAsync(
            "blitz-3", token, "game-7", CancellationToken.None);

        Assert.Equal(LichessRegistrationOutcome.MissingToken, result.Outcome);
    }

    [Fact]
    internal async Task Register_MissingGame_Fails()
    {
        LichessRegistrationResult result = await Service().RegisterAsync(
            "blitz-3", "lip_token", null, CancellationToken.None);

        Assert.Equal(LichessRegistrationOutcome.MissingGame, result.Outcome);
    }

    [Fact]
    internal async Task Register_UnknownBot_Fails()
    {
        LichessRegistrationResult result = await Service(botExists: false).RegisterAsync(
            "ghost", "lip_token", "game-7", CancellationToken.None);

        Assert.Equal(LichessRegistrationOutcome.UnknownBot, result.Outcome);
        Assert.Contains("ghost", result.Error);
    }

    [Fact]
    internal async Task Register_BridgeFailure_MapsToProviderError()
    {
        var launcher = new FakeBridgeLauncher(new HttpRequestException("game 404"));
        var service = Service(launcher: launcher);

        LichessRegistrationResult result = await service.RegisterAsync(
            "blitz-3", "lip_token", "missing-game", CancellationToken.None);

        Assert.Equal(LichessRegistrationOutcome.ProviderError, result.Outcome);
        Assert.Null(result.MatchId);
        Assert.NotNull(result.Error);
    }

    // --- ChallengeAsync (create a game by challenging an opponent) ---

    [Fact]
    internal async Task Challenge_ValidRequest_CreatesGameThenDrivesIt()
    {
        var challenger = new FakeChallenger("lichess-game-9");
        var launcher = new FakeBridgeLauncher("match-99");
        var service = Service(launcher: launcher, challenger: challenger);

        LichessRegistrationResult result = await service.ChallengeAsync(
            "blitz-3", "lip_token", AiChallenge(), CancellationToken.None);

        Assert.Equal(LichessRegistrationOutcome.Created, result.Outcome);
        Assert.Equal("match-99", result.MatchId);

        // The challenge is created with our token, then the returned game id is driven.
        Assert.Equal("lip_token", challenger.Token);
        Assert.Equal("ai", challenger.Received!.Opponent);
        Assert.Equal("lichess-game-9", launcher.GameId);
        Assert.Equal("blitz-3", launcher.BotId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    internal async Task Challenge_MissingOpponent_Fails(string? opponent)
    {
        LichessRegistrationResult result = await Service().ChallengeAsync(
            "blitz-3", "lip_token", AiChallenge(opponent!), CancellationToken.None);

        Assert.Equal(LichessRegistrationOutcome.MissingOpponent, result.Outcome);
    }

    [Fact]
    internal async Task Challenge_MissingBot_Fails()
    {
        LichessRegistrationResult result = await Service().ChallengeAsync(
            null, "lip_token", AiChallenge(), CancellationToken.None);

        Assert.Equal(LichessRegistrationOutcome.MissingBot, result.Outcome);
    }

    [Fact]
    internal async Task Challenge_MissingToken_Fails()
    {
        LichessRegistrationResult result = await Service().ChallengeAsync(
            "blitz-3", null, AiChallenge(), CancellationToken.None);

        Assert.Equal(LichessRegistrationOutcome.MissingToken, result.Outcome);
    }

    [Fact]
    internal async Task Challenge_UnknownBot_Fails()
    {
        LichessRegistrationResult result = await Service(botExists: false).ChallengeAsync(
            "ghost", "lip_token", AiChallenge(), CancellationToken.None);

        Assert.Equal(LichessRegistrationOutcome.UnknownBot, result.Outcome);
    }

    [Fact]
    internal async Task Challenge_CreationFails_MapsToProviderError()
    {
        var challenger = new FakeChallenger(new HttpRequestException("lichess 401"));
        var service = Service(challenger: challenger);

        LichessRegistrationResult result = await service.ChallengeAsync(
            "blitz-3", "lip_token", AiChallenge("Maia1"), CancellationToken.None);

        Assert.Equal(LichessRegistrationOutcome.ProviderError, result.Outcome);
        Assert.NotNull(result.Error);
    }

    [Fact]
    internal async Task Challenge_BridgeFailure_MapsToProviderError()
    {
        var launcher = new FakeBridgeLauncher(new HttpRequestException("stream failed"));
        var service = Service(launcher: launcher, challenger: new FakeChallenger("g1"));

        LichessRegistrationResult result = await service.ChallengeAsync(
            "blitz-3", "lip_token", AiChallenge(), CancellationToken.None);

        Assert.Equal(LichessRegistrationOutcome.ProviderError, result.Outcome);
    }
}
