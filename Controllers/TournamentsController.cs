using Microsoft.AspNetCore.Mvc;
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
    public async Task<ActionResult<ApiResponse<TournamentStats>>> GetStats(int id)
    {
        var stats = await _stats.BuildAsync(id);
        if (stats == null) return NotFound("Tournament not found.");
        return Ok(stats);
    }

    // GET api/tournaments/5/cricket-stats — points table, NRR, leaderboards, MVP and records
    [HttpGet("{id}/cricket-stats")]
    public async Task<ActionResult<ApiResponse<CricketTournamentStats>>> GetCricketStats(int id)
    {
        var stats = await _cricketStats.BuildAsync(id);
        if (stats == null) return NotFound("Tournament not found.");
        return Ok(stats);
    }

    // GET api/tournaments            — every tournament
    // GET api/tournaments?sport=Cricket — just that sport's, for the home page tabs
    [HttpGet]
    public async Task<ActionResult<ApiResponse<object>>> GetAll([FromQuery] Sport? sport) =>
        Respond(await _tournaments.GetAllAsync(sport));

    // GET api/tournaments/5
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<Tournament>>> GetById(int id) =>
        Respond(await _tournaments.GetByIdAsync(id));

    // GET api/tournaments/5/schedule  (latest approved schedule, if any)
    [HttpGet("{id}/schedule")]
    public async Task<ActionResult<ApiResponse<SavedSchedule>>> GetLatestSchedule(int id) =>
        Respond(await _tournaments.GetActiveScheduleAsync(id));

    // POST api/tournaments
    [HttpPost]
    public async Task<ActionResult<ApiResponse<Tournament>>> Create([FromBody] CreateTournamentRequest request) =>
        Respond(await _tournaments.CreateAsync(request),
            tournament => CreatedAtAction(nameof(GetById), new { id = tournament.Id }, tournament));

    // DELETE api/tournaments/5
    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResponse<object>>> Delete(int id) =>
        Respond(await _tournaments.DeleteAsync(id), _ => Ok());

    [HttpGet("{id}/schedules")]
    public async Task<ActionResult<ApiResponse<object>>> GetAllSchedules(int id) =>
        Respond(await _tournaments.GetRecentSchedulesAsync(id));

    // POST api/tournaments/5/schedules/12/activate
    [HttpPost("{tournamentId}/schedules/{scheduleId}/activate")]
    public async Task<ActionResult<ApiResponse<object>>> ActivateSchedule(int tournamentId, int scheduleId) =>
        Respond(await _tournaments.ActivateScheduleAsync(tournamentId, scheduleId));

    // POST api/tournaments/5/start
    [HttpPost("{id}/start")]
    public async Task<ActionResult<ApiResponse<object>>> StartTournament(int id) =>
        Respond(await _tournaments.StartAsync(id));

    // GET api/tournaments/5/matches — both sports' match cards
    [HttpGet("{id}/matches")]
    public async Task<ActionResult<ApiResponse<object>>> GetMatches(int id) =>
        Respond(await _tournaments.GetMatchesAsync(id));
}
