namespace MaichessTournamentBridgeService.Models;

// A request to create a Lichess game by challenging an opponent. `Opponent` is either a
// Lichess username or the literal "ai" (play Lichess's Stockfish). Clock fields are in
// **seconds** — Lichess's challenge API takes seconds, even though the game stream then
// reports remaining time in ms. `Level` (1–8) only applies to "ai".
internal sealed record LichessChallenge(
    string Opponent,
    int ClockLimitSeconds,
    int ClockIncrementSeconds,
    bool Rated,
    int Level);
