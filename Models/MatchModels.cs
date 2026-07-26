namespace TournamentScheduler.Api.Models;

public enum MatchStatus
{
    NotStarted = 0,
    InProgress = 1,
    Paused = 2,
    Completed = 3,
    PenaltyShootout = 4
}

public enum SquadStatus
{
    Starting = 0,
    Bench = 1,
    Unavailable = 2,
    SubstitutedOff = 3,
    SentOff = 4
}

public enum MatchEventType
{
    Goal = 0,
    YellowCard = 1,
    RedCard = 2,
    SubstitutionIn = 3,
    SubstitutionOut = 4,
    HalfStart = 5,
    HalfEnd = 6,
    ExtraTimeStart = 7,
    ExtraTimeAdded = 8,
    ClockPaused = 9,
    ClockResumed = 10,
    MatchCompleted = 11,
    PenaltyShootoutStarted = 12,
    PenaltyKick = 13,
    MatchAbandoned = 14
}

public class Match
{
    public int Id { get; set; }

    public int TournamentId { get; set; }
    public Tournament? Tournament { get; set; }

    public int SavedFixtureId { get; set; }
    public SavedFixture? SavedFixture { get; set; }

    public string GroupName { get; set; } = string.Empty;
    public int MatchNumber { get; set; }

    public int? HomeTeamId { get; set; }
    public Team? HomeTeam { get; set; }
    public int? AwayTeamId { get; set; }
    public Team? AwayTeam { get; set; }

    public string HomeTeamName { get; set; } = string.Empty;
    public string AwayTeamName { get; set; } = string.Empty;

    public MatchStatus Status { get; set; } = MatchStatus.NotStarted;

    public int? MaxPlayersPerSide { get; set; }
    public int? MinutesPerHalf { get; set; }
    public bool ExtraTimeAllowed { get; set; }
    public bool DrawAllowed { get; set; }
    public int? MaxSubstitutions { get; set; }
    public bool RollingSubsAllowed { get; set; }

    public int CurrentMinute { get; set; }
    public int CurrentHalf { get; set; } = 1;
    public bool IsClockRunning { get; set; }

    public int HomeScore { get; set; }
    public int AwayScore { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public long PausedDurationMs { get; set; }
    public DateTime? PausedAt { get; set; }
    public int ExtraMinutesAddedThisHalf { get; set; }
    public DateTime? HalfStartedAt { get; set; }
    public int? ExtraTimeMinutesPerHalf { get; set; }

    public bool IsPenaltyShootout { get; set; }
    public int? PenaltyTakersPerSide { get; set; }
    public int? PenaltyWinnerTeamId { get; set; }
    public int? FinalWhistleMinute { get; set; }
    public int? PenaltyHomeScore { get; set; }
    public int? PenaltyAwayScore { get; set; }
    public int? ForfeitWinnerTeamId { get; set; }

    public List<PenaltyKick> PenaltyKicks { get; set; } = new();
    public List<MatchPlayer> MatchPlayers { get; set; } = new();
    public List<MatchEvent> Events { get; set; } = new();
}

public class PenaltyKick
{
    public int Id { get; set; }
    public int MatchId { get; set; }
    public Match? Match { get; set; }
    public int TeamId { get; set; }
    public int? PlayerId { get; set; }
    public int RoundNumber { get; set; }
    public bool IsSuddenDeath { get; set; }
    public bool Scored { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class StartPenaltiesRequest
{
    public int TakersPerSide { get; set; } = 5;
}

public class RecordPenaltyKickRequest
{
    public int TeamId { get; set; }
    public int? PlayerId { get; set; }
    public bool Scored { get; set; }
}

public class EndPenaltiesRequest
{
    public int WinningTeamId { get; set; }
}

public class MatchPlayer
{
    public int Id { get; set; }

    public int MatchId { get; set; }
    public Match? Match { get; set; }

    public int PlayerId { get; set; }
    public Player? Player { get; set; }

    public int TeamId { get; set; }
    public Team? Team { get; set; }

    public SquadStatus SquadStatus { get; set; }
}

public class MatchEvent
{
    public int Id { get; set; }
    public int MatchId { get; set; }
    public Match? Match { get; set; }
    public MatchEventType EventType { get; set; }
    public int MinuteOfMatch { get; set; }
    public int? TeamId { get; set; }
    public int? PlayerId { get; set; }
    public int? RelatedPlayerId { get; set; } // e.g. incoming player on a substitution
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int? StoppageMinute { get; set; }
    public bool? PenaltyScored { get; set; }
    public int? PenaltyHomeScoreAfter { get; set; }
    public int? PenaltyAwayScoreAfter { get; set; }
}

public class MatchSquadSelection
{
    public int PlayerId { get; set; }
    public int TeamId { get; set; }
    public SquadStatus SquadStatus { get; set; }
}

public class StartMatchRequest
{
    public int MaxPlayersPerSide { get; set; }
    public int MinutesPerHalf { get; set; }
    public bool ExtraTimeAllowed { get; set; }
    public bool DrawAllowed { get; set; }
    public int MaxSubstitutions { get; set; }
    public bool RollingSubsAllowed { get; set; }
    public int? ExtraTimeMinutesPerHalf { get; set; }
    public List<MatchSquadSelection> Squad { get; set; } = new();
}

public class RecordEventRequest
{
    public MatchEventType EventType { get; set; }
    public int? TeamId { get; set; }
    public int? PlayerId { get; set; }
    public int? RelatedPlayerId { get; set; } // e.g. incoming sub player
}

public class AddTimeRequest
{
    public int Minutes { get; set; }
}