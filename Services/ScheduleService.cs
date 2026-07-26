using TournamentScheduler.Api.Data;
using TournamentScheduler.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace TournamentScheduler.Api.Services;

public class ScheduleService : IScheduleService
{
    private readonly TournamentDbContext _db;
    public ScheduleService(TournamentDbContext db)
    {
        _db = db;
    }
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

    public GroupSchedule GenerateGroupSchedule(string groupName, List<string> teams, int matchesPerTeam)
    {
        var working = new List<string>(teams);
        var hasBye = working.Count % 2 != 0;
        if (hasBye) working.Add("BYE");

        int n = working.Count;
        if (n < 2)
        {
            return new GroupSchedule
            {
                GroupName = groupName,
                Fixtures = new List<Fixture>(),
                RoundsUsed = 0,
                MaxPossibleMatchesPerTeam = 0
            };
        }

        int rounds = n - 1;
        int half = n / 2;
        var arr = new List<string>(working);
        var roundsList = new List<List<(string home, string away)>>();

        for (int r = 0; r < rounds; r++)
        {
            var roundMatches = new List<(string, string)>();
            for (int i = 0; i < half; i++)
            {
                var home = arr[i];
                var away = arr[n - 1 - i];
                if (home != "BYE" && away != "BYE")
                    roundMatches.Add((home, away));
            }
            roundsList.Add(roundMatches);

            var fixedTeam = arr[0];
            var rest = arr.Skip(1).ToList();
            var last = rest[^1];
            rest.RemoveAt(rest.Count - 1);
            rest.Insert(0, last);
            arr = new List<string> { fixedTeam };
            arr.AddRange(rest);
        }

        int capped = Math.Min(matchesPerTeam, rounds);
        var chosenRounds = roundsList.Take(capped);

        var fixtures = chosenRounds
            .SelectMany(round => round)
            .Select((match, idx) => new Fixture
            {
                Id = idx + 1,
                Home = match.home,
                Away = match.away
            })
            .ToList();

        return new GroupSchedule
        {
            GroupName = groupName,
            Fixtures = fixtures,
            RoundsUsed = capped,
            MaxPossibleMatchesPerTeam = rounds
        };
    }

    public TournamentSchedule GenerateTournamentSchedule(List<GroupInput> groups, int matchesPerTeam)
    {
        var result = new TournamentSchedule();
        foreach (var g in groups)
        {
            result.Groups.Add(GenerateGroupSchedule(g.Name, g.Teams, matchesPerTeam));
        }
        return result;
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
        var existingActive = await _db.SavedSchedules
            .Where(s => s.TournamentId == tournamentId && s.IsActive)
            .ToListAsync();

        foreach (var old in existingActive)
        {
            old.IsActive = false;
        }

        var saved = new SavedSchedule
        {
            TournamentId = tournamentId,
            IsActive = true,
            MatchesPerTeam = schedule.Groups.FirstOrDefault()?.RoundsUsed ?? 0,
            TotalMatches = schedule.TotalMatches,
            Groups = schedule.Groups.Select(g => new SavedGroup
            {
                GroupName = g.GroupName,
                Fixtures = g.Fixtures.Select(f => new SavedFixture
                {
                    MatchNumber = f.Id,
                    Home = f.Home,
                    Away = f.Away
                }).ToList()
            }).ToList()
        };

        _db.SavedSchedules.Add(saved);
        await _db.SaveChangesAsync();
        return saved;
    }
}