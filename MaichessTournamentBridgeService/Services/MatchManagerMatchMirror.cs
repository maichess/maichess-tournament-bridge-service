using System.Diagnostics.CodeAnalysis;
using Grpc.Core;
using Maichess.MatchManager.V1;
using Microsoft.Extensions.Logging;

namespace MaichessTournamentBridgeService.Services;

// Match Manager gRPC implementation of the external-match mirror. Creates the match
// with source = EXTERNAL (read-only: move validation and bot-move scheduling are
// skipped) and pushes state via SyncExternalMatch. RecordMatchResult is never called,
// so external games stay unrated. Live gRPC I/O — excluded from coverage; the pure
// status/end-reason mapping it relies on is tested separately (ExternalMatchMapping).
[ExcludeFromCodeCoverage]
internal sealed class MatchManagerMatchMirror(
    Matches.MatchesClient client,
    ILogger<MatchManagerMatchMirror> logger) : IExternalMatchMirror
{
    public async Task<string> CreateAsync(ExternalMatchInfo info, CancellationToken ct)
    {
        Player ours = new() { BotId = info.OurBotId };
        Player opponent = new() { ExternalName = info.OpponentName };

        CreateMatchResponse response = await client.CreateMatchAsync(
            new CreateMatchRequest
            {
                White = info.OurColor == "white" ? ours : opponent,
                Black = info.OurColor == "white" ? opponent : ours,
                TimeFormat = new TimeFormat
                {
                    Id = $"{info.BaseMs / 60000}+{info.IncrementMs / 1000}",
                    BaseMs = info.BaseMs,
                    IncrementMs = info.IncrementMs,
                    Category = Categorize(info.BaseMs),
                },
                Source = MatchSource.External,
                ExternalProvider = info.ProviderName,
                ExternalRef = info.ExternalRef,
                CreatedBy = ours,
            },
            cancellationToken: ct);

        return response.Match.Id;
    }

    public async Task SyncAsync(ExternalMatchSync sync, CancellationToken ct)
    {
        var request = new SyncExternalMatchRequest
        {
            MatchId = sync.MatchId,
            CurrentFen = sync.Fen,
            Status = ExternalMatchMapping.ToMatchStatus(sync.Status),
            WhiteTimeMs = sync.WhiteTimeMs,
            BlackTimeMs = sync.BlackTimeMs,
            EndReason = sync.EndReason,
        };
        request.Moves.AddRange(sync.Moves);

        if (sync.Finished)
        {
            request.FinishedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        try
        {
            await client.SyncExternalMatchAsync(request, cancellationToken: ct);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            logger.LogWarning("External match {MatchId} not found during sync", sync.MatchId);
        }
    }

    private static string Categorize(long baseMs) => baseMs switch
    {
        < 180_000 => "bullet",
        < 600_000 => "blitz",
        < 1_500_000 => "rapid",
        _ => "classical",
    };
}
