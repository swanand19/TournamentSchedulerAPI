using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TournamentScheduler.Api.Data;
using TournamentScheduler.Api.Models;
using TournamentScheduler.Api.Models.Cricket;
using TournamentScheduler.Api.Services;

namespace TournamentScheduler.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TournamentsController : ControllerBase
{
    private readonly TournamentDbContext _db;
    private readonly IStatsService _stats;

    public TournamentsController(TournamentDbContext db, IStatsService stats)
    {
        _db = db;
        _stats = stats;
    }

    // GET api/tournaments/5/stats — every leaderboard for this tournament
    [HttpGet("{id}/stats")]
    public async Task<ActionResult<TournamentStats>> GetStats(int id)
    {
        var stats = await _stats.BuildAsync(id);
        if (stats == null) return NotFound("Tournament not found.");
        return Ok(stats);
    }

    // GET api/tournaments            — every tournament
    // GET api/tournaments?sport=Cricket — just that sport's, for the home page tabs
    [HttpGet]
    public async Task<ActionResult<object>> GetAll([FromQuery] Sport? sport)
    {
        var query = _db.Tournaments.AsQueryable();

        if (sport.HasValue)
            query = query.Where(t => t.Sport == sport.Value);

        var tournaments = await query
        .OrderByDescending(t => t.CreatedAt)
        .Select(t => new
        {
            t.Id,
            t.Name,
            t.Sport,
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

        var tournament = new Tournament { Name = name, Sport = request.Sport };
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

        // Schedules approved before fixtures carried team ids fall back to matching on the name.
        int? ResolveTeam(int? savedId, string name) =>
            savedId ?? teams.FirstOrDefault(t => t.Name == name)?.Id;

        var fixtures = activeSchedule.Groups
            .SelectMany(group => group.Fixtures.Select(fixture => (group, fixture)))
            .ToList();

        int created;
        if (tournament.Sport == Sport.Cricket)
        {
            var cricketMatches = fixtures.Select(x => new CricketMatch
            {
                TournamentId = id,
                SavedFixtureId = x.fixture.Id,
                GroupName = x.group.GroupName,
                MatchNumber = x.fixture.MatchNumber,
                HomeTeamId = ResolveTeam(x.fixture.HomeTeamId, x.fixture.Home),
                AwayTeamId = ResolveTeam(x.fixture.AwayTeamId, x.fixture.Away),
                HomeTeamName = x.fixture.Home,
                AwayTeamName = x.fixture.Away,
                Status = CricketMatchStatus.NotStarted
            }).ToList();

            _db.CricketMatches.AddRange(cricketMatches);
            created = cricketMatches.Count;
        }
        else
        {
            var matches = fixtures.Select(x => new Match
            {
                TournamentId = id,
                SavedFixtureId = x.fixture.Id,
                GroupName = x.group.GroupName,
                MatchNumber = x.fixture.MatchNumber,
                HomeTeamId = ResolveTeam(x.fixture.HomeTeamId, x.fixture.Home),
                AwayTeamId = ResolveTeam(x.fixture.AwayTeamId, x.fixture.Away),
                HomeTeamName = x.fixture.Home,
                AwayTeamName = x.fixture.Away,
                Status = MatchStatus.NotStarted
            }).ToList();

            _db.Matches.AddRange(matches);
            created = matches.Count;
        }

        tournament.IsStarted = true;
        tournament.StartedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new { matchesCreated = created });
    }

    // GET api/tournaments/5/matches
    //
    // Both sports answer here with the same card shape — id, teams, group, status, sport — so the
    // match list stays one component. Each sport then adds only what its own card draws: football
    // the period and penalty detail, cricket the innings score lines.
    [HttpGet("{id}/matches")]
    public async Task<ActionResult<object>> GetMatches(int id)
    {
        var tournament = await _db.Tournaments.FindAsync(id);
        if (tournament == null) return NotFound("Tournament not found.");

        if (tournament.Sport == Sport.Cricket)
        {
            var loaded = await _db.CricketMatches
                .Where(m => m.TournamentId == id)
                .Include(m => m.Innings)
                .OrderBy(m => m.GroupName).ThenBy(m => m.MatchNumber)
                .ToListAsync();

            // "148/6 (20.0)", or "300 & 150/4" once a side has batted twice. Null until they have
            // batted at all, which is what makes the card fall back to "VS".
            static string? ScoreLineFor(CricketMatch match, int? teamId)
            {
                if (teamId is null) return null;

                var lines = match.Innings
                    .Where(i => i.BattingTeamId == teamId)
                    .OrderBy(i => i.InningsNumber)
                    .Select(i => i.ScoreLine)
                    .ToList();

                return lines.Count == 0 ? null : string.Join(" & ", lines);
            }

            var cricketMatches = loaded.Select(m => new
            {
                m.Id,
                m.GroupName,
                m.MatchNumber,
                m.HomeTeamName,
                m.AwayTeamName,
                m.HomeTeamId,
                m.AwayTeamId,
                Sport = nameof(Sport.Cricket),
                Status = m.Status.ToString(),
                m.WinnerTeamId,
                m.ResultSummary,
                m.IsTie,
                m.IsDraw,
                m.IsNoResult,
                HomeLine = ScoreLineFor(m, m.HomeTeamId),
                AwayLine = ScoreLineFor(m, m.AwayTeamId)
            });

            return Ok(cricketMatches);
        }

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
                m.HomeTeamId,
                m.AwayTeamId,
                Sport = nameof(Sport.Football),
                Status = m.Status.ToString(),
                PeriodState = m.PeriodState.ToString(),
                m.CurrentHalf,
                m.HomeScore,
                m.AwayScore,
                m.PenaltyHomeScore,
                m.PenaltyAwayScore,
                m.PenaltyWinnerTeamId,
                m.ForfeitWinnerTeamId
            })
            .ToListAsync();

        return Ok(matches);
    }
}