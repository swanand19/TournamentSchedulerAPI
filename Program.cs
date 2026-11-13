using TournamentScheduler.Api.Auth;
using TournamentScheduler.Api.Data;
using TournamentScheduler.Api.Gateway;
using TournamentScheduler.Api.Http;
using TournamentScheduler.Api.Logging;
using TournamentScheduler.Api.Services;
using TournamentScheduler.Api.Services.Cricket;
using TournamentScheduler.Api.Services.Football;
using TournamentScheduler.Api.Services.Profiles;
using TournamentScheduler.Api.Services.Watch;
using TournamentScheduler.Api.Services.Knockouts;
using TournamentScheduler.Api.Services.Public;
using TournamentScheduler.Api.Models.Public;
using TournamentScheduler.Api.Realtime;
using TournamentScheduler.Api.Hosting;
using Azure.Monitor.OpenTelemetry.AspNetCore;

// `dotnet run -- gateway-keys new|show|ensure` manages the gateway's keys and exits.
if (GatewayKeyTool.TryRun(args, out var keyToolExitCode))
    return keyToolExitCode;

// `dotnet run -- users create-admin <email> [name]` makes an admin and exits.
if (UserAdminTool.TryRun(args, out var userToolExitCode))
    return userToolExitCode;

var builder = WebApplication.CreateBuilder(args);

// Every response goes out as { status, data } — see Http/ApiResponse.cs. Every action that touches a
// tournament is checked against the caller's role in it first — see Auth/TournamentAccess.
builder.Services.AddControllers(options =>
    {
        options.Filters.Add<ApiResponseFilter>();
        options.Filters.Add<TournamentScheduler.Api.Auth.TournamentAccess.TournamentAccessFilter>();
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
// Layers, top to bottom: controllers -> services -> IDataService<T> -> IRepository<T> -> DbContext.
// The last three are registered by AddDataLayer; only the Data folder ever touches the database.
builder.Services.AddDataLayer(builder.Configuration);

// Every app request arrives encrypted at POST /api/gateway — see Gateway/README.md.
builder.Services.AddGateway(builder.Configuration);

// MBMiddlewareLogs, MBActivityLogs and ErrorLogs, written in the background to the logs database —
// see Logging/README.md.
builder.Services.AddLogStore(builder.Configuration);

// Accounts: email one-time codes, sessions, the signed-in user — see Auth/README.md.
builder.Services.AddAccounts(builder.Configuration);

builder.Services.AddScoped<IHealthService, HealthService>();
builder.Services.AddScoped<ITournamentService, TournamentService>();
builder.Services.AddScoped<ITeamService, TeamService>();
builder.Services.AddScoped<IScheduleService, ScheduleService>();
builder.Services.AddScoped<IStatsService, StatsService>();
builder.Services.AddScoped<IFootballMatchService, FootballMatchService>();
builder.Services.AddScoped<ICricketScoringService, CricketScoringService>();
builder.Services.AddScoped<ICricketMatchQueryService, CricketMatchQueryService>();
builder.Services.AddScoped<ICricketStatsService, CricketStatsService>();
builder.Services.AddScoped<IProfileService, ProfileService>();
builder.Services.AddScoped<IWatchService, WatchService>();
builder.Services.AddScoped<IKnockoutService, KnockoutService>();

// Public live scores (6E): the owners' switch, following, and the anonymous public pages (limited per address).
builder.Services.Configure<PublicOptions>(builder.Configuration.GetSection(PublicOptions.Section));
builder.Services.AddMemoryCache();
builder.Services.AddScoped<ITournamentSharingService, TournamentSharingService>();
builder.Services.AddScoped<IPublicTournamentService, PublicTournamentService>();
builder.Services.AddPublicRateLimit(builder.Configuration);

// Live updates: a SignalR line that nudges the apps ("match 12 changed") so they fetch at once — see Realtime/LiveHub.cs.
builder.Services.AddLiveUpdates();

// Laptop (IIS) or Azure App Service: CORS list, forwarded headers, HSTS — see Hosting/HostingRegistration.cs.
builder.Services.AddHosting(builder.Configuration);

// Azure: requests, timings, dependencies and errors to Application Insights. Only when App Service supplies
// the connection string, so the laptop and the tests send nothing anywhere.
if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor();
    builder.Services.Configure<OpenTelemetry.Instrumentation.AspNetCore.AspNetCoreTraceInstrumentationOptions>(options =>
        options.Filter = context => !context.Request.Path.StartsWithSegments(HostingRegistration.LivePath));
}

// A deploy or restart waits this long for requests in flight and for the log writer to empty its queue.
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(25));

// A scoped service caught inside a singleton is a bug in every environment, not only in Development.
builder.Host.UseDefaultServiceProvider(options =>
{
    options.ValidateScopes = true;
    options.ValidateOnBuild = true;
});

var app = builder.Build();

// First: the caller's real address behind Azure's front end, and the health probe (answered before logging).
app.UseHostingEdge();

// Then each request gets the id every log row about it carries (X-Request-Id).
app.UseRequestCorrelation();

// Next, so a crash or an unmatched URL anywhere below still answers in the { status, data } envelope.
app.UseApiResponses();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Before the gateway, so its answers — and the browser's preflight for POST /api/gateway — carry CORS
// headers; then HSTS and the HTTPS redirect where they apply (off for the laptop's IIS site, whose phones
// use plain HTTP, and on Azure, where App Service redirects first).
app.UseHostingSecurity();

// The gateway decrypts and rewrites the request to the action's own URL, so it runs before the
// router; the rest run after it, once the action is known. The activity log goes first so refused
// calls are recorded too; enforcement before the session check, so a direct call that isn't allowed
// hears that rather than "sign in"; then who is calling, then whether their role may call this at all.
app.UseGateway();
// The live line's own door: only a one-minute ticket from LIVE_TICKET opens it (it can't come through the gateway).
app.UseLiveHubAuthentication();
app.UseRouting();
app.UseActivityLogging();
app.UseRateLimiter(); // only the public pages carry a policy (Http/PublicRateLimit.cs)
app.UseGatewayEnforcement();
app.UseSessionAuthentication();
app.UsePermissionAuthorization();
app.UseAuthorization();
app.MapControllers();
app.MapLiveHub();

app.Run();
return 0;

// Lets the contract tests host this exact pipeline (WebApplicationFactory<Program>).
public partial class Program { }