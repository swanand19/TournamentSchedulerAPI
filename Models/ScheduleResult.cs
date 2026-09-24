namespace TournamentScheduler.Api.Models;

public class GroupSchedule
{
    public string GroupName { get; set; } = string.Empty;
    public List<Fixture> Fixtures { get; set; } = new();

    /// <summary>Number of matchdays the fixtures are spread over.</summary>
    public int RoundsUsed { get; set; }

    /// <summary>Matches per team achievable without any team facing another twice (= teamCount - 1).</summary>
    public int MaxPossibleMatchesPerTeam { get; set; }

    public int TeamCount { get; set; }
    public int RequestedMatchesPerTeam { get; set; }

    /// <summary>Fewest matches any single team in this group actually plays.</summary>
    public int MinMatchesPerTeam { get; set; }

    /// <summary>Most matches any single team in this group actually plays.</summary>
    public int MaxMatchesPerTeam { get; set; }

    /// <summary>Complete round-robin legs scheduled — every pair meets at least this many times.</summary>
    public int FullRoundRobinLegs { get; set; }

    /// <summary>Top-up matches per team scheduled on top of the complete legs.</summary>
    public int ExtraMatchesPerTeam { get; set; }

    /// <summary>Highest number of times any single pair of teams meets.</summary>
    public int MaxMeetingsBetweenAnyPair { get; set; }

    public bool RepeatFixturesAllowed { get; set; }

    /// <summary>Human-readable notes about anything the generator had to adjust or cap.</summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>Team name -> matches scheduled for that team.</summary>
    public Dictionary<string, int> MatchesByTeam { get; set; } = new();
}

public class TournamentSchedule
{
    public List<GroupSchedule> Groups { get; set; } = new();
    public int TotalMatches => Groups.Sum(g => g.Fixtures.Count);
    public List<string> Warnings => Groups
        .SelectMany(g => g.Warnings.Select(w => $"Group {g.GroupName}: {w}"))
        .ToList();
}
