using TournamentScheduler.Api.Auth;
using TournamentScheduler.Api.Data;
using TournamentScheduler.Api.Data.DataServices;
using TournamentScheduler.Api.Data.Queries;
using TournamentScheduler.Api.Models;
using TournamentScheduler.Api.Models.Cricket;
using TournamentScheduler.Api.Models.Knockouts;
using TournamentScheduler.Api.Services.Access;
using TournamentScheduler.Api.Services.Cricket;

namespace TournamentScheduler.Api.Services.Knockouts;

public interface IKnockoutService
{
    Task<ServiceResult<KnockoutStageDto>> GetAsync(int tournamentId);
    Task<ServiceResult<List<KnockoutPresetDto>>> PresetsAsync(int tournamentId);
    Task<ServiceResult<KnockoutStageDto>> ApplyAsync(int tournamentId, KnockoutApplyRequest request);
    Task<ServiceResult<KnockoutStageDto>> ClearAsync(int tournamentId);
    Task<ServiceResult<KnockoutStageDto>> AddAsync(int tournamentId, KnockoutFixtureRequest request);
    Task<ServiceResult<KnockoutStageDto>> UpdateAsync(int tournamentId, int fixtureId, KnockoutFixtureRequest request);
    Task<ServiceResult<KnockoutStageDto>> DeleteAsync(int tournamentId, int fixtureId);
    Task<ServiceResult<KnockoutStageDto>> SetTeamAsync(int tournamentId, int fixtureId, KnockoutTeamRequest request);

    /// <summary>Whether the tournament has a knockout stage (a knockout-only tournament may start without a schedule).</summary>
    Task<bool> AnyAsync(int tournamentId);

    /// <summary>Why the knockout stage can't start with these groups (one it names is gone or too small); null if it can.</summary>
    Task<string?> ProblemAtStartAsync(int tournamentId, IReadOnlyList<KnockoutGroup> groups);

    /// <summary>At the tournament's start, after its group matches are saved: a match for every knockout fixture. Saves.</summary>
    Task<int> CreateMatchesAsync(int tournamentId);

    /// <summary>After any scoring action: if it ended a match, fill in the knockout sides that are now known. Saves.</summary>
    Task AfterScoringAsync();
}

/// <summary>
/// The knockout stage: presets, one match at a time, the owner's manual choice, and filling sides in
/// as groups and matches finish. Sides are only ever filled or changed on matches that haven't
/// started; a match in play or over keeps the teams it was played by.
/// </summary>
public class KnockoutService : ServiceBase, IKnockoutService
{
    private readonly IDataService<Tournament> _tournaments;
    private readonly IDataService<KnockoutFixture> _fixtures;
    private readonly IDataService<Match> _matches;
    private readonly IDataService<CricketMatch> _cricketMatches;
    private readonly IDataService<Team> _teams;
    private readonly IDataService<SavedSchedule> _schedules;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly ITournamentContext _context;
    private readonly TimeProvider _clock;

    public KnockoutService(
        IDataService<Tournament> tournaments,
        IDataService<KnockoutFixture> fixtures,
        IDataService<Match> matches,
        IDataService<CricketMatch> cricketMatches,
        IDataService<Team> teams,
        IDataService<SavedSchedule> schedules,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        ITournamentContext context,
        TimeProvider clock)
    {
        _tournaments = tournaments;
        _fixtures = fixtures;
        _matches = matches;
        _cricketMatches = cricketMatches;
        _teams = teams;
        _schedules = schedules;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _context = context;
        _clock = clock;
    }

    // -----------------------------------------------------------------
    // The tournament as the knockout stage sees it
    // -----------------------------------------------------------------

    /// <summary>A match with what the knockout stage needs to know about it, whichever the sport.</summary>
    private sealed record Played(ITournamentMatch Match, bool Changeable, bool Over, int? Winner, string Status, string? Result);

    private sealed class Stage
    {
        public required Tournament Tournament;
        public required List<KnockoutFixture> Fixtures;
        public required List<Played> Matches;
        public required Dictionary<int, string> Teams;
        public required List<KnockoutGroup> Groups;
        /// <summary>Finished groups' teams in finishing order.</summary>
        public required Dictionary<string, List<int>> Finishes;

        public Played? MatchOf(KnockoutFixture f) => Matches.FirstOrDefault(m => m.Match.KnockoutFixtureId == f.Id);
        public KnockoutFixture? ByOrder(int order) => Fixtures.FirstOrDefault(f => f.Order == order);
        public string? LabelOf(int order) => ByOrder(order)?.Label;
        public string? TeamName(int id) => Teams.GetValueOrDefault(id);
        public string Describe(KnockoutSlot slot) => KnockoutBracket.Describe(slot, TeamName, LabelOf);

        /// <summary>The team a slot works out to now, or null while it isn't known.</summary>
        public int? TeamFor(KnockoutSlot slot)
        {
            if (slot.OverrideTeamId is int chosen) return chosen;
            switch (slot.Kind)
            {
                case KnockoutSlotKind.Team:
                    return slot.TeamId;
                case KnockoutSlotKind.GroupPlace:
                    return slot.GroupName != null && slot.Place is int place && Finishes.TryGetValue(slot.GroupName, out var order)
                           && place >= 1 && place <= order.Count ? order[place - 1] : null;
                case KnockoutSlotKind.WinnerOf:
                case KnockoutSlotKind.LoserOf:
                    if (slot.FromOrder is not int from || ByOrder(from) is not { } earlier || MatchOf(earlier) is not { Over: true } played)
                        return null;
                    return slot.Kind == KnockoutSlotKind.WinnerOf
                        ? played.Winner
                        : KnockoutResults.LoserOf(played.Winner, played.Match.HomeTeamId, played.Match.AwayTeamId);
                default:
                    return null;
            }
        }

        public bool AnyStarted => Fixtures.Any(f => MatchOf(f) is { Changeable: false });
        public int NextOrder => Fixtures.Count == 0 ? 1 : Fixtures.Max(f => f.Order) + 1;
    }

    private async Task<Stage?> LoadAsync(int tournamentId)
    {
        var tournament = await _tournaments.GetByIdAsync(tournamentId);
        if (tournament == null) return null;

        var fixtures = await _fixtures.ListAsync(f => f.TournamentId == tournamentId, q => q.OrderBy(f => f.Order));
        var teams = (await _teams.QueryAsync(q => q.Where(t => t.TournamentId == tournamentId).Select(t => new { t.Id, t.Name }), tracking: false))
            .ToDictionary(t => t.Id, t => t.Name);

        List<Played> played;
        var finishes = new Dictionary<string, List<int>>();
        List<KnockoutGroup> groups;

        if (tournament.Sport == Sport.Cricket)
        {
            var matches = await _cricketMatches.ListAsync(m => m.TournamentId == tournamentId, CricketMatchQueries.ForKnockouts);
            played = matches.Select(m => new Played(m,
                m.Status == CricketMatchStatus.NotStarted && m.TossWinnerTeamId == null,
                m.IsOver, KnockoutResults.WinnerOf(m), m.Status.ToString(), KnockoutResults.ResultLine(m))).ToList();
            groups = GroupsOf(matches.Where(m => !m.IsKnockout).Cast<ITournamentMatch>().ToList());
            foreach (var group in matches.Where(m => !m.IsKnockout && m.GroupName != "").GroupBy(m => m.GroupName))
                if (group.All(m => m.IsOver))
                    finishes[group.Key] = CricketStatsService.RankGroup(group.ToList());
        }
        else
        {
            var matches = await _matches.ListAsync(m => m.TournamentId == tournamentId);
            played = matches.Select(m => new Played(m,
                m.Status == MatchStatus.NotStarted,
                m.Status == MatchStatus.Completed, KnockoutResults.WinnerOf(m), m.Status.ToString(), KnockoutResults.ResultLine(m))).ToList();
            groups = GroupsOf(matches.Where(m => !m.IsKnockout).Cast<ITournamentMatch>().ToList());
            foreach (var group in matches.Where(m => !m.IsKnockout && m.GroupName != "").GroupBy(m => m.GroupName))
                if (group.All(m => m.Status == MatchStatus.Completed))
                    finishes[group.Key] = StatsService.RankGroup(group.ToList());
        }

        // Before the start there are no group matches yet: the groups are the active schedule's.
        if (!tournament.IsStarted)
            groups = await ScheduledGroupsAsync(tournamentId);

        return new Stage
        {
            Tournament = tournament,
            Fixtures = fixtures,
            Matches = played,
            Teams = teams,
            Groups = groups,
            Finishes = finishes
        };
    }

    private static List<KnockoutGroup> GroupsOf(List<ITournamentMatch> groupMatches) =>
        groupMatches.Where(m => m.GroupName != "")
            .GroupBy(m => m.GroupName)
            .Select(g => new KnockoutGroup(g.Key, g.SelectMany(m => new[] { m.HomeTeamId, m.AwayTeamId }).Where(id => id != null).Distinct().Count()))
            .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private async Task<List<KnockoutGroup>> ScheduledGroupsAsync(int tournamentId)
    {
        var schedule = await _schedules.FirstOrDefaultAsync(s => s.TournamentId == tournamentId && s.IsActive, ScheduleQueries.WithFixtures, tracking: false);
        if (schedule == null) return [];
        return schedule.Groups
            .Select(g => new KnockoutGroup(g.GroupName, g.Fixtures.SelectMany(f => new[] { f.Home, f.Away }).Distinct().Count()))
            .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Puts each not-yet-started knockout match's teams (or what they're waiting for) in line with its sources.</summary>
    private static void Resolve(Stage stage)
    {
        // In bracket order, so a winner known now flows on in the same pass.
        foreach (var fixture in stage.Fixtures.OrderBy(f => f.Order))
        {
            if (stage.MatchOf(fixture) is not { Changeable: true } played) continue;
            var match = played.Match;
            match.RoundLabel = fixture.Label;

            var home = stage.TeamFor(fixture.Home);
            match.HomeTeamId = home;
            match.HomeTeamName = home is int h ? stage.TeamName(h) ?? "" : stage.Describe(fixture.Home);

            var away = stage.TeamFor(fixture.Away);
            match.AwayTeamId = away;
            match.AwayTeamName = away is int a ? stage.TeamName(a) ?? "" : stage.Describe(fixture.Away);
        }
    }

    private KnockoutStageDto Present(Stage stage)
    {
        var dto = new KnockoutStageDto
        {
            Groups = stage.Groups.Select(g => new KnockoutGroupDto
            {
                GroupName = g.Name,
                Teams = g.Teams,
                Finished = stage.Finishes.ContainsKey(g.Name)
            }).ToList(),
            Started = stage.AnyStarted
        };

        foreach (var fixture in stage.Fixtures.OrderBy(f => f.Order))
        {
            var played = stage.MatchOf(fixture);
            dto.Fixtures.Add(new KnockoutFixtureDto
            {
                Id = fixture.Id,
                Order = fixture.Order,
                Round = fixture.Round,
                Label = fixture.Label,
                Home = Side(stage, fixture.Home, played, home: true),
                Away = Side(stage, fixture.Away, played, home: false),
                MatchId = played?.Match.Id,
                MatchNumber = played?.Match.MatchNumber,
                Status = played?.Status,
                ResultLine = played?.Result,
                WinnerTeamId = played is { Over: true } ? played.Winner : null,
                CanChange = played is null or { Changeable: true }
            });

            if (fixture.Round == KnockoutRound.Final && played is { Over: true, Winner: int champion })
            {
                dto.ChampionTeamId = champion;
                dto.ChampionName = stage.TeamName(champion);
            }
        }
        return dto;
    }

    private static KnockoutSideDto Side(Stage stage, KnockoutSlot slot, Played? played, bool home)
    {
        // A match under way or over shows who actually played; otherwise who the slot works out to.
        var teamId = played is { Changeable: false } ? (home ? played.Match.HomeTeamId : played.Match.AwayTeamId) : stage.TeamFor(slot);
        return new KnockoutSideDto
        {
            TeamId = teamId,
            TeamName = teamId is int id ? stage.TeamName(id) : null,
            Source = stage.Describe(slot),
            Slot = new KnockoutSlotInput { Kind = slot.Kind, TeamId = slot.TeamId, GroupName = slot.GroupName, Place = slot.Place, FromOrder = slot.FromOrder },
            SetByOwner = slot.OverrideTeamId != null
        };
    }

    /// <summary>Fills the sides that are now known, saves, and answers with the stage.</summary>
    private async Task<ServiceResult<KnockoutStageDto>> SaveAndPresent(int tournamentId)
    {
        await _unitOfWork.SaveChangesAsync();
        var stage = (await LoadAsync(tournamentId))!;
        Resolve(stage);
        await _unitOfWork.SaveChangesAsync();
        return Success(Present(stage));
    }

    // -----------------------------------------------------------------
    // Reading
    // -----------------------------------------------------------------

    public async Task<ServiceResult<KnockoutStageDto>> GetAsync(int tournamentId)
    {
        var stage = await LoadAsync(tournamentId);
        if (stage == null) return NotFound("Tournament not found.");
        return Success(Present(stage));
    }

    public async Task<ServiceResult<List<KnockoutPresetDto>>> PresetsAsync(int tournamentId)
    {
        var stage = await LoadAsync(tournamentId);
        if (stage == null) return NotFound("Tournament not found.");

        var teamIds = stage.Teams.Keys.Order().ToList();
        var presets = KnockoutBracket.Presets(stage.Groups, teamIds);
        return Success(presets.Select(p => new KnockoutPresetDto
        {
            Id = p.Id,
            Name = p.Name,
            Description = p.Description,
            OffersThirdPlace = p.OffersThirdPlace,
            Fixtures = p.Fixtures.Select(f => new KnockoutPreviewDto
            {
                Round = f.Round,
                Label = f.Label,
                Home = Preview(f.Home, p.Fixtures, stage, teamIds),
                Away = Preview(f.Away, p.Fixtures, stage, teamIds)
            }).ToList()
        }).ToList());
    }

    private static string Preview(KnockoutSlot slot, List<PlannedFixture> planned, Stage stage, List<int> seeded) =>
        // Knockout only: the draw happens on Apply, so the preview names places in the draw, not teams.
        slot.Kind == KnockoutSlotKind.Team && slot.TeamId is int id
            ? $"Team {seeded.IndexOf(id) + 1}"
            : KnockoutBracket.Describe(slot, stage.TeamName, order => planned.FirstOrDefault(f => f.Order == order)?.Label);

    // -----------------------------------------------------------------
    // Presets and clearing
    // -----------------------------------------------------------------

    private const string StartedRefusal =
        "A knockout match has already started, so the knockout stage can't be replaced. Change the matches that haven't started one at a time.";

    public async Task<ServiceResult<KnockoutStageDto>> ApplyAsync(int tournamentId, KnockoutApplyRequest request)
    {
        var stage = await LoadAsync(tournamentId);
        if (stage == null) return NotFound("Tournament not found.");
        if (stage.AnyStarted) return Conflict(StartedRefusal);

        // Knockout only: the draw is random, made here so nobody can preview their way to a kind one.
        var teamIds = stage.Teams.Keys.OrderBy(_ => Random.Shared.Next()).ToList();
        var preset = KnockoutBracket.Build(request.Preset ?? "", request.ThirdPlace, stage.Groups, teamIds, stage.NextOrder);
        if (preset == null)
            return BadRequest(stage.Groups.Count == 0 && stage.Teams.Count < 2
                ? "Add at least two teams before setting up a knockout."
                : "That knockout format doesn't fit this tournament's groups. Pick one of the formats offered.");

        if (request.StartDate != null || request.EndDate != null)
        {
            var tournament = stage.Tournament;
            var start = request.StartDate ?? tournament.StartDate;
            var end = request.EndDate ?? tournament.EndDate;
            if (TournamentDates.Problem(start, end, tournament.TimeZoneId, _clock.GetUtcNow().UtcDateTime) is { } problem)
                return BadRequest(problem);
            tournament.StartDate = start;
            tournament.EndDate = end;
        }

        RemoveAll(stage);
        foreach (var planned in preset.Fixtures)
            AddFixture(stage, planned.Round, planned.Label, planned.Home, planned.Away, planned.Order);

        return await SaveAndPresent(tournamentId);
    }

    public async Task<ServiceResult<KnockoutStageDto>> ClearAsync(int tournamentId)
    {
        var stage = await LoadAsync(tournamentId);
        if (stage == null) return NotFound("Tournament not found.");
        if (stage.AnyStarted) return Conflict("A knockout match has already started, so the knockout stage can't be removed.");

        RemoveAll(stage);
        return await SaveAndPresent(tournamentId);
    }

    private void RemoveAll(Stage stage)
    {
        foreach (var fixture in stage.Fixtures.ToList()) Remove(stage, fixture);
    }

    private void Remove(Stage stage, KnockoutFixture fixture)
    {
        if (stage.MatchOf(fixture) is { } played)
        {
            if (played.Match is Match m) _matches.Remove(m);
            else _cricketMatches.Remove((CricketMatch)played.Match);
            stage.Matches.Remove(played);
        }
        _fixtures.Remove(fixture);
        stage.Fixtures.Remove(fixture);
    }

    /// <summary>Stages a knockout fixture and, once the tournament has started, the match that plays it.</summary>
    private void AddFixture(Stage stage, KnockoutRound round, string label, KnockoutSlot home, KnockoutSlot away, int order)
    {
        var fixture = new KnockoutFixture
        {
            TournamentId = stage.Tournament.Id,
            Order = order,
            Round = round,
            Label = label,
            Home = home,
            Away = away,
            CreatedByUserId = _currentUser.UserId
        };
        _fixtures.Add(fixture);
        stage.Fixtures.Add(fixture);
        if (stage.Tournament.IsStarted) AddMatch(stage, fixture);
    }

    /// <summary>The playable match for a fixture, numbered after every match so far; its sides are filled by <see cref="Resolve"/>.</summary>
    private void AddMatch(Stage stage, KnockoutFixture fixture)
    {
        var number = stage.Matches.Count == 0 ? 1 : stage.Matches.Max(m => m.Match.MatchNumber) + 1;
        ITournamentMatch match;
        if (stage.Tournament.Sport == Sport.Cricket)
        {
            var cricket = new CricketMatch { TournamentId = stage.Tournament.Id, KnockoutFixture = fixture, Status = CricketMatchStatus.NotStarted };
            _cricketMatches.Add(cricket);
            match = cricket;
        }
        else
        {
            var football = new Match { TournamentId = stage.Tournament.Id, KnockoutFixture = fixture, Status = MatchStatus.NotStarted };
            _matches.Add(football);
            match = football;
        }
        match.MatchNumber = number;
        match.GroupName = "";
        match.RoundLabel = fixture.Label;
        stage.Matches.Add(new Played(match, true, false, null, "NotStarted", null));
    }

    // -----------------------------------------------------------------
    // One match at a time
    // -----------------------------------------------------------------

    public async Task<ServiceResult<KnockoutStageDto>> AddAsync(int tournamentId, KnockoutFixtureRequest request)
    {
        var stage = await LoadAsync(tournamentId);
        if (stage == null) return NotFound("Tournament not found.");

        var order = stage.NextOrder;
        var (home, away, problem) = Sides(stage, request, order);
        if (problem != null) return BadRequest(problem);

        AddFixture(stage, request.Round, LabelFor(stage, request, null), home!, away!, order);
        return await SaveAndPresent(tournamentId);
    }

    public async Task<ServiceResult<KnockoutStageDto>> UpdateAsync(int tournamentId, int fixtureId, KnockoutFixtureRequest request)
    {
        var stage = await LoadAsync(tournamentId);
        if (stage == null) return NotFound("Tournament not found.");
        if (stage.Fixtures.FirstOrDefault(f => f.Id == fixtureId) is not { } fixture) return NotFound("Knockout match not found.");
        if (stage.MatchOf(fixture) is { Changeable: false }) return Conflict($"{fixture.Label} has started, so its sides can't change.");

        var (home, away, problem) = Sides(stage, request, fixture.Order);
        if (problem != null) return BadRequest(problem);

        fixture.Round = request.Round;
        fixture.Label = LabelFor(stage, request, fixture);
        fixture.Home = home!;
        fixture.Away = away!;
        return await SaveAndPresent(tournamentId);
    }

    public async Task<ServiceResult<KnockoutStageDto>> DeleteAsync(int tournamentId, int fixtureId)
    {
        var stage = await LoadAsync(tournamentId);
        if (stage == null) return NotFound("Tournament not found.");
        if (stage.Fixtures.FirstOrDefault(f => f.Id == fixtureId) is not { } fixture) return NotFound("Knockout match not found.");
        if (stage.MatchOf(fixture) is { Changeable: false }) return Conflict($"{fixture.Label} has started, so it can't be deleted.");

        if (stage.Fixtures.FirstOrDefault(f => f.Id != fixture.Id && (Feeds(f.Home, fixture) || Feeds(f.Away, fixture))) is { } later)
            return Conflict($"{later.Label} takes a team from {fixture.Label}. Change or delete {later.Label} first.");

        Remove(stage, fixture);
        return await SaveAndPresent(tournamentId);
    }

    private static bool Feeds(KnockoutSlot slot, KnockoutFixture from) =>
        slot.Kind is KnockoutSlotKind.WinnerOf or KnockoutSlotKind.LoserOf && slot.FromOrder == from.Order;

    /// <summary>The owner's choice for one side — a coin toss, a no-result settled off the field — or back to its source.</summary>
    public async Task<ServiceResult<KnockoutStageDto>> SetTeamAsync(int tournamentId, int fixtureId, KnockoutTeamRequest request)
    {
        var stage = await LoadAsync(tournamentId);
        if (stage == null) return NotFound("Tournament not found.");
        if (stage.Fixtures.FirstOrDefault(f => f.Id == fixtureId) is not { } fixture) return NotFound("Knockout match not found.");
        if (stage.MatchOf(fixture) is { Changeable: false }) return Conflict($"{fixture.Label} has started, so its teams can't change.");

        var home = string.Equals(request.Side, "Home", StringComparison.OrdinalIgnoreCase);
        if (!home && !string.Equals(request.Side, "Away", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Say which side to set: Home or Away.");

        if (request.TeamId is int teamId)
        {
            if (!stage.Teams.ContainsKey(teamId)) return BadRequest("That team isn't in this tournament.");
            var other = stage.TeamFor(home ? fixture.Away : fixture.Home);
            if (other == teamId) return BadRequest($"{stage.TeamName(teamId)} is already the other side of {fixture.Label}.");
        }

        var slot = home ? fixture.Home : fixture.Away;
        // Owned values are replaced, not edited in place, so the change is tracked.
        var changed = new KnockoutSlot
        {
            Kind = slot.Kind, TeamId = slot.TeamId, GroupName = slot.GroupName, Place = slot.Place, FromOrder = slot.FromOrder,
            OverrideTeamId = request.TeamId
        };
        if (home) fixture.Home = changed; else fixture.Away = changed;
        return await SaveAndPresent(tournamentId);
    }

    /// <summary>The two sides as slots, or the sentence saying why they can't be.</summary>
    private static (KnockoutSlot? Home, KnockoutSlot? Away, string? Problem) Sides(Stage stage, KnockoutFixtureRequest request, int order)
    {
        if (!Enum.IsDefined(request.Round)) return (null, null, "Pick the round this match belongs to.");

        var home = Slot(stage, request.Home, order, out var homeProblem);
        if (homeProblem != null) return (null, null, homeProblem);
        var away = Slot(stage, request.Away, order, out var awayProblem);
        if (awayProblem != null) return (null, null, awayProblem);

        if (home!.Kind == away!.Kind && home.TeamId == away.TeamId && home.GroupName == away.GroupName
            && home.Place == away.Place && home.FromOrder == away.FromOrder)
            return (null, null, "Both sides come from the same place. Pick a different one for each side.");
        return (home, away, null);
    }

    private static KnockoutSlot? Slot(Stage stage, KnockoutSlotInput? input, int order, out string? problem)
    {
        problem = null;
        if (input == null) { problem = "Choose where each side comes from."; return null; }

        switch (input.Kind)
        {
            case KnockoutSlotKind.Team:
                if (input.TeamId is not int teamId || !stage.Teams.ContainsKey(teamId))
                    problem = "Pick a team from this tournament.";
                return new KnockoutSlot { Kind = input.Kind, TeamId = input.TeamId };

            case KnockoutSlotKind.GroupPlace:
                var group = stage.Groups.FirstOrDefault(g => string.Equals(g.Name, input.GroupName?.Trim(), StringComparison.OrdinalIgnoreCase));
                if (group == null)
                    problem = stage.Groups.Count == 0 ? "This tournament has no groups, so pick a team instead." : "Pick one of the tournament's groups.";
                else if (input.Place is not int place || place < 1 || place > group.Teams)
                    problem = $"Group {group.Name} has {group.Teams} teams, so pick a place from 1 to {group.Teams}.";
                return new KnockoutSlot { Kind = input.Kind, GroupName = group?.Name, Place = input.Place };

            case KnockoutSlotKind.WinnerOf:
            case KnockoutSlotKind.LoserOf:
                if (input.FromOrder is not int from || stage.ByOrder(from) == null || from >= order)
                    problem = "A side can only come from an earlier knockout match.";
                return new KnockoutSlot { Kind = input.Kind, FromOrder = input.FromOrder };

            default:
                problem = "Choose where each side comes from.";
                return null;
        }
    }

    /// <summary>The owner's name for it, or the round's own, numbered when the round has several: "Semi-final 2".</summary>
    private static string LabelFor(Stage stage, KnockoutFixtureRequest request, KnockoutFixture? editing)
    {
        var typed = request.Label?.Trim();
        if (!string.IsNullOrEmpty(typed)) return typed.Length > 60 ? typed[..60] : typed;
        if (editing != null && editing.Round == request.Round) return editing.Label;

        var name = KnockoutBracket.RoundName(request.Round);
        if (request.Round is KnockoutRound.Final or KnockoutRound.ThirdPlace or KnockoutRound.Eliminator)
            return name;
        var taken = stage.Fixtures.Count(f => f.Round == request.Round && f != editing);
        return $"{name} {taken + 1}";
    }

    // -----------------------------------------------------------------
    // The tournament's start, and results coming in
    // -----------------------------------------------------------------

    public Task<bool> AnyAsync(int tournamentId) => _fixtures.AnyAsync(f => f.TournamentId == tournamentId);

    public async Task<string?> ProblemAtStartAsync(int tournamentId, IReadOnlyList<KnockoutGroup> groups)
    {
        var fixtures = await _fixtures.ListAsync(f => f.TournamentId == tournamentId, tracking: false);
        foreach (var fixture in fixtures)
            foreach (var slot in new[] { fixture.Home, fixture.Away }.Where(s => s.Kind == KnockoutSlotKind.GroupPlace && s.OverrideTeamId == null))
            {
                var group = groups.FirstOrDefault(g => g.Name == slot.GroupName);
                if (group == null)
                    return $"{fixture.Label} takes a team from Group {slot.GroupName}, which the schedule doesn't have any more. Set up the knockout stage again.";
                if (slot.Place > group.Teams)
                    return $"{fixture.Label} needs the {KnockoutBracket.Ordinal(slot.Place ?? 0)}-placed team in Group {group.Name}, which now has {group.Teams} teams. Set up the knockout stage again.";
            }
        return null;
    }

    public async Task<int> CreateMatchesAsync(int tournamentId)
    {
        var stage = await LoadAsync(tournamentId);
        if (stage == null || stage.Fixtures.Count == 0) return 0;

        var missing = stage.Fixtures.OrderBy(f => f.Order).Where(f => stage.MatchOf(f) == null).ToList();
        foreach (var fixture in missing) AddMatch(stage, fixture);
        await SaveAndPresent(tournamentId);
        return missing.Count;
    }

    public async Task AfterScoringAsync()
    {
        // Only a match that is now over can have settled a group or a knockout side.
        if (_context.Match is not { IsOver: true }) return;
        if (!await AnyAsync(_context.TournamentId)) return;

        var stage = await LoadAsync(_context.TournamentId);
        if (stage == null) return;
        Resolve(stage);
        await _unitOfWork.SaveChangesAsync();
    }
}
