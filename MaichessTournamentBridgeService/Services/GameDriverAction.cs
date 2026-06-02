namespace MaichessTournamentBridgeService.Services;

internal enum GameDriverAction
{
    WaitForOpponent,
    RequestEngineMove,
    SyncToMatchDb,
    FinalizeMatch,
}
