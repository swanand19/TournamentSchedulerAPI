namespace TournamentScheduler.Api.Models;
public class GroupSchedule
{
    public string GroupName { get; set; } = string.Empty;
    public List<Fixture> Fixtures { get; set; } = new();
    public int RoundsUsed { get; set; }
    public int MaxPossibleMatchesPerTeam { get; set; }
}

public class TournamentSchedule
{
    public List<GroupSchedule> Groups { get; set; } = new();
    public int TotalMatches => Groups.Sum(g => g.Fixtures.Count);
}
