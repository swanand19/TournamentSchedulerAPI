using Microsoft.AspNetCore.Mvc;
using TournamentScheduler.Api.Gateway;
using TournamentScheduler.Api.Http;
using TournamentScheduler.Api.Models;
using TournamentScheduler.Api.Models.Cricket;
using TournamentScheduler.Api.Services;
using TournamentScheduler.Api.Services.Cricket;

namespace TournamentScheduler.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TournamentsController : ApiController
{
    private readonly ITournamentService _tournaments;
    private readonly IStatsService _stats;
    private readonly ICricketStatsService _cricketStats;

    public TournamentsController(ITournamentService tournaments, IStatsService stats, ICricketStatsService cricketStats)
    {
        _tournaments = tournaments;
        _stats = stats;
        _cricketStats = cricketStats;
    }

    // GET api/tournaments/5/stats — every leaderboard for this tournament
    [HttpGet("{id}/stats")]
    [ServiceRequestId("TOURNAMENT_STATS")]
    public async Task<ActionResult<ApiResponse<TournamentStats>>> GetStats(int id)
    {
        var stats = await _stats.BuildAsync(id);
        if (stats == null) return NotFound("Tournament not found.");
        return Ok(stats);
    }

    // GET api/tournaments/5/cricket-stats — points table, NRR, leaderboards, MVP and records
    [HttpGet("{id}/cricket-stats")]
    [ServiceRequestId("TOURNAMENT_CRICKET_STATS")]
    public async Task<ActionResult<ApiResponse<CricketTournamentStats>>> GetCricketStats(int id)
    {
        var stats = await _cricketStats.BuildAsync(id);
        if (stats == null) return NotFound("Tournament not found.");
        return Ok(stats);
    }

    // GET api/tournaments            — every tournament
    // GET api/tournaments?sport=Cricket — just that sport's, for the home page tabs
    [HttpGet]
    [ServiceRequestId("TOURNAMENT_LIST")]
    public async Task<ActionResult<ApiResponse<object>>> GetAll([FromQuery] Sport? sport) =>
        Respond(await _tournaments.GetAllAsync(sport));

    // GET api/tournaments/5
    [HttpGet("{id}")]
    [ServiceRequestId("TOURNAMENT_GET")]
    public async Task<ActionResult<ApiResponse<Tournament>>> GetById(int id) =>
        Respond(await _tournaments.GetByIdAsync(id));

    // GET api/tournaments/5/schedule  (latest approved schedule, if any)
    [HttpGet("{id}/schedule")]
    [ServiceRequestId("SCHEDULE_ACTIVE")]
    public async Task<ActionResult<ApiResponse<SavedSchedule>>> GetLatestSchedule(int id) =>
        Respond(await _tournaments.GetActiveScheduleAsync(id));

    // POST api/tournaments
    [HttpPost]
    [ServiceRequestId("TOURNAMENT_CREATE")]
    public async Task<ActionResult<ApiResponse<Tournament>>> Create([FromBody] CreateTournamentRequest request) =>
        Respond(await _tournaments.CreateAsync(request),
            tournament => CreatedAtAction(nameof(GetById), new { id = tournament.Id }, tournament));

    // DELETE api/tournaments/5
    [HttpDelete("{id}")]
    [ServiceRequestId("TOURNAMENT_DELETE")]
    public async Task<ActionResult<ApiResponse<object>>> Delete(int id) =>
        Respond(await _tournaments.DeleteAsync(id), _ => Ok());

    [HttpGet("{id}/schedules")]
    [ServiceRequestId("SCHEDULE_HISTORY")]
    public async Task<ActionResult<ApiResponse<object>>> GetAllSchedules(int id) =>
        Respond(await _tournaments.GetRecentSchedulesAsync(id));

    // POST api/tournaments/5/schedules/12/activate
    [HttpPost("{id}/schedules/{scheduleId}/activate")]
    [ServiceRequestId("SCHEDULE_ACTIVATE")]
    public async Task<ActionResult<ApiResponse<object>>> ActivateSchedule(int id, int scheduleId) =>
        Respond(await _tournaments.ActivateScheduleAsync(id, scheduleId));

    // POST api/tournaments/5/start
    [HttpPost("{id}/start")]
    [ServiceRequestId("TOURNAMENT_START")]
    public async Task<ActionResult<ApiResponse<object>>> StartTournament(int id) =>
        Respond(await _tournaments.StartAsync(id));

    // GET api/tournaments/5/matches — both sports' match cards
    [HttpGet("{id}/matches")]
    [ServiceRequestId("TOURNAMENT_MATCHES")]
    public async Task<ActionResult<ApiResponse<object>>> GetMatches(int id) =>
        Respond(await _tournaments.GetMatchesAsync(id));
}
