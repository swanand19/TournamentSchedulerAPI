using TournamentScheduler.Api.Data;
using TournamentScheduler.Api.Http;
using TournamentScheduler.Api.Services;
using TournamentScheduler.Api.Services.Cricket;
using TournamentScheduler.Api.Services.Football;
using System.Text.Json.Serialization;

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
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});

var app = builder.Build();

// First, so a crash or an unmatched URL anywhere below still answers in the { status, data } envelope.
app.UseApiResponses();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowFrontend");

// On by default. The IIS site that serves phones over plain HTTP on the home network turns it off
// (Hosting__RedirectToHttps=false in its web.config), because a phone can't follow a redirect to
// an HTTPS address the laptop has no trusted certificate for.
if (app.Configuration.GetValue("Hosting:RedirectToHttps", true))
{
    app.UseHttpsRedirection();
}
app.UseAuthorization();
app.MapControllers();

app.Run();

// Lets the contract tests host this exact pipeline (WebApplicationFactory<Program>).
public partial class Program { }