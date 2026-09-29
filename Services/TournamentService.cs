using TournamentScheduler.Api.Data;
using TournamentScheduler.Api.Data.DataServices;
using TournamentScheduler.Api.Data.Queries;
using TournamentScheduler.Api.Models;
using TournamentScheduler.Api.Models.Cricket;

namespace TournamentScheduler.Api.Services;

public interface ITournamentService
{
    Task<ServiceResult<object>> GetAllAsync(Sport? sport);
    Task<ServiceResult<Tournament>> GetByIdAsync(int id);
    Task<ServiceResult<Tournament>> CreateAsync(CreateTournamentRequest request);
    Task<ServiceResult<bool>> DeleteAsync(int id);
    Task<ServiceResult<SavedSchedule>> GetActiveScheduleAsync(int id);
    Task<ServiceResult<object>> GetRecentSchedulesAsync(int id);
    Task<ServiceResult<object>> ActivateScheduleAsync(int tournamentId, int scheduleId);
    Task<ServiceResult<object>> StartAsync(int id);
    Task<ServiceResult<object>> GetMatchesAsync(int id);
}

/// <summary>Tournaments: creating them, their schedule versions, kick-off, and the match list.</summary>
public class TournamentService : ServiceBase, ITournamentService
{
    private readonly IDataService<Tournament> _tournaments;
    private readonly IDataService<SavedSchedule> _schedules;
    private readonly IDataService<Team> _teams;
    private readonly IDataService<Match> _matches;
    private readonly IDataService<CricketMatch> _cricketMatches;
    private readonly IUnitOfWork _unitOfWork;

    public TournamentService(
        IDataService<Tournament> tournaments,
        IDataService<SavedSchedule> schedules,
        IDataService<Team> teams,
        IDataService<Match> matches,
        IDataService<CricketMatch> cricketMatches,
        IUnitOfWork unitOfWork)
    {
        _tournaments = tournaments;
        _schedules = schedules;
        _teams = teams;
        _matches = matches;
        _cricketMatches = cricketMatches;
        _unitOfWork = unitOfWork;
    }

    public async Task<ServiceResult<object>> GetAllAsync(Sport? sport)
    {
        var tournaments = await _tournaments.QueryAsync(q =>
        {
            if (sport.HasValue)
                q = q.Where(t => t.Sport == sport.Value);

            return q
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
                });
        });

        return Ok(tournaments);
    }

    public async Task<ServiceResult<Tournament>> GetByIdAsync(int id)
    {
        var tournament = await _tournaments.GetByIdAsync(id);
        if (tournament == null) return NotFound();
        return Success(tournament);
    }

    public async Task<ServiceResult<Tournament>> CreateAsync(CreateTournamentRequest request)
    {
        var name = request.Name?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest("Tournament name is required.");

        var tournament = new Tournament { Name = name, Sport = request.Sport };
        _tournaments.Add(tournament);
        await _unitOfWork.SaveChangesAsync();
        return Success(tournament);
    }

    public async Task<ServiceResult<bool>> DeleteAsync(int id)
    {
        var tournament = await _tournaments.GetByIdAsync(id);
        if (tournament == null) return NotFound();

        _tournaments.Remove(tournament); // cascades to schedules
        await _unitOfWork.SaveChangesAsync();
        return Success(true);
    }

    /// <summary>The latest approved schedule, if any.</summary>
    public async Task<ServiceResult<SavedSchedule>> GetActiveScheduleAsync(int id)
    {
        var schedule = await _schedules.QueryFirstOrDefaultAsync(q =>
            ScheduleQueries.WithFixtures(q.Where(s => s.TournamentId == id && s.IsActive))
                .OrderByDescending(s => s.CreatedAt));

        if (schedule == null) return NotFound();
        return Success(schedule);
    }

    public async Task<ServiceResult<object>> GetRecentSchedulesAsync(int id)
    {
        var schedules = await _schedules.QueryAsync(q => q
            .Where(s => s.TournamentId == id)
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
            }));

        return Ok(schedules);
    }

    public async Task<ServiceResult<object>> ActivateScheduleAsync(int tournamentId, int scheduleId)
    {
        var tournament = await _tournaments.GetByIdAsync(tournamentId);
        if (tournament == null) return NotFound("Tournament not found.");

        if (tournament.IsStarted)
            return BadRequest("This tournament has already started — its schedule is locked and can no longer be changed.");

        var target = await _schedules.FirstOrDefaultAsync(s => s.Id == scheduleId && s.TournamentId == tournamentId);
        if (target == null) return NotFound("Schedule not found for this tournament.");

        var currentlyActive = await _schedules.ListAsync(s => s.TournamentId == tournamentId && s.IsActive);
        foreach (var s in currentlyActive)
            s.IsActive = false;

        target.IsActive = true;
        await _unitOfWork.SaveChangesAsync();

        return Ok(new { activatedScheduleId = target.Id });
    }

    /// <summary>Creates a match for every fixture of the active schedule and locks the schedule.</summary>
    public async Task<ServiceResult<object>> StartAsync(int id)
    {
        var tournament = await _tournaments.GetByIdAsync(id);
        if (tournament == null) return NotFound("Tournament not found.");

        if (tournament.IsStarted)
            return BadRequest("Tournament has already been started.");

        var activeSchedule = await _schedules.FirstOrDefaultAsync(
            s => s.TournamentId == id && s.IsActive, ScheduleQueries.WithFixtures);

        if (activeSchedule == null)
            return BadRequest("No active schedule found for this tournament.");

        var teams = await _teams.ListAsync(t => t.TournamentId == id);

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

            _cricketMatches.AddRange(cricketMatches);
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

            _matches.AddRange(matches);
            created = matches.Count;
        }

        tournament.IsStarted = true;
        tournament.StartedAt = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync();

        return Ok(new { matchesCreated = created });
    }

    /// <summary>
    /// Both sports answer with the same card shape — id, teams, group, status, sport — so the match
    /// list stays one component. Each sport then adds only what its own card draws: football the
    /// period and penalty detail, cricket the innings score lines.
    /// </summary>
    public async Task<ServiceResult<object>> GetMatchesAsync(int id)
    {
        var tournament = await _tournaments.GetByIdAsync(id);
        if (tournament == null) return NotFound("Tournament not found.");

        if (tournament.Sport == Sport.Cricket)
        {
            var loaded = await _cricketMatches.ListAsync(
                m => m.TournamentId == id,
                q => CricketMatchQueries.WithInnings(q).OrderBy(m => m.GroupName).ThenBy(m => m.MatchNumber));

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

        var matches = await _matches.QueryAsync(q => q
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
            }));

        return Ok(matches);
    }
}
