using MaichessTournamentBridgeService.Models;

namespace MaichessTournamentBridgeService.Providers;

// The protocol seam between the bridge and an external chess provider. The engine-drives
// game loop (LichessGameBridge / TournamentOrchestrator) is written against this so the
// only thing a new provider has to supply is its wire protocol: how to stream a game's
// state and how to submit a move. Parsing the provider's NDJSON into GameUpdate lives
// inside each implementation (see LichessEventParser). tournament-server and Lichess are
// the two implementations.
internal interface IExternalProvider
{
    // Provider identifier mirrored into match-db as Match.external_provider
    // (e.g. "lichess", "tournament-server").
    string Name { get; }

    // Streams the game's state as provider-normalized GameUpdates. The first update
    // carries the game-start handshake (OurColor, OpponentName); subsequent updates
    // reflect each move. The sequence ends when the game finishes or the stream closes.
    IAsyncEnumerable<GameUpdate> StreamGameAsync(ExternalGameRef game, CancellationToken ct);

    // Submits our move (UCI) for the given game.
    Task SubmitMoveAsync(ExternalGameRef game, string uci, CancellationToken ct);
}
