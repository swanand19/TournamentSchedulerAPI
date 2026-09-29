using TournamentScheduler.Api.Data;
using TournamentScheduler.Api.Data.DataServices;
using TournamentScheduler.Api.Data.Queries;
using TournamentScheduler.Api.Models;

namespace TournamentScheduler.Api.Services;

public class ScheduleService : ServiceBase, IScheduleService
{
    private readonly IDataService<Tournament> _tournaments;
    private readonly IDataService<SavedSchedule> _schedules;
    private readonly IDataService<Team> _teams;
    private readonly IUnitOfWork _unitOfWork;

    public ScheduleService(
        IDataService<Tournament> tournaments,
        IDataService<SavedSchedule> schedules,
        IDataService<Team> teams,
        IUnitOfWork unitOfWork)
    {
        _tournaments = tournaments;
        _schedules = schedules;
        _teams = teams;
        _unitOfWork = unitOfWork;
    }

    // -----------------------------------------------------------------
    // Endpoint actions
    // -----------------------------------------------------------------

    public ServiceResult<object> DrawGroups(RandomizeGroupsRequest request)
    {
        if (request.GroupCount < 1)
            return BadRequest("Group count must be at least 1.");

        var minTeamsNeeded = Math.Max(2, request.GroupCount * 2);
        if (request.TeamNames == null || request.TeamNames.Count < minTeamsNeeded)
            return BadRequest($"Need at least {minTeamsNeeded} teams for {request.GroupCount} group(s) (min 2 per group).");

        var groups = RandomizeGroups(request.TeamNames, request.GroupCount);
        return Ok(new { groups });
    }

    public ServiceResult<object> CheckManualGroups(ManualGroupsRequest request)
    {
        if (request.Groups == null || request.Groups.Count < 1)
            return BadRequest("Provide at least 1 group.");

        if (request.Groups.Any(g => g.Teams.Count == 0))
            return BadRequest("Every group needs at least one team.");

        var allTeams = request.Groups.SelectMany(g => g.Teams).ToList();
        var duplicates = allTeams.GroupBy(t => t).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Any())
            return BadRequest($"Team(s) appear in more than one group: {string.Join(", ", duplicates)}");

        return Ok(new { groups = request.Groups });
    }

    public ServiceResult<TournamentSchedule> GenerateSchedule(GenerateScheduleRequest request)
    {
        if (request.Groups == null || request.Groups.Count < 1)
            return BadRequest("Provide at least 1 group.");

        if (request.Groups.Any(g => g.Teams.Count < 2))
            return BadRequest("Every group needs at least 2 teams.");

        if (request.MatchesPerTeam < 1)
            return BadRequest("Matches per team must be at least 1.");

        // Without repeats the ceiling is (group size - 1); with repeats a hard cap keeps a typo
        // from generating thousands of fixtures.
        const int absoluteCap = 60;
        if (request.MatchesPerTeam > absoluteCap)
            return BadRequest($"Matches per team is capped at {absoluteCap}.");

        return Success(GenerateTournamentSchedule(request.Groups, request.MatchesPerTeam, request.AllowRepeatFixtures));
    }

    public async Task<ServiceResult<object>> ApproveAsync(ApproveScheduleWithTournamentRequest request)
    {
        if (request.Schedule?.Groups == null || request.Schedule.Groups.Count == 0)
            return BadRequest("No schedule to approve.");

        if (request.TournamentId <= 0)
            return BadRequest("A tournament must be specified.");

        var tournament = await _tournaments.GetByIdAsync(request.TournamentId);
        if (tournament == null) return NotFound("Tournament not found.");
        if (tournament.IsStarted)
            return BadRequest("This tournament has already started — its schedule is locked and can no longer be changed.");

        var saved = await ApproveScheduleAsync(request.TournamentId, request.Schedule);
        return Ok(new { savedScheduleId = saved.Id, savedAt = saved.CreatedAt });
    }

    public async Task<ServiceResult<SavedSchedule>> GetSavedScheduleAsync(int id)
    {
        var schedule = await _schedules.FirstOrDefaultAsync(s => s.Id == id, ScheduleQueries.WithFixtures);
        if (schedule == null) return NotFound();
        return Success(schedule);
    }

    // -----------------------------------------------------------------
    // Drawing groups and generating fixtures
    // -----------------------------------------------------------------

    public List<Group> RandomizeGroups(List<string> teams, int groupCount)
    {
        if (groupCount < 1) groupCount = 1;

        var shuffled = teams.OrderBy(_ => Guid.NewGuid()).ToList();
        var groups = new List<Group>();

        for (int i = 0; i < groupCount; i++)
        {
            groups.Add(new Group
            {
                Name = GroupLabel(i),
                Teams = new List<string>()
            });
        }

        // deal teams round-robin style across groups for balanced sizes
        for (int i = 0; i < shuffled.Count; i++)
        {
            groups[i % groupCount].Teams.Add(shuffled[i]);
        }

        return groups;
    }

    /// <summary>
    /// Builds a group's fixture list so that every team plays <paramref name="matchesPerTeam"/> matches.
    ///
    /// With repeats off, a team can face each opponent at most once, so the ceiling is (teams - 1).
    /// With repeats on, the request is met by playing complete round-robin legs first
    /// (everyone faces everyone) and then topping each team up with the remaining matches
    /// against randomly chosen opponents, spread so no pair is used twice in the top-up.
    ///
    /// A perfectly even split is not always possible: every match uses two team-appearances, so
    /// teams x matchesPerTeam has to be even. When it is odd, exactly one team plays one fewer
    /// match and a warning explains it.
    /// </summary>
    public GroupSchedule GenerateGroupSchedule(string groupName, List<string> teams, int matchesPerTeam, bool allowRepeatFixtures)
    {
        var roster = (teams ?? new List<string>())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var n = roster.Count;
        var schedule = new GroupSchedule
        {
            GroupName = groupName,
            TeamCount = n,
            RequestedMatchesPerTeam = matchesPerTeam,
            MaxPossibleMatchesPerTeam = Math.Max(0, n - 1),
            RepeatFixturesAllowed = allowRepeatFixtures
        };

        if (n < 2)
        {
            schedule.Warnings.Add("A group needs at least 2 teams before any fixtures can be scheduled.");
            return schedule;
        }

        if (matchesPerTeam < 1)
        {
            schedule.Warnings.Add("Matches per team must be at least 1 — no fixtures were generated.");
            return schedule;
        }

        // Randomising the order is what makes the top-up opponents random rather than always
        // the alphabetically-adjacent teams.
        var order = roster.OrderBy(_ => Random.Shared.Next()).ToList();
        var maxWithoutRepeats = n - 1;

        int legs;
        int remainder;

        if (allowRepeatFixtures)
        {
            legs = matchesPerTeam / maxWithoutRepeats;
            remainder = matchesPerTeam % maxWithoutRepeats;
        }
        else
        {
            var capped = Math.Min(matchesPerTeam, maxWithoutRepeats);
            if (matchesPerTeam > maxWithoutRepeats)
            {
                schedule.Warnings.Add(
                    $"Repeat fixtures are off, so each team can face each of the other {maxWithoutRepeats} team(s) only once — " +
                    $"{matchesPerTeam} matches per team was reduced to {capped}. Turn on repeat fixtures to schedule more.");
            }
            legs = capped == maxWithoutRepeats ? 1 : 0;
            remainder = capped == maxWithoutRepeats ? 0 : capped;
        }

        var phases = new List<List<List<(int Home, int Away)>>>();
        var homeCount = new int[n];
        var awayCount = new int[n];

        for (var leg = 0; leg < legs; leg++)
        {
            // Flip home/away every other leg so a double round robin gives each pair one match each way.
            var legRounds = RoundRobinRounds(n, flipHomeAway: leg % 2 == 1);
            foreach (var round in legRounds)
                foreach (var (h, a) in round) { homeCount[h]++; awayCount[a]++; }
            phases.Add(legRounds);
        }

        var shortTeamIndices = new List<int>();
        if (remainder > 0)
        {
            var extraEdges = RegularLayer(n, remainder, out shortTeamIndices);
            var oriented = BalanceOrientation(extraEdges, homeCount, awayCount);
            phases.Add(PackIntoRounds(oriented));
        }

        var fixtures = new List<Fixture>();
        var roundNumber = 0;
        foreach (var phase in phases)
        {
            foreach (var round in phase)
            {
                if (round.Count == 0) continue;
                roundNumber++;
                foreach (var (h, a) in round)
                {
                    fixtures.Add(new Fixture
                    {
                        Id = fixtures.Count + 1,
                        Round = roundNumber,
                        Home = order[h],
                        Away = order[a]
                    });
                }
            }
        }

        schedule.Fixtures = fixtures;
        schedule.RoundsUsed = roundNumber;
        schedule.FullRoundRobinLegs = legs;
        schedule.ExtraMatchesPerTeam = remainder;

        var perTeam = order.ToDictionary(t => t, _ => 0);
        var meetings = new Dictionary<(string, string), int>();
        foreach (var f in fixtures)
        {
            perTeam[f.Home]++;
            perTeam[f.Away]++;
            var key = string.CompareOrdinal(f.Home, f.Away) < 0 ? (f.Home, f.Away) : (f.Away, f.Home);
            meetings[key] = meetings.TryGetValue(key, out var c) ? c + 1 : 1;
        }

        schedule.MatchesByTeam = perTeam;
        schedule.MinMatchesPerTeam = perTeam.Values.DefaultIfEmpty(0).Min();
        schedule.MaxMatchesPerTeam = perTeam.Values.DefaultIfEmpty(0).Max();
        schedule.MaxMeetingsBetweenAnyPair = meetings.Values.DefaultIfEmpty(0).Max();

        if (shortTeamIndices.Count > 0)
        {
            var target = legs * maxWithoutRepeats + remainder;
            var shortNames = string.Join(", ", shortTeamIndices.Select(i => order[i]));
            schedule.Warnings.Add(
                $"{n} teams x {target} matches each needs {n * target} team slots, but every match fills exactly 2 — " +
                $"an odd total can't be split evenly. {shortNames} play{(shortTeamIndices.Count == 1 ? "s" : "")} {target - 1}; " +
                $"every other team plays {target}. Use {target - 1} or {target + 1} matches per team for a perfectly even group.");
        }

        if (legs > 1)
        {
            schedule.Warnings.Add(
                $"Every pair of teams meets {legs} times" +
                (remainder > 0 ? $", and {remainder} extra match(es) per team are drawn against random opponents from the group." : "."));
        }
        else if (legs == 1 && remainder > 0)
        {
            schedule.Warnings.Add(
                $"Every team faces all {maxWithoutRepeats} opponents once, then plays {remainder} extra match(es) against random opponents from the group.");
        }

        return schedule;
    }

    public TournamentSchedule GenerateTournamentSchedule(List<GroupInput> groups, int matchesPerTeam, bool allowRepeatFixtures)
    {
        var result = new TournamentSchedule();
        foreach (var g in groups)
        {
            result.Groups.Add(GenerateGroupSchedule(g.Name, g.Teams, matchesPerTeam, allowRepeatFixtures));
        }
        return result;
    }

    // ---------------------------------------------------------------------
    // Fixture-graph building blocks
    // ---------------------------------------------------------------------

    /// <summary>
    /// Circle-method round robin over team indices 0..n-1. Every team meets every other exactly once,
    /// spread over n-1 matchdays (n rounds with one team resting each day when n is odd).
    /// </summary>
    private static List<List<(int Home, int Away)>> RoundRobinRounds(int n, bool flipHomeAway)
    {
        var size = n % 2 == 0 ? n : n + 1;   // indices >= n are the "bye" slot
        var arr = Enumerable.Range(0, size).ToList();
        var rounds = new List<List<(int, int)>>();

        for (var r = 0; r < size - 1; r++)
        {
            var round = new List<(int, int)>();
            for (var i = 0; i < size / 2; i++)
            {
                var a = arr[i];
                var b = arr[size - 1 - i];
                if (a >= n || b >= n) continue;             // one of them is the bye

                // Every team but the pinned one rotates between the two sides of the circle, so the
                // natural orientation already balances them. The pinned team never moves, so its own
                // fixture alternates instead — otherwise it would be at home every single round.
                var homeFirst = (i != 0 || r % 2 == 0) ^ flipHomeAway;
                round.Add(homeFirst ? (a, b) : (b, a));
            }
            rounds.Add(round);

            var pinned = arr[0];
            var rest = arr.Skip(1).ToList();
            var last = rest[^1];
            rest.RemoveAt(rest.Count - 1);
            rest.Insert(0, last);
            arr = new List<int> { pinned };
            arr.AddRange(rest);
        }

        return rounds;
    }

    /// <summary>
    /// Builds a set of matches in which every team faces <paramref name="degree"/> <em>different</em>
    /// opponents — a degree-regular graph, built as a circulant so the pairings are evenly spread.
    /// When n * degree is odd no such graph exists; the teams left one match short are reported via
    /// <paramref name="shortIndices"/> (normally exactly one).
    /// </summary>
    private static List<(int A, int B)> RegularLayer(int n, int degree, out List<int> shortIndices)
    {
        shortIndices = new List<int>();
        var edges = new HashSet<(int, int)>();
        if (degree <= 0 || n < 2) return new List<(int, int)>();

        degree = Math.Min(degree, n - 1);

        var needsOddPatch = (long)n * degree % 2 != 0;   // only possible when both n and degree are odd
        var baseDegree = needsOddPatch ? degree - 1 : degree;

        void Add(int a, int b)
        {
            if (a == b) return;
            edges.Add(a < b ? (a, b) : (b, a));
        }

        if (baseDegree % 2 == 1)
        {
            // n is even here: half-offsets plus the "opposite side of the circle" perfect matching.
            for (var j = 1; j <= (baseDegree - 1) / 2; j++)
                for (var i = 0; i < n; i++) Add(i, (i + j) % n);
            for (var i = 0; i < n / 2; i++) Add(i, i + n / 2);
        }
        else
        {
            for (var j = 1; j <= baseDegree / 2; j++)
                for (var i = 0; i < n; i++) Add(i, (i + j) % n);
        }

        if (needsOddPatch)
        {
            // Everyone bar one team gets one more match. Both n and degree are odd here, so
            // degree <= n-2 and the widest step round the circle, (n-1)/2, is unused by the base
            // layer above. That step is always coprime with an odd n, so walking it visits all n
            // teams in a single loop; pairing off alternate steps of that loop covers all but one
            // team, with no pair repeated. Searching for such a pairing instead can strand several
            // teams, which is why it is constructed rather than hunted for.
            var step = (n - 1) / 2;
            var start = Random.Shared.Next(n);

            var loop = new int[n];
            for (var i = 0; i < n; i++) loop[i] = (start + i * step) % n;

            for (var i = 0; i + 1 < n; i += 2) Add(loop[i], loop[i + 1]);
            shortIndices = new List<int> { loop[n - 1] };
        }

        return edges.ToList();
    }

    /// <summary>
    /// Chooses home/away for each match so every team's home and away counts stay as even as possible
    /// across the whole group schedule.
    /// </summary>
    private static List<(int Home, int Away)> BalanceOrientation(List<(int A, int B)> edges, int[] homeCount, int[] awayCount)
    {
        var result = new List<(int, int)>();
        foreach (var (a, b) in edges.OrderBy(_ => Random.Shared.Next()))
        {
            // Whoever is furthest "behind" on home games takes the home slot.
            var aBias = homeCount[a] - awayCount[a];
            var bBias = homeCount[b] - awayCount[b];
            var aHome = aBias < bBias || (aBias == bBias && Random.Shared.Next(2) == 0);

            if (aHome) { result.Add((a, b)); homeCount[a]++; awayCount[b]++; }
            else { result.Add((b, a)); homeCount[b]++; awayCount[a]++; }
        }
        return result;
    }

    /// <summary>
    /// Greedily splits matches into matchdays where no team appears twice, so fixtures can be played
    /// in order without a team playing back-to-back.
    /// </summary>
    private static List<List<(int Home, int Away)>> PackIntoRounds(List<(int Home, int Away)> edges)
    {
        var remaining = new List<(int, int)>(edges);
        var rounds = new List<List<(int, int)>>();

        while (remaining.Count > 0)
        {
            var used = new HashSet<int>();
            var round = new List<(int, int)>();
            var leftovers = new List<(int, int)>();

            foreach (var e in remaining)
            {
                if (used.Contains(e.Item1) || used.Contains(e.Item2)) { leftovers.Add(e); continue; }
                round.Add(e);
                used.Add(e.Item1);
                used.Add(e.Item2);
            }

            rounds.Add(round);
            remaining = leftovers;
        }

        return rounds;
    }

    private static string GroupLabel(int index)
    {
        // 0 -> A, 1 -> B, ... 25 -> Z, 26 -> AA, etc.
        var label = "";
        index++;
        while (index > 0)
        {
            int rem = (index - 1) % 26;
            label = (char)('A' + rem) + label;
            index = (index - 1) / 26;
        }
        return label;
    }

    public async Task<SavedSchedule> ApproveScheduleAsync(int tournamentId, TournamentSchedule schedule)
    {
        // deactivate any previously active schedule for this tournament
        var existingActive = await _schedules.ListAsync(s => s.TournamentId == tournamentId && s.IsActive);

        foreach (var old in existingActive)
        {
            old.IsActive = false;
        }

        // Resolve team ids up front and store them on the fixture. A schedule only ever held team
        // names, so starting the tournament had to match on the name — and a team renamed between
        // approval and kick-off silently produced a match with no team id, which then vanished
        // from the standings. Pinning the id here means the name is only a display label.
        var teamIdsByName = (await _teams.ListAsync(t => t.TournamentId == tournamentId))
            .ToDictionary(t => t.Name, t => t.Id);

        int? TeamId(string name) =>
            teamIdsByName.TryGetValue(name, out var id) ? id : null;

        var saved = new SavedSchedule
        {
            TournamentId = tournamentId,
            IsActive = true,
            MatchesPerTeam = schedule.Groups.Count == 0 ? 0 : schedule.Groups.Max(g => g.MaxMatchesPerTeam),
            TotalMatches = schedule.TotalMatches,
            Groups = schedule.Groups.Select(g => new SavedGroup
            {
                GroupName = g.GroupName,
                Fixtures = g.Fixtures.Select(f => new SavedFixture
                {
                    MatchNumber = f.Id,
                    Round = f.Round,
                    Home = f.Home,
                    Away = f.Away,
                    HomeTeamId = TeamId(f.Home),
                    AwayTeamId = TeamId(f.Away)
                }).ToList()
            }).ToList()
        };

        _schedules.Add(saved);
        await _unitOfWork.SaveChangesAsync();
        return saved;
    }
}
