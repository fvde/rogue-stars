using Google.Cloud.Firestore;
using Microsoft.Extensions.Options;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Server.Arena;
using RogueStars.Server.Auth;
using RogueStars.Server.Commands;
using RogueStars.Server.Guilds;
using RogueStars.Server.Infrastructure;
using RogueStars.Server.Invasions;
using RogueStars.Server.Jobs;
using RogueStars.Server.Payments;
using RogueStars.Server.Storage;

var builder = WebApplication.CreateBuilder(args);

// Cloud Run injects PORT; default 8080.
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port)) builder.WebHost.UseUrls("http://0.0.0.0:" + port);

builder.Services.AddOptions<ServerOptions>().Bind(builder.Configuration.GetSection(ServerOptions.Section));
var options = builder.Configuration.GetSection(ServerOptions.Section).Get<ServerOptions>() ?? new ServerOptions();

if (options.Auth.Mode == "dev" && builder.Environment.IsProduction())
    throw new InvalidOperationException("Dev auth must never run in Production (RogueStars__Auth__Mode=dev).");
if (options.Jobs.Mode == "secret" && builder.Environment.IsProduction())
    throw new InvalidOperationException("Shared-secret job auth must not run in Production; use oidc.");

// Logging: structured JSON for Cloud Logging outside Development.
if (!builder.Environment.IsDevelopment())
{
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole(o => o.FormatterName = CloudLoggingFormatter.FormatterName)
        .AddConsoleFormatter<CloudLoggingFormatter, Microsoft.Extensions.Logging.Console.ConsoleFormatterOptions>();
}

builder.Services.ConfigureHttpJsonOptions(o => StateJson.ConfigureWire(o.SerializerOptions));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<Clock>();
builder.Services.AddHttpClient();

// Rules engine: same handlers as the client; JSON round-trip cloning makes commands atomic.
builder.Services.AddSingleton(_ => new GameEngine(ContentDb.Default) { Cloner = StateJson.Clone });

// Storage
if (options.Store.Mode == "memory")
{
    builder.Services.AddSingleton<IPlayerStore, InMemoryPlayerStore>();
    builder.Services.AddSingleton<IDocStore, InMemoryDocStore>();
}
else
{
    builder.Services.AddSingleton(_ => new FirestoreDbBuilder
    {
        ProjectId = options.Store.FirestoreProjectId ?? throw new InvalidOperationException("RogueStars:Store:FirestoreProjectId is required"),
        DatabaseId = string.IsNullOrEmpty(options.Store.FirestoreDatabase) ? "(default)" : options.Store.FirestoreDatabase,
    }.Build());
    builder.Services.AddSingleton<IPlayerStore>(sp => new FirestorePlayerStore(sp.GetRequiredService<FirestoreDb>(), options.Store));
    builder.Services.AddSingleton<IDocStore>(sp => new FirestoreDocStore(sp.GetRequiredService<FirestoreDb>(), options.Store));
}

// Auth
builder.Services.AddSingleton<DevAuthVerifier>();
builder.Services.AddKeyedSingleton<ISigningKeySource>("firebase", (sp, _) => new GoogleCertificateSource(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("google-certs"), GoogleCertificateSource.FirebaseCertsUrl, sp.GetRequiredService<TimeProvider>()));
builder.Services.AddKeyedSingleton<ISigningKeySource>("google-oidc", (sp, _) => new GoogleCertificateSource(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("google-certs"), GoogleCertificateSource.GoogleOidcCertsUrl, sp.GetRequiredService<TimeProvider>()));
if (options.Auth.Mode == "dev")
    builder.Services.AddSingleton<IAuthVerifier>(sp => sp.GetRequiredService<DevAuthVerifier>());
else
    builder.Services.AddSingleton<IAuthVerifier>(sp => new FirebaseAuthVerifier(
        sp.GetRequiredKeyedService<ISigningKeySource>("firebase"), sp.GetRequiredService<TimeProvider>(), options.Auth.FirebaseProjectId ?? ""));
builder.Services.AddSingleton<PlayerAuthFilter>();
builder.Services.AddSingleton<JobAuthFilter>();

// Payments
if (options.Iap.Mode == "fake") builder.Services.AddSingleton<IReceiptValidator, FakeReceiptValidator>();
else builder.Services.AddSingleton<IReceiptValidator>(sp => new GooglePlayReceiptValidator(
    sp.GetRequiredService<IOptions<ServerOptions>>(), sp.GetRequiredService<ILogger<GooglePlayReceiptValidator>>()));
if (options.Ads.Mode == "fake") builder.Services.AddSingleton<IAdVerifier, FakeAdVerifier>();
else builder.Services.AddSingleton<IAdVerifier, AppLovinAdVerifier>();
builder.Services.AddSingleton<AdCreditService>();

// Game services
builder.Services.AddSingleton<CommandCatalog>();
builder.Services.AddSingleton<CommandService>();
builder.Services.AddSingleton<ServerHooks>();
builder.Services.AddSingleton<PlayerService>();
// TODO(lead): replace with a BattleSimulator-backed resolver after merge.
builder.Services.AddSingleton<IBattleResolver, SimulatorBattleResolver>();
builder.Services.AddSingleton<ArenaService>();
builder.Services.AddSingleton<IChatFilter, WordListChatFilter>();
builder.Services.AddSingleton<GuildService>();
builder.Services.AddSingleton<InvasionService>();

var app = builder.Build();

app.UseMiddleware<ApiExceptionMiddleware>();

app.MapGet("/healthz", (IOptions<ServerOptions> o) => Results.Ok(new
{
    status = "ok",
    version = typeof(Program).Assembly.GetName().Version?.ToString(),
    auth = o.Value.Auth.Mode,
    store = o.Value.Store.Mode,
}));

var v1 = app.MapGroup("/v1").AddEndpointFilter<PlayerAuthFilter>();
PlayerEndpoints.Map(v1, app, options);
PaymentEndpoints.Map(v1, app);
FeatureEndpoints.MapArena(v1);
FeatureEndpoints.MapGuilds(v1);
FeatureEndpoints.MapInvasions(v1);
FeatureEndpoints.MapInternal(app);

app.Logger.LogInformation("Rogue Stars server starting (auth={Auth}, store={Store}, env={Env})",
    options.Auth.Mode, options.Store.Mode, app.Environment.EnvironmentName);
app.Run();

public partial class Program;
