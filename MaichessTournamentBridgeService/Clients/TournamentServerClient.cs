using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using MaichessTournamentBridgeService.Models;

namespace MaichessTournamentBridgeService.Clients;

internal sealed class TournamentServerClient(HttpClient httpClient)
{
    // Omit null properties so optional fields (endpoint, bot metadata, opening key)
    // are not sent as JSON nulls — the tournament server derives/defaults them.
    private static readonly JsonSerializerOptions OmitNulls = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

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

    internal async Task<RegisteredBot> RegisterBotAsync(
        string serverUrl, string token, RegisterBotRequest payload, CancellationToken ct)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, $"{serverUrl}/api/bots")
        {
            Content = JsonContent.Create(payload, options: OmitNulls),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        using HttpResponseMessage response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RegisteredBot>(ct)
            ?? throw new InvalidOperationException("Empty register bot response");
    }

    internal async Task AddParticipantAsync(
        string serverUrl, string token, string tournamentId, string botId, CancellationToken ct)
    {
        using HttpRequestMessage request = new(
            HttpMethod.Post, $"{serverUrl}/api/tournament/{tournamentId}/participants")
        {
            Content = JsonContent.Create(new AddParticipantRequest(botId)),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        using HttpResponseMessage response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    internal async Task<Opening> RegisterOpeningAsync(
        string serverUrl, string token, string name, string fen, string? key, CancellationToken ct)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, $"{serverUrl}/api/openings")
        {
            Content = JsonContent.Create(new { name, fen, key }, options: OmitNulls),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        using HttpResponseMessage response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Opening>(ct)
            ?? throw new InvalidOperationException("Empty register opening response");
    }

    internal async Task<string> GetAnalyticsExportAsync(
        string serverUrl, string tournamentId, CancellationToken ct)
    {
        using HttpResponseMessage response = await httpClient.GetAsync(
            $"{serverUrl}/api/tournament/{tournamentId}/analytics-export", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    internal async Task<RegisteredBotsResponse> ListRegisteredBotsAsync(
        string serverUrl, CancellationToken ct)
    {
        using HttpResponseMessage response = await httpClient.GetAsync(
            $"{serverUrl}/api/bots", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RegisteredBotsResponse>(ct)
            ?? throw new InvalidOperationException("Empty registered bots response");
    }

    internal async Task DeleteRegisteredBotAsync(
        string serverUrl, string token, string botId, CancellationToken ct)
    {
        using HttpRequestMessage request = new(HttpMethod.Delete, $"{serverUrl}/api/bots/{botId}");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        using HttpResponseMessage response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    internal async Task<OpeningsResponse> ListOpeningsAsync(
        string serverUrl, CancellationToken ct)
    {
        using HttpResponseMessage response = await httpClient.GetAsync(
            $"{serverUrl}/api/openings", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OpeningsResponse>(ct)
            ?? throw new InvalidOperationException("Empty openings response");
    }

    internal async Task<string> ExportGamesAsync(
        string serverUrl, string tournamentId, CancellationToken ct)
    {
        using HttpRequestMessage request = new(
            HttpMethod.Get, $"{serverUrl}/api/tournament/{tournamentId}/export/games");
        request.Headers.Accept.Add(
            new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/x-chess-pgn"));

        using HttpResponseMessage response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
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

    internal async Task<GameState> GetGameAsync(
        string serverUrl, string tournamentId, string gameId, CancellationToken ct)
    {
        using HttpResponseMessage response = await httpClient.GetAsync(
            $"{serverUrl}/api/tournament/{tournamentId}/game/{gameId}", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<GameState>(ct)
            ?? throw new InvalidOperationException("Empty game state response");
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
        [EnumeratorCancellation] CancellationToken ct)
    {
        using HttpRequestMessage request = new(
            HttpMethod.Get,
            $"{serverUrl}/api/tournament/{tournamentId}/game/{gameId}/stream");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/x-ndjson"));

        using HttpResponseMessage response = await httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

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
