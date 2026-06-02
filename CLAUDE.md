# Tournament Bridge Service

Proxies tournament lifecycle to an external tournament server, registers a maichess bot to play, drives moves via the Engine service, and mirrors each game into match-db as a read-only `external` match. The bridge is the only maichess service that communicates with external tournament servers.

## Contracts

- **REST:** `maichess-api-contracts/rest/tournament-bridge.md`
- **gRPC clients:** `protos/match-manager-service/v1/matches.proto`, `protos/engine-service/v1/bots.proto`
- **Tournament server API:** `tournament-server/api/openapi.yaml`
- **Generated stubs:** `Maichess.PlatformProtos` NuGet package (see `maichess-api-contracts/dotnet/`)

Implement against these contracts exactly. Document any blocker in `CONTRACT_NOTES.md`.

## Stack

- **Runtime:** ASP.NET (net10.0), C#, nullable enabled
- **Tournament server communication:** HTTP client (NDJSON streaming, form-encoded requests)
- **RPC:** gRPC clients (Match Manager, Engine) via `Maichess.PlatformProtos`
- **Real-time:** SSE (Server-Sent Events) to browser clients; NDJSON from tournament server

## Structure

```
MaichessTournamentBridgeService/
  Clients/         # HTTP client for tournament server API
  Models/          # DTOs for tournament server responses and bridge persistence
  Rest/            # REST endpoint handlers
  Services/        # Core orchestration: GameDriver (pure), TournamentOrchestrator, BridgeConfig, RegistrationStore
  Program.cs       # DI wiring, Kestrel config
```

## Key Design Decisions

- **Engine-drives/we-mirror model:** The bridge opens the tournament game stream, calls Engine `GetBestMove` for each of our turns, submits moves to the tournament server, and creates/syncs `external` matches in match-db so the existing Watch/Past Matches UI works.
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

- **Match Manager (gRPC):** `CreateMatch` to create external matches, `SyncExternalMatch` to update them
- **Engine (gRPC):** `GetBestMove` to drive bot moves, `ListBots` to list available bots
- **Tournament Server (HTTP):** Full lifecycle — register, create, join, start, stream, move, results

## Entity Framework Rules

N/A — this service uses in-memory state. Persistence via database-service may be added later.
