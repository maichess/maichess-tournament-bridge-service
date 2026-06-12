using System.Text;
using Grpc.Net.Client;
using Maichess.Engine.V1;
using Maichess.MatchManager.V1;
using MaichessTournamentBridgeService.Clients;
using MaichessTournamentBridgeService.Kafka;
using MaichessTournamentBridgeService.Providers;
using MaichessTournamentBridgeService.Providers.Lichess;
using MaichessTournamentBridgeService.Rest;
using MaichessTournamentBridgeService.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

DotNetEnv.Env.Load();
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// gRPC clients
string matchManagerUrl = builder.Configuration["Services:MatchManagerService"]
    ?? throw new InvalidOperationException("Services:MatchManagerService is not configured");
string engineUrl = builder.Configuration["Services:EngineService"]
    ?? throw new InvalidOperationException("Services:EngineService is not configured");

builder.Services.AddSingleton(
    new Matches.MatchesClient(GrpcChannel.ForAddress(matchManagerUrl)));
builder.Services.AddSingleton(
    new Bots.BotsClient(GrpcChannel.ForAddress(engineUrl)));

// HTTP client for tournament server
builder.Services.AddHttpClient<TournamentServerClient>();

// Lichess provider (engine-drives/we-mirror, single game per registration). The bot
// OAuth token is per-game, so the typed HttpClient is a stateless singleton.
string lichessApiUrl = builder.Configuration["Lichess:ApiUrl"]
    ?? Environment.GetEnvironmentVariable("LICHESS_API_URL")
    ?? "https://lichess.org";
builder.Services.AddHttpClient(
    LichessProvider.HttpClientName, c => c.BaseAddress = new Uri(lichessApiUrl));
builder.Services.AddSingleton(sp => new LichessProvider(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient(LichessProvider.HttpClientName)));
builder.Services.AddSingleton<IExternalProvider>(sp => sp.GetRequiredService<LichessProvider>());
builder.Services.AddSingleton<ILichessChallenger>(sp => sp.GetRequiredService<LichessProvider>());
builder.Services.AddSingleton<IExternalMatchMirror, MatchManagerMatchMirror>();
builder.Services.AddSingleton<IBotCatalog, EngineBotCatalog>();
builder.Services.AddSingleton<LichessGameBridge>();
builder.Services.AddSingleton<ILichessBridgeLauncher>(sp => sp.GetRequiredService<LichessGameBridge>());
builder.Services.AddSingleton<LichessRegistrationService>();

// Application services
string defaultServerUrl = builder.Configuration["TournamentServer:DefaultUrl"]
    ?? "http://tournament-server:8080";
builder.Services.AddSingleton(new BridgeConfig(defaultServerUrl));
builder.Services.AddSingleton<RegistrationStore>();
builder.Services.AddSingleton<TournamentOrchestrator>();

// Engine bot moves over Kafka (Kafka task 09 retired the synchronous Engine.GetBestMove
// gRPC call): requests go to engine.commands.v1 and replies arrive on engine.events.v1,
// correlated by request_id through the shared PendingBotMoves registry. ListBots stays on
// the Bots gRPC client above.
builder.Services.AddSingleton<PendingBotMoves>();
builder.Services.AddSingleton<IEngineMoveSource, KafkaEngineMoveSource>();
builder.Services.AddHostedService<EngineEventConsumer>();

// JWT authentication
string jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Jwt:Key is not configured");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (context.Request.Cookies.TryGetValue("access_token", out string? token))
                {
                    context.Token = token;
                }

                return Task.CompletedTask;
            },
        };
    });

builder.Services.AddAuthorization();

string otlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")
    ?? "http://otel-collector:4317";

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("tournament-bridge-service"))
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation()
        .AddGrpcClientInstrumentation()
        .AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint)));

WebApplication app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapTournamentEndpoints();
app.MapExternalGameEndpoints();

app.Run();
