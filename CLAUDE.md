# Tournament Bridge Service

Bridges maichess bots into external chess providers behind the `IExternalProvider` seam, drives moves via the Engine service (engine-drives/we-mirror), and mirrors each game into match-db as a read-only `external` match. Two providers: the **tournament-server** (full tournament lifecycle) and **Lichess** (single game via the Bot API). The bridge is the only maichess service that communicates with external providers.

## Contracts

- **REST:** `maichess-api-contracts/rest/tournament-bridge.md`
- **gRPC clients:** `protos/match-manager-service/v1/matches.proto`, `protos/engine-service/v1/bots.proto`
- **Tournament server API:** `tournament-server/api/openapi.yaml`
- **Generated stubs:** `Maichess.PlatformProtos` NuGet package (see `maichess-api-contracts/dotnet/`)

Implement against these contracts exactly. Document any blocker in `CONTRACT_NOTES.md`.

## Stack

- **Runtime:** ASP.NET (net10.0), C#, nullable enabled
- **Tournament server communication:** HTTP client (NDJSON streaming, form-encoded requests)
- **RPC:** gRPC clients (Match Manager: CreateMatch/SyncExternalMatch; Engine: ListBots) via `Maichess.PlatformProtos`
- **Kafka:** bot-move request/reply over `engine.commands.v1` / `engine.events.v1` (Confluent.Kafka, raw Protobuf)
- **Real-time:** SSE (Server-Sent Events) to browser clients; NDJSON from tournament server

## Structure

```
MaichessTournamentBridgeService/
  Chess/           # ChessPosition: pure UCI-move → FEN replay (for providers that don't send FEN, e.g. Lichess)
  Clients/         # HTTP client for tournament server API
  Models/          # DTOs for provider responses; GameUpdate (provider-normalized snapshot); bridge persistence
  Providers/       # IExternalProvider seam + ExternalGameRef; Lichess/ (LichessProvider IO, LichessEventParser pure, LichessStatus)
  Rest/            # REST endpoint handlers (TournamentEndpoints, ExternalGameEndpoints)
  Services/        # Core orchestration: GameDriver (pure), TournamentOrchestrator, LichessGameBridge,
                   #   LichessRegistrationService, mirror/catalog seams, BridgeConfig, RegistrationStore
  Program.cs       # DI wiring, Kestrel config
```

## Key Design Decisions

- **Engine-drives/we-mirror model:** The bridge opens the provider game stream, requests a bot move for each of our turns over Kafka (`IEngineMoveSource` → `BotMoveRequested` to `engine.commands.v1`, await the correlated `BotMoveCalculated` on `engine.events.v1`; Kafka task 09 replaced the synchronous `Engine.GetBestMove` gRPC call), submits moves to the provider, and creates/syncs `external` matches in match-db so the existing Watch/Past Matches UI works. See `CONTRACT_NOTES.md`.
- **Provider seam (`IExternalProvider`):** `Name`, `StreamGameAsync → IAsyncEnumerable<GameUpdate>`, `SubmitMoveAsync`. Each provider normalizes its wire format into `GameUpdate` (ms clocks). The tournament-server path predates the seam and keeps its `TournamentOrchestrator`; **Lichess** is implemented fully behind it (`LichessProvider` + `LichessGameBridge`). `GameDriver` (the pure action/decision logic) is reused untouched by both.
- **Lichess specifics:** the Bot API stream (`gameFull` then `gameState`) carries **no FEN** — the position is rebuilt from `initialFen` + the UCI move list with the pure `ChessPosition` (castling rights/en passant/move counters). Clocks are **already milliseconds** and pass through with no `*1000`. `LichessEventParser` (pure, 100% covered) does the NDJSON→`GameUpdate` translation; `LichessProvider` (typed `HttpClient`, `[ExcludeFromCodeCoverage]`) is the only IO. `LichessGameBridge` drives one game and creates the mirror match from the first `gameFull` so registration returns a watchable `match_id` immediately. Mirroring goes through `IExternalMatchMirror` (create + sync only, **no result-recording** → unrated by construction).
- **Two Lichess entry points** (`LichessRegistrationService`): `POST /external/lichess` attaches to an existing `game_id`; `POST /external/lichess/challenge` *creates* the game via `ILichessChallenger` (`POST /api/challenge/{user}` or `/api/challenge/ai`, pure request/response handling in `LichessChallengeBuilder`) then drives the returned id. Challenge clock inputs are **seconds**; AI games start immediately, user challenges start on accept (the game-stream connect retries on 404 to bridge the acceptance gap).
- **Pure vs IO boundaries:** `GameDriver` is a pure static class (no network, no state) that determines actions from game state. `TournamentOrchestrator` handles all IO (gRPC, HTTP, persistence). This makes the core logic unit-testable without mocking.
- **Registration store:** In-memory `ConcurrentDictionary` for tournament registrations and game mappings. Each registration tracks the director token, bot token, and match-db mappings.
- **Provider auth:** The bridge registers identities on the tournament server via `POST /api/auth/register`, receiving JWTs. For the maichess deployment, the tournament server uses a shared `TOURNAMENT_JWT_SECRET`. For external servers, users provide the secret.
- **External matches are unrated:** `RecordMatchResult` is never called for external matches. No impact on W/L/D or Glicko-2.
- **Time management:** Tournament server clock is in seconds; maichess is in milliseconds. The bridge converts `clock * 1000` when syncing. Engine time limit uses `remainingTime / estimatedMovesLeft`.
- **Dual real-time:** Tournament-level events flow via SSE (bridge → client). Game-level events (moves, end) flow via socket.io (match-db → socket service → client) because games are mirrored to match-db.
- **Config endpoint:** The default tournament server URL is configurable at runtime via `PUT /config`.

## Code Style

- All compiler warnings are errors (`TreatWarningsAsErrors=true`); `CS1591` is exempted.
- `EnableNETAnalyzers`, `AnalysisMode=All`, `EnforceCodeStyleInBuild=true`, StyleCop.Analyzers.
- Prefer direct, readable code. No repository pattern beyond `RegistrationStore`.
- Use C# records for DTOs and response models.
- Use sealed classes throughout; no public types unless required by framework.
- Validate inputs at REST boundaries. Trust internal data after that.
- One type per file (SA1402 enforced).

## Docker & CI

- **Dockerfile:** `MaichessTournamentBridgeService/Dockerfile` — multi-stage build (SDK → runtime)
- **CI:** `.github/workflows/docker-publish.yml` — builds and pushes to `ghcr.io/maichess/maichess-tournament-bridge-service` on main push or version tag
- **NuGet auth:** `nuget.config` uses `GITHUB_ACTOR`/`GITHUB_TOKEN` env vars for the private GitHub Packages feed

## Dependencies

- **Match Manager (gRPC):** `CreateMatch` to create external matches, `SyncExternalMatch` to update them (via `IExternalMatchMirror`)
- **Engine:** bot moves over Kafka (`engine.commands.v1` → `engine.events.v1`); `ListBots` over gRPC to list available bots / validate a bot id (via `IBotCatalog`)
- **Tournament Server (HTTP):** Full lifecycle — register, create, join, start, stream, move, results
- **Lichess Bot API (HTTP):** `GET /api/account`, `GET /api/bot/game/stream/{id}` (NDJSON), `POST /api/bot/game/{id}/move/{uci}`, `POST /api/challenge/{user}` / `POST /api/challenge/ai`; per-game bot OAuth token. Base URL configurable via `Lichess:ApiUrl` / `LICHESS_API_URL` (default `https://lichess.org`).

## Entity Framework Rules

N/A — this service uses in-memory state. Persistence via database-service may be added later.
