using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MaichessTournamentBridgeService.Models;

namespace MaichessTournamentBridgeService.Providers.Lichess;

// Lichess Bot API implementation of IExternalProvider (https://lichess.org/api#tag/Bot).
// Holds only the typed HttpClient; the per-game bot OAuth token travels on ExternalGameRef
// so one singleton serves every user's game. Live HTTP — excluded from coverage; the
// NDJSON→GameUpdate translation it delegates to (LichessEventParser) is tested in full.
[ExcludeFromCodeCoverage]
internal sealed class LichessProvider(HttpClient httpClient) : IExternalProvider, ILichessChallenger
{
    internal const string HttpClientName = "lichess";

    // A just-accepted challenge can take a moment to become a streamable game, so the
    // first connect retries on 404 before giving up.
    private const int MaxConnectAttempts = 10;

    // A single game stream is a long-lived HTTP response that can drop mid-game (idle
    // close, proxy reset, HTTP/2 GOAWAY, client timeout). Reconnect and resume up to this
    // many consecutive failures; a healthy update resets the budget.
    private const int MaxReconnectAttempts = 6;

    private static readonly TimeSpan ConnectRetryDelay = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(2);

    public string Name => "lichess";

    public async Task<string> CreateChallengeAsync(
        string token, LichessChallenge challenge, CancellationToken ct)
    {
        using HttpRequestMessage request = new(
            HttpMethod.Post, LichessChallengeBuilder.BuildPath(challenge))
        {
            Content = new FormUrlEncodedContent(LichessChallengeBuilder.BuildForm(challenge)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using HttpResponseMessage response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync(ct);
        return LichessChallengeBuilder.ParseGameId(json);
    }

    public async IAsyncEnumerable<GameUpdate> StreamGameAsync(
        ExternalGameRef game, [EnumeratorCancellation] CancellationToken ct)
    {
        string accountId = await GetAccountIdAsync(game.Token, ct);

        string initialFen = MaichessTournamentBridgeService.Chess.ChessPosition.StartFen;
        string ourColor = "white";
        string opponentName = "Lichess opponent";
        bool gameFullSeen = false;
        bool finished = false;
        bool everConnected = false;
        int reconnects = 0;

        // Reconnect-and-resume loop: Lichess re-sends gameFull (full current state) on
        // every connect, so the caller's fold rebuilds after a drop. Stop on a terminal
        // game state, on shutdown, or after too many consecutive failures. The very first
        // connect is NOT tolerated (everConnected gate) so a bad token / unknown game id
        // still surfaces to the registration caller as it did before.
        while (!finished && !ct.IsCancellationRequested)
        {
            HttpResponseMessage response;
            try
            {
                response = await OpenGameStreamAsync(game, ct);
            }
            catch (Exception ex) when (everConnected && IsTransient(ex) && !ct.IsCancellationRequested)
            {
                if (++reconnects > MaxReconnectAttempts)
                {
                    yield break;
                }

                await Task.Delay(ReconnectDelay, ct);
                continue;
            }

            everConnected = true;
            bool dropped = false;
            using (response)
            {
                await using Stream stream = await response.Content.ReadAsStreamAsync(ct);
                using StreamReader reader = new(stream);

                while (!finished)
                {
                    string? line;
                    try
                    {
                        line = await reader.ReadLineAsync(ct);
                    }
                    catch (Exception ex) when (IsTransient(ex) && !ct.IsCancellationRequested)
                    {
                        dropped = true;
                        break;
                    }

                    if (line is null)
                    {
                        // Clean EOF before a terminal state — the stream dropped; reconnect.
                        dropped = true;
                        break;
                    }

                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    if (!gameFullSeen)
                    {
                        (initialFen, ourColor, opponentName) = ReadGameFull(line, accountId);
                        gameFullSeen = true;
                    }

                    GameUpdate? update = LichessEventParser.Parse(line, initialFen, ourColor, opponentName);
                    if (update is not null)
                    {
                        reconnects = 0;
                        finished = update.IsFinished;
                        yield return update;
                    }
                }
            }

            if (dropped && !finished && !ct.IsCancellationRequested)
            {
                if (++reconnects > MaxReconnectAttempts)
                {
                    yield break;
                }

                await Task.Delay(ReconnectDelay, ct);
            }
        }
    }

    public async Task SubmitMoveAsync(ExternalGameRef game, string uci, CancellationToken ct)
    {
        using HttpRequestMessage request = new(
            HttpMethod.Post, $"/api/bot/game/{game.GameId}/move/{uci}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", game.Token);

        using HttpResponseMessage response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    // Connection-level failures that warrant a reconnect rather than ending the game.
    // OperationCanceledException is treated as transient only at call sites guarded by a
    // `!ct.IsCancellationRequested` check, so a requested shutdown still propagates.
    private static bool IsTransient(Exception ex) =>
        ex is IOException or HttpRequestException or OperationCanceledException;

    private static (string InitialFen, string OurColor, string OpponentName) ReadGameFull(
        string line, string accountId)
    {
        using var doc = JsonDocument.Parse(line);
        JsonElement root = doc.RootElement;
        string ourColor = LichessEventParser.ResolveColor(root, accountId);
        return (
            LichessEventParser.ResolveInitialFen(root),
            ourColor,
            LichessEventParser.ResolveOpponentName(root, ourColor));
    }

    private async Task<HttpResponseMessage> OpenGameStreamAsync(
        ExternalGameRef game, CancellationToken ct)
    {
        for (int attempt = 1; ; attempt++)
        {
            using HttpRequestMessage request = new(
                HttpMethod.Get, $"/api/bot/game/stream/{game.GameId}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", game.Token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/x-ndjson"));

            HttpResponseMessage response = await httpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, ct);

            if (response.StatusCode != HttpStatusCode.NotFound || attempt >= MaxConnectAttempts)
            {
                response.EnsureSuccessStatusCode();
                return response;
            }

            response.Dispose();
            await Task.Delay(ConnectRetryDelay, ct);
        }
    }

    private async Task<string> GetAccountIdAsync(string token, CancellationToken ct)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, "/api/account");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using HttpResponseMessage response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        await using Stream stream = await response.Content.ReadAsStreamAsync(ct);
        using JsonDocument doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return doc.RootElement.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("Lichess account response missing id");
    }
}
