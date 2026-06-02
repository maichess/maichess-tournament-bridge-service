namespace MaichessTournamentBridgeService.Services;

internal sealed class BridgeConfig(string defaultServerUrl)
{
    private readonly Lock _lock = new();

    internal string DefaultServerUrl { get; private set; } = defaultServerUrl;

    internal void SetDefaultServerUrl(string url)
    {
        lock (_lock)
        {
            DefaultServerUrl = url;
        }
    }

    internal string ResolveServerUrl(string? serverOverride) =>
        string.IsNullOrWhiteSpace(serverOverride) ? DefaultServerUrl : serverOverride;
}
