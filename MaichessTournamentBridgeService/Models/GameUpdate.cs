namespace MaichessTournamentBridgeService.Models;

// Provider-normalized snapshot of an external game, emitted by every IExternalProvider
// so the per-game bridge loop is provider-agnostic. Clocks are always milliseconds
// (match-db's unit): the tournament-server adapter converts its seconds wire format,
// the Lichess adapter passes its native ms straight through. OurColor/OpponentName are
// stamped from the provider's game-start handshake (Lichess gameFull); OpponentName is
// only meaningful on the first update of a game.
internal sealed record GameUpdate(
    IReadOnlyList<string> Moves,
    string Fen,
    string Turn,
    string Status,
    string RawStatus,
    long WhiteTimeMs,
    long BlackTimeMs,
    string OurColor,
    string OpponentName)
{
    // Our normalized status vocabulary, matching GameDriverState: "ongoing",
    // "white_won", "black_won", "draw".
    internal bool IsFinished => Status is not "ongoing";

    internal bool IsOurTurn => Turn == OurColor;
}
