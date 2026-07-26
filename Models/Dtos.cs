namespace TournamentScheduler.Api.Models;

public class AddTeamsRequest
{
    public List<string> TeamNames { get; set; } = new();
}

public class RandomizeGroupsRequest
{
    public List<string> TeamNames { get; set; } = new();
    public int GroupCount { get; set; } = 2;
}

public class ManualGroupsRequest
{
    public List<GroupInput> Groups { get; set; } = new();
}

public class GroupInput
{
    public string Name { get; set; } = string.Empty;
    public List<string> Teams { get; set; } = new();
}

public class GenerateScheduleRequest
{
    public List<GroupInput> Groups { get; set; } = new();
    public int MatchesPerTeam { get; set; } = 5;
}

public class ApproveScheduleRequest
{
    public List<GroupInput> Groups { get; set; } = new(); // teams, for reference
    public TournamentSchedule Schedule { get; set; } = new();
}

public class CreateTeamRequest
{
    public string Name { get; set; } = string.Empty;
}

public class UpdateTeamRequest
{
    public string Name { get; set; } = string.Empty;
}

public class CreatePlayerRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Position { get; set; }
    public int? JerseyNumber { get; set; }
}

public class UpdatePlayerRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Position { get; set; }
    public int? JerseyNumber { get; set; }
}

public class CreateTournamentRequest
{
    public string Name { get; set; } = string.Empty;
}

public class ApproveScheduleWithTournamentRequest
{
    public int TournamentId { get; set; }
    public TournamentSchedule Schedule { get; set; } = new();
}