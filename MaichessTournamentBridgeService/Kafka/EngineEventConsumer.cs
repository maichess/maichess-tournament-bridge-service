using System.Diagnostics.CodeAnalysis;
using Confluent.Kafka;
using Maichess.Events.V1;

namespace MaichessTournamentBridgeService.Kafka;

// Consumes the engine's bot-move replies from engine.events.v1 and hands each one to
// the PendingBotMoves registry, completing the Task that KafkaEngineMoveSource is
// awaiting for the matching request_id. Reads from Latest: only replies to requests
// this instance issued matter; a reply that arrives after a restart (whose waiter is
// gone) is harmlessly dropped, and the request times out. Live-Kafka I/O shell,
// excluded from coverage like the platform's other consumer shells.
[ExcludeFromCodeCoverage]
internal sealed class EngineEventConsumer : BackgroundService
{
    private const string Topic = "engine.events.v1";
    private const string GroupId = "tournament-bridge-engine-replies";

    private readonly PendingBotMoves pending;
    private readonly ILogger<EngineEventConsumer> logger;

    public EngineEventConsumer(PendingBotMoves pending, ILogger<EngineEventConsumer> logger)
    {
        this.pending = pending;
        this.logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Run(() => Consume(stoppingToken), stoppingToken);

    private void Consume(CancellationToken ct)
    {
        string bootstrap = Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP") ?? "kafka:9092";

        using IConsumer<string, MatchEvent> consumer = new ConsumerBuilder<string, MatchEvent>(
                new ConsumerConfig
                {
                    BootstrapServers = bootstrap,
                    GroupId = GroupId,
                    AutoOffsetReset = AutoOffsetReset.Latest,
                    EnableAutoCommit = true,
                })
            .SetValueDeserializer(ProtobufEventSerdes.Deserializer<MatchEvent>())
            .Build();

        consumer.Subscribe(Topic);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                ConsumeResult<string, MatchEvent> result = consumer.Consume(ct);
                MatchEvent evt = result.Message.Value;
                if (evt.PayloadCase == MatchEvent.PayloadOneofCase.BotMoveCalculated)
                {
                    BotMoveCalculated calc = evt.BotMoveCalculated;
                    if (!pending.Complete(calc.RequestId, calc.MoveUci))
                    {
                        logger.LogDebug(
                            "No waiter for bot-move reply {RequestId}", calc.RequestId);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            consumer.Close();
        }
    }
}
