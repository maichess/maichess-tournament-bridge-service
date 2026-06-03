using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MaichessTournamentBridgeService.Models;

namespace MaichessTournamentBridgeService.Clients;

internal sealed class TournamentServerClient(HttpClient httpClient)
{
    internal async Task<RegisterIdentityResponse> RegisterAsync(
        string serverUrl, string name, bool isBot, CancellationToken ct)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, $"{serverUrl}/api/auth/register")
        {
            Content = JsonContent.Create(new RegisterIdentityRequest(name, isBot)),
        };

        using HttpResponseMessage response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RegisterIdentityResponse>(ct)
            ?? throw new InvalidOperationException("Empty register response");
    }

    internal async Task<TournamentListResponse> ListTournamentsAsync(
        string serverUrl, CancellationToken ct)
    {
        using HttpResponseMessage response = await httpClient.GetAsync(
            $"{serverUrl}/api/tournament", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TournamentListResponse>(ct)
            ?? throw new InvalidOperationException("Empty tournament list response");
    }

    internal async Task<Tournament> CreateTournamentAsync(
        string serverUrl,
        string token,
        Dictionary<string, string> formFields,
        CancellationToken ct)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, $"{serverUrl}/api/tournament")
        {
            Content = new FormUrlEncodedContent(formFields),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        using HttpResponseMessage response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Tournament>(ct)
            ?? throw new InvalidOperationException("Empty create tournament response");
    }

    internal async Task<Tournament> GetTournamentAsync(
        string serverUrl, string tournamentId, CancellationToken ct)
    {
        using HttpResponseMessage response = await httpClient.GetAsync(
            $"{serverUrl}/api/tournament/{tournamentId}", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Tournament>(ct)
            ?? throw new InvalidOperationException("Empty tournament response");
    }

    internal async Task DeleteTournamentAsync(
        string serverUrl, string token, string tournamentId, CancellationToken ct)
    {
        using HttpRequestMessage request = new(HttpMethod.Delete, $"{serverUrl}/api/tournament/{tournamentId}");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        using HttpResponseMessage response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    internal async Task<Tournament> StartTournamentAsync(
        string serverUrl, string token, string tournamentId, CancellationToken ct)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, $"{serverUrl}/api/tournament/{tournamentId}/start");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        using HttpResponseMessage response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Tournament>(ct)
            ?? throw new InvalidOperationException("Empty start tournament response");
    }

    internal async Task JoinTournamentAsync(
        string serverUrl, string token, string tournamentId, CancellationToken ct)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, $"{serverUrl}/api/tournament/{tournamentId}/join");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        using HttpResponseMessage response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    internal async Task WithdrawFromTournamentAsync(
        string serverUrl, string token, string tournamentId, CancellationToken ct)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, $"{serverUrl}/api/tournament/{tournamentId}/withdraw");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        using HttpResponseMessage response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    internal async Task<RoundPairingsResponse> GetRoundPairingsAsync(
        string serverUrl, string tournamentId, int round, CancellationToken ct)
    {
        using HttpResponseMessage response = await httpClient.GetAsync(
            $"{serverUrl}/api/tournament/{tournamentId}/round/{round}", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RoundPairingsResponse>(ct)
            ?? throw new InvalidOperationException("Empty round pairings response");
    }

    internal async Task SubmitMoveAsync(
        string serverUrl,
        string token,
        string tournamentId,
        string gameId,
        string uci,
        CancellationToken ct)
    {
        using HttpRequestMessage request = new(
            HttpMethod.Post,
            $"{serverUrl}/api/tournament/{tournamentId}/game/{gameId}/move/{uci}");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        using HttpResponseMessage response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    internal async IAsyncEnumerable<TournamentEvent> StreamTournamentAsync(
        string serverUrl,
        string token,
        string tournamentId,
        [EnumeratorCancellation] CancellationToken ct,
        Action? onConnected = null)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, $"{serverUrl}/api/tournament/{tournamentId}/stream");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/x-ndjson"));

        using HttpResponseMessage response = await httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        onConnected?.Invoke();

        await using Stream stream = await response.Content.ReadAsStreamAsync(ct);
        using StreamReader reader = new(stream);

        while (!ct.IsCancellationRequested)
        {
            string? line = await reader.ReadLineAsync(ct);
            if (line is null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            TournamentEvent? evt = JsonSerializer.Deserialize<TournamentEvent>(line);
            if (evt is not null)
            {
                yield return evt;
            }
        }
    }

    internal async IAsyncEnumerable<GameEvent> StreamGameAsync(
        string serverUrl,
        string token,
        string tournamentId,
        string gameId,
        [EnumeratorCancellation] CancellationToken ct,
        Func<Task>? onConnected = null)
    {
        using HttpRequestMessage request = new(
            HttpMethod.Get,
            $"{serverUrl}/api/tournament/{tournamentId}/game/{gameId}/stream");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/x-ndjson"));

        using HttpResponseMessage response = await httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        // The game-event subscription is registered server-side by the time the
        // response headers arrive, so any move published from here on is queued
        // for us. Play our opening move now — before reading the stream — so a
        // fast opponent reply cannot slip through before we are listening.
        if (onConnected is not null)
        {
            await onConnected();
        }

        await using Stream stream = await response.Content.ReadAsStreamAsync(ct);
        using StreamReader reader = new(stream);

        while (!ct.IsCancellationRequested)
        {
            string? line = await reader.ReadLineAsync(ct);
            if (line is null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            GameEvent? evt = JsonSerializer.Deserialize<GameEvent>(line);
            if (evt is not null)
            {
                yield return evt;
            }
        }
    }

    internal async Task<List<TournamentResult>> GetResultsAsync(
        string serverUrl, string tournamentId, CancellationToken ct)
    {
        using HttpRequestMessage request = new(
            HttpMethod.Get,
            $"{serverUrl}/api/tournament/{tournamentId}/results");
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/x-ndjson"));

        using HttpResponseMessage response = await httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        List<TournamentResult> results = [];
        await using Stream stream = await response.Content.ReadAsStreamAsync(ct);
        using StreamReader reader = new(stream);

        while (true)
        {
            string? line = await reader.ReadLineAsync(ct);
            if (line is null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            TournamentResult? result = JsonSerializer.Deserialize<TournamentResult>(line);
            if (result is not null)
            {
                results.Add(result);
            }
        }

        return results;
    }
}
