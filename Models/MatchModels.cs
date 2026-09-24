using System.ComponentModel.DataAnnotations.Schema;

namespace TournamentScheduler.Api.Models;

public enum MatchStatus
{
    NotStarted = 0,
    InProgress = 1,
    Paused = 2,
    Completed = 3,
    PenaltyShootout = 4
}

/// <summary>
/// Where the <em>current period</em> (half / extra-time half) stands. This is deliberately separate
/// from <see cref="MatchStatus"/>: a match can be "Paused" because the referee stopped play for an
/// injury (period still running) or because the period is over (half-time / full time). Conflating
/// the two is what let a shootout be offered the moment the second half kicked off.
/// </summary>
public enum PeriodState
{
    /// <summary>Match set up but the first half has not kicked off.</summary>
    NotStarted = 0,

    /// <summary>Clock running.</summary>
    InPlay = 1,

    /// <summary>Play stopped mid-period (injury, incident) — the period is NOT over.</summary>
    Stopped = 2,

    /// <summary>The referee has ended this period. Half-time, full time, or end of extra time.</summary>
    Ended = 3
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

    /// <summary>Whether the current half is running, stopped mid-play, or finished.</summary>
    public PeriodState PeriodState { get; set; } = PeriodState.NotStarted;

    public int? MaxPlayersPerSide { get; set; }

    /// <summary>Fewest players a side may be reduced to before the match is abandoned (IFAB Law 3 uses 7 for 11-a-side).</summary>
    public int? MinPlayersPerSide { get; set; }
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

    // -----------------------------------------------------------------
    // Derived match-flow state.
    //
    // These are computed here rather than in the UI so the client never has to
    // re-derive football rules — every screen and every endpoint agrees on what
    // is legal right now. They are [NotMapped] (never persisted) but do get
    // serialised, so the front end simply reads them.
    // -----------------------------------------------------------------

    [NotMapped]
    public bool ScoresLevel => HomeScore == AwayScore;

    /// <summary>
    /// Whether extra time can still be reached. Only when it is enabled, draws are not allowed,
    /// and the teams are level once regulation is over — matching how extra time works in a
    /// knockout tie.
    /// </summary>
    [NotMapped]
    public bool ExtraTimeApplicable =>
        ExtraTimeAllowed && !DrawAllowed && (CurrentHalf > 2 || ScoresLevel);

    /// <summary>Highest period number this match can reach: 2 for regulation, 4 when extra time applies.</summary>
    [NotMapped]
    public int MaxPeriods => ExtraTimeApplicable ? 4 : 2;

    [NotMapped]
    public bool IsFinalPeriod => CurrentHalf >= MaxPeriods;

    /// <summary>Regulation length of the period currently being played (extra-time halves are usually shorter).</summary>
    [NotMapped]
    public int CurrentPeriodMinutes =>
        CurrentHalf <= 2 ? (MinutesPerHalf ?? 0) : (ExtraTimeMinutesPerHalf ?? MinutesPerHalf ?? 0);

    /// <summary>Total regulation minutes of every period before the current one — the clock's starting offset.</summary>
    [NotMapped]
    public int PriorPeriodsMinutes
    {
        get
        {
            var total = 0;
            for (var h = 1; h < CurrentHalf; h++)
                total += h <= 2 ? (MinutesPerHalf ?? 0) : (ExtraTimeMinutesPerHalf ?? MinutesPerHalf ?? 0);
            return total;
        }
    }

    /// <summary>Stoppage time may not exceed half of the period's own length.</summary>
    [NotMapped]
    public int MaxStoppageThisPeriod => (int)Math.Ceiling(CurrentPeriodMinutes * 0.5);

    [NotMapped]
    public int StoppageRemainingThisPeriod => Math.Max(0, MaxStoppageThisPeriod - ExtraMinutesAddedThisHalf);

    [NotMapped]
    public bool IsLiveOrPaused => Status == MatchStatus.InProgress || Status == MatchStatus.Paused;

    /// <summary>The referee can blow for the end of the period whenever it is under way.</summary>
    [NotMapped]
    public bool CanEndPeriod => IsLiveOrPaused && PeriodState != PeriodState.Ended;

    /// <summary>A further period exists and the current one has been ended.</summary>
    [NotMapped]
    public bool CanStartNextPeriod =>
        IsLiveOrPaused && PeriodState == PeriodState.Ended && CurrentHalf < MaxPeriods;

    [NotMapped]
    public string CurrentPeriodLabel => PeriodLabel(CurrentHalf);

    [NotMapped]
    public string? NextPeriodLabel => CurrentHalf < MaxPeriods ? PeriodLabel(CurrentHalf + 1) : null;

    /// <summary>
    /// True only once the last period this match can play has actually been ended — the point at
    /// which the result stands and the match can be resolved.
    /// </summary>
    [NotMapped]
    public bool AwaitingResolution =>
        IsLiveOrPaused && PeriodState == PeriodState.Ended && CurrentHalf >= MaxPeriods;

    /// <summary>Penalties are the only way to separate the teams, and the match has reached that point.</summary>
    [NotMapped]
    public bool CanStartPenalties => AwaitingResolution && ScoresLevel && !DrawAllowed;

    /// <summary>Level with no draw allowed and no periods left — a shootout is required to finish.</summary>
    [NotMapped]
    public bool PenaltiesRequired => CanStartPenalties;

    /// <summary>The match can be signed off with the score as it stands (a winner, or a permitted draw).</summary>
    [NotMapped]
    public bool CanCompleteNormally => AwaitingResolution && (!ScoresLevel || DrawAllowed);

    public static string PeriodLabel(int period) => period switch
    {
        1 => "1st half",
        2 => "2nd half",
        3 => "Extra time 1st half",
        4 => "Extra time 2nd half",
        _ => $"Period {period}"
    };
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

    /// <summary>
    /// Whether this player was in the starting line-up. <see cref="SquadStatus"/> only records where
    /// a player stands *now*, and with rolling substitutions a starter who has been taken off is put
    /// back on the bench — indistinguishable from someone who never played. Appearances and minutes
    /// need the line-up as it was, so it is recorded at kick-off rather than inferred afterwards.
    /// </summary>
    public bool StartedMatch { get; set; }
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

    /// <summary>
    /// Fewest players a side may be reduced to (by red cards) before the match is abandoned.
    /// Defaults to 1 when not supplied, keeping the previous behaviour for small-sided games.
    /// </summary>
    public int? MinPlayersPerSide { get; set; }
    public int MinutesPerHalf { get; set; }
    public bool ExtraTimeAllowed { get; set; }
    public bool DrawAllowed { get; set; }
    public int MaxSubstitutions { get; set; }
    public bool RollingSubsAllowed { get; set; }
    public int? ExtraTimeMinutesPerHalf { get; set; }
    public List<MatchSquadSelection> Squad { get; set; } = new();
}

public class CompleteMatchRequest
{
    /// <summary>
    /// Sign off a match that normal rules say is not finished (level with draws disallowed, or a
    /// period still running). Used for abandonments and referee overrides.
    /// </summary>
    public bool Force { get; set; }

    /// <summary>Optional winner to record when force-completing an unresolved match.</summary>
    public int? AwardWinnerTeamId { get; set; }
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