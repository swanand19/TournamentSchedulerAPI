using TournamentScheduler.Api.Data;
using TournamentScheduler.Api.Gateway;
using TournamentScheduler.Api.Http;
using TournamentScheduler.Api.Logging;
using TournamentScheduler.Api.Services;
using TournamentScheduler.Api.Services.Cricket;
using TournamentScheduler.Api.Services.Football;
using System.Text.Json.Serialization;

// `dotnet run -- gateway-keys new|show|ensure` manages the gateway's keys and exits.
if (GatewayKeyTool.TryRun(args, out var keyToolExitCode))
    return keyToolExitCode;

var builder = WebApplication.CreateBuilder(args);

// Every response goes out as { status, data } — see Http/ApiResponse.cs.
builder.Services.AddControllers(options => options.Filters.Add<ApiResponseFilter>())
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

builder.Services.AddScoped<IHealthService, HealthService>();
builder.Services.AddScoped<ITournamentService, TournamentService>();
builder.Services.AddScoped<ITeamService, TeamService>();
builder.Services.AddScoped<IScheduleService, ScheduleService>();
builder.Services.AddScoped<IStatsService, StatsService>();
builder.Services.AddScoped<IFootballMatchService, FootballMatchService>();
builder.Services.AddScoped<ICricketScoringService, CricketScoringService>();
builder.Services.AddScoped<ICricketMatchQueryService, CricketMatchQueryService>();
builder.Services.AddScoped<ICricketStatsService, CricketStatsService>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader().WithExposedHeaders(GatewayMiddleware.RequestIdHeader);
    });
});

var app = builder.Build();

// Before everything: each request gets the id every log row about it carries (X-Request-Id).
app.UseRequestCorrelation();

// Next, so a crash or an unmatched URL anywhere below still answers in the { status, data } envelope.
app.UseApiResponses();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Before the gateway, so its answers — and the browser's preflight for POST /api/gateway — carry CORS headers.
app.UseCors("AllowFrontend");

// On by default. The IIS site that serves phones over plain HTTP on the home network turns it off
// (Hosting__RedirectToHttps=false in its web.config), because a phone can't follow a redirect to
// an HTTPS address the laptop has no trusted certificate for.
if (app.Configuration.GetValue("Hosting:RedirectToHttps", true))
{
    app.UseHttpsRedirection();
}

// The gateway decrypts and rewrites the request to the action's own URL, so it runs before the
// router; the activity log and enforcement run after it, once the action is known. The activity log
// goes first so a refused direct call is recorded too.
app.UseGateway();
app.UseRouting();
app.UseActivityLogging();
app.UseGatewayEnforcement();
app.UseAuthorization();
app.MapControllers();

app.Run();
return 0;

// Lets the contract tests host this exact pipeline (WebApplicationFactory<Program>).
public partial class Program { }