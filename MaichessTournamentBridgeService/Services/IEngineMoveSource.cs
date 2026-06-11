namespace MaichessTournamentBridgeService.Services;

// Seam for obtaining a bot's best move for an external (tournament) game. The sole
// implementation (KafkaEngineMoveSource) issues the request over engine.commands.v1
// and awaits the correlated reply on engine.events.v1, replacing the synchronous
// Engine.GetBestMove gRPC call (removed in Kafka task 09). Keeping it a seam lets
// TournamentOrchestrator stay transport-agnostic and the Kafka glue stay excluded.
internal interface IEngineMoveSource
{
    // Returns the engine's best move (UCI) for botId at fen. Throws on timeout or a
    // produce failure; the caller treats that like any other engine error.
    Task<string> GetBestMoveAsync(string botId, string fen, int timeLimitMs, CancellationToken ct);
}
