namespace MaichessTournamentBridgeService.Services;

internal enum LichessRegistrationOutcome
{
    Created,
    MissingBot,
    MissingToken,
    MissingGame,
    MissingOpponent,
    UnknownBot,
    ProviderError,
}
