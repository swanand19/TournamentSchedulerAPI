using TournamentScheduler.Api.Auth;
using TournamentScheduler.Api.Data;
using TournamentScheduler.Api.Data.DataServices;
using TournamentScheduler.Api.Data.Queries;
using TournamentScheduler.Api.Models;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Models.Cricket;
using TournamentScheduler.Api.Services.Access;
using TournamentScheduler.Api.Services.Knockouts;

namespace TournamentScheduler.Api.Services;

public interface ITournamentService
{
    Task<ServiceResult<object>> GetAllAsync(Sport? sport);
    Task<ServiceResult<Tournament>> GetByIdAsync(int id);
    Task<ServiceResult<Tournament>> CreateAsync(CreateTournamentRequest request);
    Task<ServiceResult<Tournament>> UpdateAsync(int id, UpdateTournamentRequest request);
    Task<ServiceResult<Tournament>> CompleteAsync(int id);
    Task<ServiceResult<bool>> DeleteAsync(int id);
    Task<ServiceResult<SavedSchedule>> GetActiveScheduleAsync(int id);
    Task<ServiceResult<object>> GetRecentSchedulesAsync(int id);
    Task<ServiceResult<object>> ActivateScheduleAsync(int tournamentId, int scheduleId);
    Task<ServiceResult<object>> StartAsync(int id);
    Task<ServiceResult<object>> GetMatchesAsync(int id);
}

/// <summary>
/// Tournaments: the signed-in person's own list, creating them (the creator becomes their first
/// owner), their dates and status, schedule versions, kick-off, completion, and the match list.
/// Who may call which method is decided before it runs (<c>[TournamentAccess]</c>); what the
/// caller turned out to be here arrives as <see cref="ITournamentContext"/>.
/// </summary>
public class TournamentService : ServiceBase, ITournamentService
{
    private readonly IDataService<Tournament> _tournaments;
    private readonly IDataService<SavedSchedule> _schedules;
    private readonly IDataService<Team> _teams;
    private readonly IDataService<Match> _matches;
    private readonly IDataService<CricketMatch> _cricketMatches;
    private readonly IDataService<TournamentMember> _members;
    private readonly IDataService<Player> _players;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly ITournamentContext _context;
    private readonly TimeProvider _clock;
    private readonly IKnockoutService _knockouts;

    public TournamentService(
        IDataService<Tournament> tournaments,
        IDataService<SavedSchedule> schedules,
        IDataService<Team> teams,
        IDataService<Match> matches,
        IDataService<CricketMatch> cricketMatches,
        IDataService<TournamentMember> members,
        IDataService<Player> players,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        ITournamentContext context,
        TimeProvider clock,
        IKnockoutService knockouts)
    {
        _tournaments = tournaments;
        _schedules = schedules;
        _teams = teams;
        _matches = matches;
        _cricketMatches = cricketMatches;
        _members = members;
        _players = players;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _context = context;
        _clock = clock;
        _knockouts = knockouts;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    /// <summary>Only the caller's own tournaments: ones they created, own, score or play in.</summary>
    public async Task<ServiceResult<object>> GetAllAsync(Sport? sport)
    {
        var me = _currentUser.UserId;
        var memberships = await _members.QueryAsync(q => q
            .Where(m => m.UserId == me && !m.IsDisabled)
            .Select(m => new { m.TournamentId, m.MemberRole }), tracking: false);
        var roleIn = memberships.ToDictionary(m => m.TournamentId, m => m.MemberRole);
        var playsIn = (await _players.QueryAsync(q => q
            .Where(p => p.UserId == me)
            .Select(p => p.TournamentId), tracking: false)).ToHashSet();
        var ids = roleIn.Keys.Concat(playsIn).Distinct().ToList();

        var tournaments = await _tournaments.QueryAsync(q =>
        {
            q = q.Where(t => ids.Contains(t.Id) || t.CreatedByUserId == me);
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
                    t.Status,
                    t.StartDate,
                    t.EndDate,
                    t.CreatedByUserId,
                    HasSchedule = t.Schedules.Any(),
                    LatestScheduleId = t.Schedules
                        .OrderByDescending(s => s.CreatedAt)
                        .Select(s => (int?)s.Id)
                        .FirstOrDefault()
                });
        }, tracking: false);

        return Ok(tournaments.Select(t => new
        {
            t.Id,
            t.Name,
            t.Sport,
            t.CreatedAt,
            t.IsStarted,
            t.Status,
            t.StartDate,
            t.EndDate,
            t.HasSchedule,
            t.LatestScheduleId,
            MyRoles = RolesOf(t.CreatedByUserId == me, roleIn.TryGetValue(t.Id, out var role) ? role : null, playsIn.Contains(t.Id))
        }).ToList());
    }

    private static List<string> RolesOf(bool isCreator, TournamentMemberRole? role, bool plays)
    {
        var roles = new List<string>();
        if (isCreator) roles.Add("creator");
        if (isCreator || role == TournamentMemberRole.Owner) roles.Add("owner");
        else if (role == TournamentMemberRole.Scorer) roles.Add("scorer");
        if (plays) roles.Add("player");
        return roles;
    }

    public async Task<ServiceResult<Tournament>> GetByIdAsync(int id)
    {
        var tournament = await _tournaments.GetByIdAsync(id);
        if (tournament == null) return NotFound();
        tournament.Access = await AccessFor(tournament);
        return Success(tournament);
    }

    /// <summary>What the caller may do here, for the apps' buttons.</summary>
    private async Task<TournamentAccessDto> AccessFor(Tournament tournament)
    {
        var closed = tournament.IsClosed;
        var (total, over, inPlay) = tournament.Status == TournamentStatus.Live ? await MatchCounts(tournament) : (0, 0, 0);

        return new TournamentAccessDto
        {
            MyRoles = RolesOf(_context.IsCreator, _context.Role, _context.MyPlace != null),
            MyPlayerId = _context.MyPlace?.PlayerId,
            MyTeamId = _context.MyPlace?.TeamId,
            CanEdit = _context.IsOwner && !closed,
            CanScore = _context.Role != null && !closed,
            CanManageMembers = _context.IsOwner,
            CanDelete = _context.IsCreator,
            CanLeave = !_context.IsCreator,
            CanComplete = _context.IsOwner && tournament.Status == TournamentStatus.Live && inPlay == 0,
            AllMatchesPlayed = tournament.Status == TournamentStatus.Live && total > 0 && over == total
        };
    }

    /// <summary>The tournament's matches: how many, how many over, how many being played now.</summary>
    private async Task<(int Total, int Over, int InPlay)> MatchCounts(Tournament tournament)
    {
        if (tournament.Sport == Sport.Cricket)
        {
            var statuses = await _cricketMatches.QueryAsync(q => q.Where(m => m.TournamentId == tournament.Id).Select(m => m.Status), tracking: false);
            return (statuses.Count,
                statuses.Count(s => s is CricketMatchStatus.Completed or CricketMatchStatus.Abandoned),
                statuses.Count(s => s is CricketMatchStatus.InProgress or CricketMatchStatus.InningsBreak or CricketMatchStatus.SuperOver));
        }

        var football = await _matches.QueryAsync(q => q.Where(m => m.TournamentId == tournament.Id).Select(m => m.Status), tracking: false);
        return (football.Count,
            football.Count(s => s == MatchStatus.Completed),
            football.Count(s => s is MatchStatus.InProgress or MatchStatus.Paused or MatchStatus.PenaltyShootout));
    }

    public async Task<ServiceResult<Tournament>> CreateAsync(CreateTournamentRequest request)
    {
        var name = request.Name?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest("Tournament name is required.");

        // Dates usually come with the schedule; checked here only when sent.
        var now = Now;
        if ((request.StartDate != null || request.EndDate != null)
            && TournamentDates.Problem(request.StartDate, request.EndDate, Tournament.DefaultTimeZoneId, now) is { } problem)
            return BadRequest(problem);

        var me = _currentUser.UserId;
        var tournament = new Tournament
        {
            Name = name,
            Sport = request.Sport,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            CreatedByUserId = me,
            Status = TournamentStatus.Upcoming
        };
        _tournaments.Add(tournament);
        await _unitOfWork.SaveChangesAsync();

        // The creator is its first owner.
        var owner = new TournamentMember { TournamentId = tournament.Id, UserId = me, MemberRole = TournamentMemberRole.Owner };
        owner.Created(now, me);
        _members.Add(owner);
        await _unitOfWork.SaveChangesAsync();

        tournament.Access = new TournamentAccessDto
        {
            MyRoles = RolesOf(true, TournamentMemberRole.Owner, false),
            CanEdit = true, CanScore = true, CanManageMembers = true, CanDelete = true
        };
        return Success(tournament);
    }

    public async Task<ServiceResult<Tournament>> UpdateAsync(int id, UpdateTournamentRequest request)
    {
        var tournament = await _tournaments.GetByIdAsync(id);
        if (tournament == null) return NotFound("Tournament not found.");

        var name = request.Name?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest("Tournament name is required.");
        if (TournamentDates.Problem(request.StartDate, request.EndDate, tournament.TimeZoneId, Now) is { } problem)
            return BadRequest(problem);

        tournament.Name = name;
        tournament.StartDate = request.StartDate;
        tournament.EndDate = request.EndDate;
        await _unitOfWork.SaveChangesAsync();

        tournament.Access = await AccessFor(tournament);
        return Success(tournament);
    }

    /// <summary>An owner closing it: nothing can be started or changed afterwards. Refused while a match is being played.</summary>
    public async Task<ServiceResult<Tournament>> CompleteAsync(int id)
    {
        var tournament = await _tournaments.GetByIdAsync(id);
        if (tournament == null) return NotFound("Tournament not found.");

        if (tournament.Status == TournamentStatus.Upcoming)
            return Conflict("This tournament hasn't started yet, so it can't be completed.");

        var (_, _, inPlay) = await MatchCounts(tournament);
        if (inPlay > 0)
            return Conflict("Finish or abandon the match in progress first.");

        tournament.Status = TournamentStatus.Completed;
        tournament.CompletedAt = Now;
        tournament.CompletedByUserId = _currentUser.UserId;
        await _unitOfWork.SaveChangesAsync();

        tournament.Access = await AccessFor(tournament);
        return Success(tournament);
    }

    public async Task<ServiceResult<bool>> DeleteAsync(int id)
    {
        var tournament = await _tournaments.GetByIdAsync(id);
        if (tournament == null) return NotFound();

        _tournaments.Remove(tournament); // cascades to schedules, teams, matches and members
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

        // Approving a schedule sets them; a schedule approved before dates existed didn't.
        if (tournament.StartDate == null || tournament.EndDate == null)
            return BadRequest("Set the tournament's start and end dates before starting it (People & settings).");

        var activeSchedule = await _schedules.FirstOrDefaultAsync(
            s => s.TournamentId == id && s.IsActive, ScheduleQueries.WithFixtures);

        // A knockout-only tournament has no schedule: its knockout stage is the whole of it.
        var hasKnockouts = await _knockouts.AnyAsync(id);
        if (activeSchedule == null && !hasKnockouts)
            return BadRequest("No active schedule found for this tournament. Approve a schedule, or set up a knockout stage.");

        if (hasKnockouts)
        {
            var groups = activeSchedule?.Groups
                .Select(g => new KnockoutGroup(g.GroupName, g.Fixtures.SelectMany(f => new[] { f.Home, f.Away }).Distinct().Count()))
                .ToList() ?? [];
            if (await _knockouts.ProblemAtStartAsync(id, groups) is { } knockoutProblem)
                return BadRequest(knockoutProblem);
        }

        var teams = await _teams.ListAsync(t => t.TournamentId == id);

        // Schedules approved before fixtures carried team ids fall back to matching on the name.
        int? ResolveTeam(int? savedId, string name) =>
            savedId ?? teams.FirstOrDefault(t => t.Name == name)?.Id;

        var fixtures = (activeSchedule?.Groups ?? [])
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
        tournament.Status = TournamentStatus.Live;
        await _unitOfWork.SaveChangesAsync();

        // After the group matches exist, so the knockout matches are numbered after them.
        if (hasKnockouts) created += await _knockouts.CreateMatchesAsync(id);

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
                q => CricketMatchQueries.WithInnings(q).OrderBy(m => m.KnockoutFixtureId != null).ThenBy(m => m.GroupName).ThenBy(m => m.MatchNumber));

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
                Stage = m.KnockoutFixtureId != null ? "Knockout" : "Group",
                m.RoundLabel,
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
            .OrderBy(m => m.KnockoutFixtureId != null).ThenBy(m => m.GroupName).ThenBy(m => m.MatchNumber)
            .Select(m => new
            {
                m.Id,
                m.GroupName,
                Stage = m.KnockoutFixtureId != null ? "Knockout" : "Group",
                m.RoundLabel,
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
