using TournamentScheduler.Api.Models.Cricket;

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

    /// <summary>
    /// When false (the default) a team faces each opponent at most once, capping matches per team
    /// at (group size - 1). When true, complete round-robin legs are repeated and the remainder is
    /// topped up against random opponents, so any number of matches per team can be scheduled.
    /// </summary>
    public bool AllowRepeatFixtures { get; set; }
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

    /// <summary>
    /// The side's usual captain, pre-selected when an XI is picked. Null clears it; because null
    /// is also what a caller that does not care about captaincy sends, <see cref="SetCaptain"/>
    /// says which of the two this is.
    /// </summary>
    public int? DefaultCaptainPlayerId { get; set; }

    /// <summary>True when <see cref="DefaultCaptainPlayerId"/> is meant to be applied.</summary>
    public bool SetCaptain { get; set; }
}

/// <summary>
/// The cricketing fields of a player, posted alongside the player itself. Null on the request
/// leaves any existing profile untouched — a football client never sends one, and a rename from
/// the cricket screen should not wipe a player's styles.
/// </summary>
public class CricketProfileInput
{
    public CricketRole PrimaryRole { get; set; } = CricketRole.Batter;
    public BattingStyle? BattingStyle { get; set; }
    public BowlingArm? BowlingArm { get; set; }
    public BowlingType? BowlingType { get; set; }
    public int? BattingOrderPreference { get; set; }
}

public class CreatePlayerRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Position { get; set; }
    public int? JerseyNumber { get; set; }
    public CricketProfileInput? Cricket { get; set; }
}

public class UpdatePlayerRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Position { get; set; }
    public int? JerseyNumber { get; set; }
    public CricketProfileInput? Cricket { get; set; }
}

public class CreateTournamentRequest
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Defaults to Football so existing callers that omit it keep working.</summary>
    public Sport Sport { get; set; } = Sport.Football;
}

public class ApproveScheduleWithTournamentRequest
{
    public int TournamentId { get; set; }
    public TournamentSchedule Schedule { get; set; } = new();
}