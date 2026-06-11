# Contract Notes — Tournament Bridge Service

## Kafka task 09 — bot moves moved off `Engine.GetBestMove` → PUBLISH HANDOFF

The bridge no longer calls the synchronous `Engine.GetBestMove` gRPC RPC (removed from
`bots.proto`). Bot moves for external (tournament) games now use a Kafka request/reply loop:

- `Services/IEngineMoveSource` seam → sole impl `Kafka/KafkaEngineMoveSource`
  (`[ExcludeFromCodeCoverage]`): produces a `BotMoveRequested` (wrapped in the shared `MatchEvent`
  envelope, keyed by `request_id`) to **`engine.commands.v1`** and awaits the correlated
  `BotMoveCalculated` from **`engine.events.v1`** (30 s timeout).
- `Kafka/EngineEventConsumer` (`BackgroundService`, `[ExcludeFromCodeCoverage]`) consumes
  `engine.events.v1` and completes the waiter via the pure, unit-tested `Kafka/PendingBotMoves`
  registry (`request_id` → `TaskCompletionSource`).
- A **dedicated** topic pair (not `match.events.v1`) keeps external-game requests off the match log,
  whose projector would otherwise create phantom live matches.

**No new contract was needed for this loop** — it reuses the existing `MatchEvent` envelope
(`BotMoveRequested`/`BotMoveCalculated` already live in its oneof), which is already in
`Maichess.PlatformProtos`. So this rework itself required no publish.

`TournamentOrchestrator` now takes `IEngineMoveSource` instead of `Bots.BotsClient`; the **`ListBots`**
gRPC client is unchanged (still used by `TournamentEndpoints`). Build green, 30 tests pass
(+4 `PendingBotMoves` tests).

**Shared publish handoff:** `GetBestMove` is removed from `bots.proto`. After the user tags/pushes the
new `platform-protos`, bump `Maichess.PlatformProtos` in the `.csproj` and rebuild/test. No code
change expected here — the bridge no longer references any removed type.
