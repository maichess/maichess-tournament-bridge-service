using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using System.Text.Json;
using Maichess.Engine.V1;
using MaichessTournamentBridgeService.Clients;
using MaichessTournamentBridgeService.Models;
using MaichessTournamentBridgeService.Services;

namespace MaichessTournamentBridgeService.Rest;

[ExcludeFromCodeCoverage]
internal static class TournamentEndpoints
{
    internal static void MapTournamentEndpoints(this WebApplication app)
    {
        RouteGroupBuilder group = app.MapGroup(string.Empty)
            .RequireAuthorization();

        group.MapGet("/tournaments", ListTournaments);
        group.MapPost("/tournaments", CreateTournament);
        group.MapGet("/tournaments/{id}", GetTournament);
        group.MapDelete("/tournaments/{id}", DeleteTournament);
        group.MapPost("/tournaments/{id}/start", StartTournament);
        group.MapPost("/tournaments/{id}/register", RegisterBot);
        group.MapDelete("/tournaments/{id}/register", WithdrawBot);
        group.MapGet("/tournaments/{id}/rounds/{round:int}", GetRoundPairings);
        group.MapGet("/tournaments/{id}/results", GetResults);
        group.MapGet("/tournaments/{id}/export", ExportGames);
        group.MapGet("/tournaments/{id}/stream", StreamTournament);
        group.MapGet("/bots", ListBots);
        group.MapGet("/openings", ListOpenings);
        group.MapGet("/config", GetConfig);
        group.MapPut("/config", UpdateConfig);
    }

    private static async Task<IResult> ListTournaments(
        string? server,
        TournamentServerClient client,
        BridgeConfig config,
        CancellationToken ct)
    {
        string serverUrl = config.ResolveServerUrl(server);
        try
        {
            TournamentListResponse result = await client.ListTournamentsAsync(serverUrl, ct);
            return Results.Ok(result);
        }
        catch (HttpRequestException)
        {
            return Results.StatusCode(502);
        }
    }

    private static async Task<IResult> CreateTournament(
        string? server,
        HttpRequest httpRequest,
        TournamentServerClient client,
        BridgeConfig config,
        RegistrationStore store,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        string serverUrl = config.ResolveServerUrl(server);
        string userId = GetUserId(user);

        JsonDocument body = await JsonDocument.ParseAsync(httpRequest.Body, cancellationToken: ct);

        string name = body.RootElement.GetProperty("name").GetString()
            ?? throw new InvalidOperationException("name is required");

        RegisterIdentityResponse directorIdentity = await client.RegisterAsync(
            serverUrl, $"director-{userId}", false, ct);

        Dictionary<string, string> formFields = [];
        foreach (JsonProperty prop in body.RootElement.EnumerateObject())
        {
            // GetString() throws on non-string kinds (e.g. the boolean `rated`),
            // so only call it for strings; numbers and booleans serialise to their
            // raw JSON literal ("300", "true"), which is what the form expects.
            formFields[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                ? prop.Value.GetString()!
                : prop.Value.GetRawText();
        }

        Tournament tournament = await client.CreateTournamentAsync(
            serverUrl, directorIdentity.Token, formFields, ct);

        Registration registration = store.Save(new Registration
        {
            Id = $"reg_{Guid.NewGuid():N}",
            ServerUrl = serverUrl,
            TournamentId = tournament.Id,
            TournamentName = name,
            MaichessBotId = string.Empty,
            MaichessUserId = userId,
            Status = "created",
            DirectorToken = directorIdentity.Token,
            BotToken = string.Empty,
        });

        return Results.Created($"/tournaments/{tournament.Id}", new
        {
            registration_id = registration.Id,
            tournament,
        });
    }

    private static async Task<IResult> GetTournament(
        string id,
        string? server,
        TournamentServerClient client,
        BridgeConfig config,
        RegistrationStore store,
        CancellationToken ct)
    {
        string serverUrl = config.ResolveServerUrl(server);
        try
        {
            Tournament tournament = await client.GetTournamentAsync(serverUrl, id, ct);
            IReadOnlyList<Registration> registrations = store.FindAllByTournament(serverUrl, id);

            var botRegistrations = registrations
                .Where(r => !string.IsNullOrEmpty(r.MaichessBotId))
                .Select(r => new
                {
                    registration_id = r.Id,
                    maichess_bot_id = r.MaichessBotId,
                    status = r.Status,
                })
                .ToList();

            var allMappings = registrations
                .SelectMany(r => r.GameMappings)
                .ToList();

            Registration? director = registrations.FirstOrDefault(r => !string.IsNullOrEmpty(r.DirectorToken));

            return Results.Ok(new
            {
                tournament,
                is_director = director is not null,
                registrations = botRegistrations,
                game_mappings = allMappings.Select(m => new
                {
                    tournament_game_id = m.TournamentGameId,
                    match_db_id = m.MatchDbMatchId,
                }),
            });
        }
        catch (HttpRequestException)
        {
            return Results.NotFound();
        }
    }

    private static async Task<IResult> DeleteTournament(
        string id,
        string? server,
        TournamentServerClient client,
        BridgeConfig config,
        RegistrationStore store,
        CancellationToken ct)
    {
        string serverUrl = config.ResolveServerUrl(server);
        Registration? director = store.FindDirector(serverUrl, id);
        if (director is null)
        {
            return Results.StatusCode(403);
        }

        try
        {
            await client.DeleteTournamentAsync(serverUrl, director.DirectorToken, id, ct);

            foreach (Registration reg in store.FindAllByTournament(serverUrl, id))
            {
                reg.Status = "terminated";
                store.Save(reg);
            }

            return Results.NoContent();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            return Results.Conflict();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            return Results.Forbid();
        }
    }

    private static async Task<IResult> StartTournament(
        string id,
        string? server,
        TournamentServerClient client,
        BridgeConfig config,
        RegistrationStore store,
        TournamentOrchestrator orchestrator,
        CancellationToken ct)
    {
        string serverUrl = config.ResolveServerUrl(server);
        Registration? director = store.FindDirector(serverUrl, id);
        if (director is null)
        {
            return Results.StatusCode(403);
        }

        try
        {
            List<Task> connectTasks = [];
            foreach (Registration reg in store.FindAllByTournament(serverUrl, id)
                .Where(r => !string.IsNullOrEmpty(r.BotToken)))
            {
                reg.Status = "active";
                store.Save(reg);
                connectTasks.Add(orchestrator.StartDriving(reg));
            }

            await Task.WhenAll(connectTasks);

            Tournament tournament = await client.StartTournamentAsync(
                serverUrl, director.DirectorToken, id, ct);

            return Results.Ok(tournament);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            return Results.Conflict();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            return Results.Forbid();
        }
    }

    private static async Task<IResult> RegisterBot(
        string id,
        string? server,
        HttpRequest httpRequest,
        TournamentServerClient client,
        BridgeConfig config,
        RegistrationStore store,
        Bots.BotsClient engineClient,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        string serverUrl = config.ResolveServerUrl(server);
        string userId = GetUserId(user);

        JsonDocument body = await JsonDocument.ParseAsync(httpRequest.Body, cancellationToken: ct);
        string botId = body.RootElement.GetProperty("bot_id").GetString()
            ?? throw new InvalidOperationException("bot_id is required");

        Registration? existing = store.FindByBot(serverUrl, id, botId);
        if (existing is not null)
        {
            return Results.Conflict(new { error = $"Bot {botId} is already registered" });
        }

        ListBotsResponse bots = await engineClient.ListBotsAsync(
            new ListBotsRequest(), cancellationToken: ct);
        Bot? bot = bots.Bots.FirstOrDefault(b => b.Id == botId);
        if (bot is null)
        {
            return Results.BadRequest(new { error = $"Unknown bot: {botId}" });
        }

        RegisterIdentityResponse botIdentity = await client.RegisterAsync(
            serverUrl, bot.Name, true, ct);
        await client.JoinTournamentAsync(serverUrl, botIdentity.Token, id, ct);

        Registration reg = store.Save(new Registration
        {
            Id = $"reg_{Guid.NewGuid():N}",
            ServerUrl = serverUrl,
            TournamentId = id,
            TournamentName = string.Empty,
            MaichessBotId = botId,
            MaichessUserId = userId,
            Status = "registered",
            DirectorToken = string.Empty,
            BotToken = botIdentity.Token,
            TournamentBotId = botIdentity.Id,
        });

        return Results.Ok(new
        {
            registration_id = reg.Id,
            tournament_id = id,
            bot_id = botId,
            status = "registered",
        });
    }

    private static async Task<IResult> WithdrawBot(
        string id,
        string? server,
        string? bot_id,
        TournamentServerClient client,
        BridgeConfig config,
        RegistrationStore store,
        CancellationToken ct)
    {
        string serverUrl = config.ResolveServerUrl(server);

        if (string.IsNullOrEmpty(bot_id))
        {
            return Results.BadRequest(new { error = "bot_id query parameter is required" });
        }

        Registration? reg = store.FindByBot(serverUrl, id, bot_id);
        if (reg is null)
        {
            return Results.NotFound();
        }

        try
        {
            await client.WithdrawFromTournamentAsync(serverUrl, reg.BotToken, id, ct);
            reg.Status = "withdrawn";
            store.Save(reg);
            return Results.NoContent();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            return Results.Conflict();
        }
    }

    private static async Task<IResult> GetRoundPairings(
        string id,
        int round,
        string? server,
        TournamentServerClient client,
        BridgeConfig config,
        RegistrationStore store,
        CancellationToken ct)
    {
        string serverUrl = config.ResolveServerUrl(server);
        RoundPairingsResponse pairings;
        try
        {
            pairings = await client.GetRoundPairingsAsync(serverUrl, id, round, ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Results.NotFound();
        }

        var mappings = store.FindAllByTournament(serverUrl, id)
            .SelectMany(r => r.GameMappings)
            .ToList();

        var enriched = pairings.Pairings.Select(p =>
        {
            string? gameId = p.Matches.Count > 0 ? p.Matches[0].GameId : null;
            string? matchDbId = gameId is not null
                ? mappings.FirstOrDefault(m => m.TournamentGameId == gameId)?.MatchDbMatchId
                : null;
            return new
            {
                white = p.White,
                black = p.Black,
                gameId,
                match_db_id = matchDbId,
                winner = p.AggregateOutcome,
            };
        });

        return Results.Ok(new { round, pairings = enriched });
    }

    private static async Task<IResult> GetResults(
        string id,
        string? server,
        TournamentServerClient client,
        BridgeConfig config,
        CancellationToken ct)
    {
        string serverUrl = config.ResolveServerUrl(server);
        List<TournamentResult> results = await client.GetResultsAsync(serverUrl, id, ct);
        return Results.Ok(new { results });
    }

    private static async Task<IResult> ExportGames(
        string id,
        string? server,
        TournamentServerClient client,
        BridgeConfig config,
        CancellationToken ct)
    {
        string serverUrl = config.ResolveServerUrl(server);
        try
        {
            string pgn = await client.ExportGamesAsync(serverUrl, id, ct);
            return Results.Text(pgn, "application/x-chess-pgn");
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Results.NotFound();
        }
        catch (HttpRequestException)
        {
            return Results.StatusCode(502);
        }
    }

    private static async Task<IResult> ListOpenings(
        string? server,
        TournamentServerClient client,
        BridgeConfig config,
        CancellationToken ct)
    {
        string serverUrl = config.ResolveServerUrl(server);
        try
        {
            OpeningsResponse openings = await client.ListOpeningsAsync(serverUrl, ct);
            return Results.Ok(openings);
        }
        catch (HttpRequestException)
        {
            return Results.StatusCode(502);
        }
    }

    private static async Task StreamTournament(
        string id,
        string? server,
        HttpContext httpContext,
        TournamentServerClient client,
        BridgeConfig config,
        RegistrationStore store,
        CancellationToken ct)
    {
        string serverUrl = config.ResolveServerUrl(server);
        IReadOnlyList<Registration> regs = store.FindAllByTournament(serverUrl, id);
        Registration? tokenSource = regs.FirstOrDefault(r => !string.IsNullOrEmpty(r.BotToken))
            ?? regs.FirstOrDefault(r => !string.IsNullOrEmpty(r.DirectorToken));

        string token = tokenSource?.BotToken ?? tokenSource?.DirectorToken ?? string.Empty;

        httpContext.Response.ContentType = "text/event-stream";
        httpContext.Response.Headers.CacheControl = "no-cache";
        httpContext.Response.Headers.Connection = "keep-alive";

        await foreach (TournamentEvent evt in client.StreamTournamentAsync(
            serverUrl, token, id, ct))
        {
            string data = JsonSerializer.Serialize(evt);
            string eventType = evt.Type;
            await httpContext.Response.WriteAsync($"event: {eventType}\ndata: {data}\n\n", ct);
            await httpContext.Response.Body.FlushAsync(ct);
        }
    }

    private static async Task<IResult> ListBots(
        Bots.BotsClient engineClient,
        CancellationToken ct)
    {
        ListBotsResponse response = await engineClient.ListBotsAsync(
            new ListBotsRequest(), cancellationToken: ct);

        var bots = response.Bots.Select(b => new
        {
            id = b.Id,
            name = b.Name,
            elo = b.Elo,
            description = b.Description,
        });

        return Results.Ok(new { bots });
    }

    private static IResult GetConfig(BridgeConfig config) =>
        Results.Ok(new { default_server_url = config.DefaultServerUrl });

    private static async Task<IResult> UpdateConfig(
        HttpRequest httpRequest,
        BridgeConfig config,
        CancellationToken ct)
    {
        using JsonDocument body = await JsonDocument.ParseAsync(httpRequest.Body, cancellationToken: ct);
        string url = body.RootElement.GetProperty("default_server_url").GetString()
            ?? throw new InvalidOperationException("default_server_url is required");
        config.SetDefaultServerUrl(url);
        return Results.Ok(new { default_server_url = config.DefaultServerUrl });
    }

    private static string GetUserId(ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new UnauthorizedAccessException("Missing user identity");
}
