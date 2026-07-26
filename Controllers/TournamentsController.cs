using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TournamentScheduler.Api.Data;
using TournamentScheduler.Api.Models;

namespace TournamentScheduler.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TournamentsController : ControllerBase
{
    private readonly TournamentDbContext _db;

    public TournamentsController(TournamentDbContext db)
    {
        _db = db;
    }

    // GET api/tournaments
    [HttpGet]
    public async Task<ActionResult<object>> GetAll()
    {
        var tournaments = await _db.Tournaments
        .OrderByDescending(t => t.CreatedAt)
        .Select(t => new
        {
            t.Id,
            t.Name,
            t.CreatedAt,
            t.IsStarted,
            HasSchedule = t.Schedules.Any(),
            LatestScheduleId = t.Schedules
                .OrderByDescending(s => s.CreatedAt)
                .Select(s => (int?)s.Id)
                .FirstOrDefault()
        })
        .ToListAsync();

        return Ok(tournaments);
    }

    // GET api/tournaments/5
    [HttpGet("{id}")]
    public async Task<ActionResult<Tournament>> GetById(int id)
    {
        var tournament = await _db.Tournaments.FindAsync(id);
        if (tournament == null) return NotFound();
        return Ok(tournament);
    }

    // GET api/tournaments/5/schedule  (latest approved schedule, if any)
    [HttpGet("{id}/schedule")]
    public async Task<ActionResult<SavedSchedule>> GetLatestSchedule(int id)
    {
        var schedule = await _db.SavedSchedules
            .Where(s => s.TournamentId == id && s.IsActive)
            .Include(s => s.Groups)
            .ThenInclude(g => g.Fixtures)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync();

        if (schedule == null) return NotFound();
        return Ok(schedule);
    }

    // POST api/tournaments
    [HttpPost]
    public async Task<ActionResult<Tournament>> Create([FromBody] CreateTournamentRequest request)
    {
        var name = request.Name?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest("Tournament name is required.");

        var tournament = new Tournament { Name = name };
        _db.Tournaments.Add(tournament);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = tournament.Id }, tournament);
    }

    // DELETE api/tournaments/5
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var tournament = await _db.Tournaments.FindAsync(id);
        if (tournament == null) return NotFound();

        _db.Tournaments.Remove(tournament); // cascades to schedules
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("{id}/schedules")]
    public async Task<ActionResult<object>> GetAllSchedules(int id)
    {
        var schedules = await _db.SavedSchedules
            .Where(s => s.TournamentId == id)
            .Include(s => s.Groups)
            .ThenInclude(g => g.Fixtures)
            .OrderByDescending(s => s.CreatedAt)
            .Take(5)
            .Select(s => new
            {
                s.Id,
                s.CreatedAt,
                s.IsActive,
                s.MatchesPerTeam,
                s.TotalMatches,
                Groups = s.Groups.Select(g => new
                {
                    g.GroupName,
                    Fixtures = g.Fixtures.Select(f => new { f.Id, f.MatchNumber, f.Home, f.Away })
                })
            })
            .ToListAsync();

        return Ok(schedules);
    }

    // POST api/tournaments/5/schedules/12/activate
    [HttpPost("{tournamentId}/schedules/{scheduleId}/activate")]
    public async Task<ActionResult<object>> ActivateSchedule(int tournamentId, int scheduleId)
    {
        var tournament = await _db.Tournaments.FindAsync(tournamentId);
        if (tournament == null) return NotFound("Tournament not found.");

        if (tournament.IsStarted)
            return BadRequest("This tournament has already started — its schedule is locked and can no longer be changed.");

        var target = await _db.SavedSchedules
            .FirstOrDefaultAsync(s => s.Id == scheduleId && s.TournamentId == tournamentId);

        if (target == null) return NotFound("Schedule not found for this tournament.");

        var currentlyActive = await _db.SavedSchedules
            .Where(s => s.TournamentId == tournamentId && s.IsActive)
            .ToListAsync();

        foreach (var s in currentlyActive)
            s.IsActive = false;

        target.IsActive = true;
        await _db.SaveChangesAsync();

        return Ok(new { activatedScheduleId = target.Id });
    }

    // POST api/tournaments/5/start
    [HttpPost("{id}/start")]
    public async Task<ActionResult<object>> StartTournament(int id)
    {
        var tournament = await _db.Tournaments.FindAsync(id);
        if (tournament == null) return NotFound("Tournament not found.");

        if (tournament.IsStarted)
            return BadRequest("Tournament has already been started.");

        var activeSchedule = await _db.SavedSchedules
            .Where(s => s.TournamentId == id && s.IsActive)
            .Include(s => s.Groups)
            .ThenInclude(g => g.Fixtures)
            .FirstOrDefaultAsync();

        if (activeSchedule == null)
            return BadRequest("No active schedule found for this tournament.");

        var teams = await _db.Teams.Where(t => t.TournamentId == id).ToListAsync();

        var matches = new List<Match>();
        foreach (var group in activeSchedule.Groups)
        {
            foreach (var fixture in group.Fixtures)
            {
                var homeTeam = teams.FirstOrDefault(t => t.Name == fixture.Home);
                var awayTeam = teams.FirstOrDefault(t => t.Name == fixture.Away);

                matches.Add(new Match
                {
                    TournamentId = id,
                    SavedFixtureId = fixture.Id,
                    GroupName = group.GroupName,
                    MatchNumber = fixture.MatchNumber,
                    HomeTeamId = homeTeam?.Id,
                    AwayTeamId = awayTeam?.Id,
                    HomeTeamName = fixture.Home,
                    AwayTeamName = fixture.Away,
                    Status = MatchStatus.NotStarted
                });
            }
        }

        _db.Matches.AddRange(matches);
        tournament.IsStarted = true;
        tournament.StartedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new { matchesCreated = matches.Count });
    }

    // GET api/tournaments/5/matches
    [HttpGet("{id}/matches")]
    public async Task<ActionResult<object>> GetMatches(int id)
    {
        var matches = await _db.Matches
            .Where(m => m.TournamentId == id)
            .OrderBy(m => m.GroupName).ThenBy(m => m.MatchNumber)
            .Select(m => new
            {
                m.Id,
                m.GroupName,
                m.MatchNumber,
                m.HomeTeamName,
                m.AwayTeamName,
                Status = m.Status.ToString(),
                m.HomeScore,
                m.AwayScore,
                m.PenaltyHomeScore,
                m.PenaltyAwayScore,
                m.PenaltyWinnerTeamId,
                m.HomeTeamId,
                m.AwayTeamId,
                m.ForfeitWinnerTeamId
            })
            .ToListAsync();

        return Ok(matches);
    }
}