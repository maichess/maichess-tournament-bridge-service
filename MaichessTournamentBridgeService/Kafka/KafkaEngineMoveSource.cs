using System.Diagnostics.CodeAnalysis;
using Confluent.Kafka;
using Maichess.Events.V1;
using MaichessTournamentBridgeService.Services;

namespace MaichessTournamentBridgeService.Kafka;

// Requests a bot move over Kafka instead of the synchronous Engine.GetBestMove gRPC
// call (removed in Kafka task 09). Produces a BotMoveRequested (wrapped in the shared
// MatchEvent envelope, keyed by request_id) to engine.commands.v1 and awaits the
// correlated BotMoveCalculated that the engine returns on engine.events.v1, surfaced
// here by EngineEventConsumer via the shared PendingBotMoves registry. A dedicated
// topic pair (not match.events.v1) keeps external-game requests out of the match log,
// whose projector would otherwise create phantom live matches.
[ExcludeFromCodeCoverage]
internal sealed class KafkaEngineMoveSource : IEngineMoveSource, IDisposable
{
    private const string Topic = "engine.commands.v1";
    private const string ProducerName = "tournament-bridge-service";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    private readonly IProducer<string, MatchEvent> producer;
    private readonly PendingBotMoves pending;

    public KafkaEngineMoveSource(PendingBotMoves pending)
    {
        this.pending = pending;

        string bootstrap = Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP") ?? "kafka:9092";

        producer = new ProducerBuilder<string, MatchEvent>(
                new ProducerConfig { BootstrapServers = bootstrap })
            .SetValueSerializer(ProtobufEventSerdes.Serializer<MatchEvent>())
            .Build();
    }

    public async Task<string> GetBestMoveAsync(string botId, string fen, int timeLimitMs, CancellationToken ct)
    {
        string requestId = Guid.NewGuid().ToString();
        Task<string> reply = pending.Register(requestId);

        MatchEvent envelope = new()
        {
            EventId = Guid.NewGuid().ToString(),
            EventType = "engine.BotMoveRequested",
            AggregateId = requestId,
            Sequence = 0L,
            OccurredAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Producer = ProducerName,
            BotMoveRequested = new BotMoveRequested
            {
                Fen = fen,
                BotId = botId,
                TimeLimitMs = timeLimitMs,
                RequestId = requestId,
            },
        };

        try
        {
            await producer.ProduceAsync(
                Topic, new Message<string, MatchEvent> { Key = requestId, Value = envelope }, ct);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(RequestTimeout);
            return await reply.WaitAsync(timeoutCts.Token);
        }
        catch (Exception)
        {
            pending.Cancel(requestId);
            throw;
        }
    }

    public void Dispose()
    {
        producer.Flush(TimeSpan.FromSeconds(5));
        producer.Dispose();
    }
}
