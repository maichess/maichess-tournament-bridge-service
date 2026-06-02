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
        group.MapGet("/tournaments/{id}/stream", StreamTournament);
        group.MapGet("/bots", ListBots);
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
            formFields[prop.Name] = prop.Value.ValueKind == JsonValueKind.Number
                ? prop.Value.GetRawText()
                : prop.Value.GetString() ?? prop.Value.GetRawText();
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
            Registration? registration = store.FindByTournament(serverUrl, id);

            return Results.Ok(new
            {
                tournament,
                registration = registration is null
                    ? null
                    : new
                    {
                        registration_id = registration.Id,
                        maichess_bot_id = registration.MaichessBotId,
                        status = registration.Status,
                    },
                game_mappings = registration?.GameMappings.Select(m => new
                {
                    tournament_game_id = m.TournamentGameId,
                    match_db_id = m.MatchDbMatchId,
                }) ?? [],
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
        Registration? reg = store.FindByTournament(serverUrl, id);
        if (reg is null)
        {
            return Results.NotFound();
        }

        try
        {
            await client.DeleteTournamentAsync(serverUrl, reg.DirectorToken, id, ct);
            reg.Status = "terminated";
            store.Save(reg);
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
        Registration? reg = store.FindByTournament(serverUrl, id);
        if (reg is null)
        {
            return Results.NotFound();
        }

        try
        {
            Tournament tournament = await client.StartTournamentAsync(
                serverUrl, reg.DirectorToken, id, ct);
            reg.Status = "active";
            store.Save(reg);
            orchestrator.StartDriving(reg);
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
        CancellationToken ct)
    {
        string serverUrl = config.ResolveServerUrl(server);
        Registration? reg = store.FindByTournament(serverUrl, id);
        if (reg is null)
        {
            return Results.NotFound();
        }

        JsonDocument body = await JsonDocument.ParseAsync(httpRequest.Body, cancellationToken: ct);
        string botId = body.RootElement.GetProperty("bot_id").GetString()
            ?? throw new InvalidOperationException("bot_id is required");

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

        reg.MaichessBotId = botId;
        reg.BotToken = botIdentity.Token;
        reg.Status = "registered";
        store.Save(reg);

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
        TournamentServerClient client,
        BridgeConfig config,
        RegistrationStore store,
        CancellationToken ct)
    {
        string serverUrl = config.ResolveServerUrl(server);
        Registration? reg = store.FindByTournament(serverUrl, id);
        if (reg is null)
        {
            return Results.NotFound();
        }

        try
        {
            await client.WithdrawFromTournamentAsync(serverUrl, reg.BotToken, id, ct);
            reg.Status = "withdrawn";
            reg.MaichessBotId = string.Empty;
            reg.BotToken = string.Empty;
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
        RoundPairingsResponse pairings = await client.GetRoundPairingsAsync(
            serverUrl, id, round, ct);

        Registration? reg = store.FindByTournament(serverUrl, id);
        List<GameMapping> mappings = reg?.GameMappings ?? [];

        var enriched = pairings.Pairings.Select(p =>
        {
            string? matchDbId = mappings.FirstOrDefault(m => m.TournamentGameId == p.GameId)
                ?.MatchDbMatchId;
            return new
            {
                white = p.White,
                black = p.Black,
                gameId = p.GameId,
                match_db_id = matchDbId,
                winner = p.Winner,
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
        Registration? reg = store.FindByTournament(serverUrl, id);

        string token = reg?.BotToken ?? reg?.DirectorToken ?? string.Empty;

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

    private static IResult UpdateConfig(
        HttpRequest httpRequest,
        BridgeConfig config)
    {
        using var body = JsonDocument.Parse(httpRequest.Body);
        string url = body.RootElement.GetProperty("default_server_url").GetString()
            ?? throw new InvalidOperationException("default_server_url is required");
        config.SetDefaultServerUrl(url);
        return Results.Ok(new { default_server_url = config.DefaultServerUrl });
    }

    private static string GetUserId(ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new UnauthorizedAccessException("Missing user identity");
}
