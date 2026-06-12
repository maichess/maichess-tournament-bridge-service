using System.Runtime.CompilerServices;
using MaichessTournamentBridgeService.Models;
using MaichessTournamentBridgeService.Providers;
using MaichessTournamentBridgeService.Providers.Lichess;
using MaichessTournamentBridgeService.Services;
using Microsoft.Extensions.Hosting;

namespace MaichessTournamentBridgeService.Tests;

// A host lifetime whose tokens never fire — the drive loop runs to completion under it.
internal sealed class FakeApplicationLifetime : IHostApplicationLifetime
{
    public CancellationToken ApplicationStarted => CancellationToken.None;

    public CancellationToken ApplicationStopping => CancellationToken.None;

    public CancellationToken ApplicationStopped => CancellationToken.None;

    public void StopApplication()
    {
    }
}

// Hand-written fakes for the bridge/registration seams. The codebase tests pure logic
// without a mocking framework; these keep that style.
internal sealed class FakeExternalProvider(params GameUpdate[] updates) : IExternalProvider
{
    public List<string> Submitted { get; } = [];

    public string Name => "lichess";

#pragma warning disable CS1998
    public async IAsyncEnumerable<GameUpdate> StreamGameAsync(
        ExternalGameRef game, [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (GameUpdate update in updates)
        {
            ct.ThrowIfCancellationRequested();
            yield return update;
        }
    }
#pragma warning restore CS1998

    public Task SubmitMoveAsync(ExternalGameRef game, string uci, CancellationToken ct)
    {
        Submitted.Add(uci);
        return Task.CompletedTask;
    }
}

// Yields the given updates, then throws — to exercise the bridge's stream-failure path.
internal sealed class FailingProvider(Exception failure, params GameUpdate[] updates) : IExternalProvider
{
    public string Name => "lichess";

#pragma warning disable CS1998
    public async IAsyncEnumerable<GameUpdate> StreamGameAsync(
        ExternalGameRef game, [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (GameUpdate update in updates)
        {
            yield return update;
        }

        throw failure;
    }
#pragma warning restore CS1998

    public Task SubmitMoveAsync(ExternalGameRef game, string uci, CancellationToken ct) =>
        Task.CompletedTask;
}

internal sealed class FakeEngineMoveSource(string move) : IEngineMoveSource
{
    public List<(string BotId, string Fen, int TimeLimitMs)> Calls { get; } = [];

    public Task<string> GetBestMoveAsync(string botId, string fen, int timeLimitMs, CancellationToken ct)
    {
        Calls.Add((botId, fen, timeLimitMs));
        return Task.FromResult(move);
    }
}

internal sealed class FakeMatchMirror(string matchId) : IExternalMatchMirror
{
    public List<ExternalMatchInfo> Created { get; } = [];

    public List<ExternalMatchSync> Synced { get; } = [];

    public Task<string> CreateAsync(ExternalMatchInfo info, CancellationToken ct)
    {
        Created.Add(info);
        return Task.FromResult(matchId);
    }

    public Task SyncAsync(ExternalMatchSync sync, CancellationToken ct)
    {
        Synced.Add(sync);
        return Task.CompletedTask;
    }
}

internal sealed class FakeBotCatalog(bool exists) : IBotCatalog
{
    public Task<bool> ExistsAsync(string botId, CancellationToken ct) => Task.FromResult(exists);
}

internal sealed class FakeChallenger : ILichessChallenger
{
    private readonly string? _gameId;
    private readonly Exception? _failure;

    public FakeChallenger(string gameId) => _gameId = gameId;

    public FakeChallenger(Exception failure) => _failure = failure;

    public string? Token { get; private set; }

    public LichessChallenge? Received { get; private set; }

    public Task<string> CreateChallengeAsync(
        string token, LichessChallenge challenge, CancellationToken ct)
    {
        Token = token;
        Received = challenge;
        return _failure is not null
            ? Task.FromException<string>(_failure)
            : Task.FromResult(_gameId!);
    }
}

internal sealed class FakeBridgeLauncher : ILichessBridgeLauncher
{
    private readonly string? _matchId;
    private readonly Exception? _failure;

    public FakeBridgeLauncher(string matchId) => _matchId = matchId;

    public FakeBridgeLauncher(Exception failure) => _failure = failure;

    public string? BotId { get; private set; }

    public string? Token { get; private set; }

    public string? GameId { get; private set; }

    public Task<string> StartAsync(string botId, string token, string gameId, CancellationToken ct)
    {
        BotId = botId;
        Token = token;
        GameId = gameId;
        return _failure is not null
            ? Task.FromException<string>(_failure)
            : Task.FromResult(_matchId!);
    }
}
