namespace TournamentScheduler.Api.Models.Cricket;

public class CricketSquadSelection
{
    public int PlayerId { get; set; }
    public int TeamId { get; set; }
    public CricketSquadStatus SquadStatus { get; set; } = CricketSquadStatus.Playing;
    public bool IsCaptain { get; set; }
    public bool IsWicketKeeper { get; set; }
    public int? BattingOrder { get; set; }
}

/// <summary>
/// Sets the match up and settles the toss in one call, the way football's
/// <see cref="StartMatchRequest"/> does — there is nothing useful to do between the two.
/// </summary>
public class CricketSetupRequest
{
    /// <summary>A key from <see cref="CricketRulePresets"/>. Ignored when <see cref="Rules"/> is sent.</summary>
    public string? Preset { get; set; }

    /// <summary>The full rule set. Takes precedence over <see cref="Preset"/>.</summary>
    public CricketMatchRules? Rules { get; set; }

    public int TossWinnerTeamId { get; set; }
    public TossDecision TossDecision { get; set; }

    public List<CricketSquadSelection> Squad { get; set; } = new();
}

public class StartInningsRequest
{
    public int StrikerId { get; set; }
    public int NonStrikerId { get; set; }
    public int BowlerId { get; set; }

    /// <summary>Overrides the format's over limit, for a rain-reduced innings.</summary>
    public int? OversLimit { get; set; }
}

/// <summary>
/// One delivery as the scorer saw it. Everything is additive: a no-ball with four byes is
/// <c>IsNoBall = true, Byes = 4</c>, and a wide that ran two is <c>IsWide = true, WideExtraRuns = 2</c>.
/// </summary>
public class RecordBallRequest
{
    public int RunsOffBat { get; set; }

    public bool IsWide { get; set; }
    public bool IsNoBall { get; set; }
    public int WideExtraRuns { get; set; }

    public int Byes { get; set; }
    public int LegByes { get; set; }
    public int PenaltyRuns { get; set; }

    public DismissalType? WicketType { get; set; }
    public int? DismissedPlayerId { get; set; }
    public int? FielderId { get; set; }

    /// <summary>Whether the batters passed each other — only consulted on a catch.</summary>
    public bool BattersCrossed { get; set; }
}

public class NewBatterRequest
{
    public int PlayerId { get; set; }
}

public class NewBowlerRequest
{
    public int PlayerId { get; set; }
}

public class EndInningsRequest
{
    /// <summary>Only <see cref="InningsEndReason.Declared"/> and
    /// <see cref="InningsEndReason.Abandoned"/> are accepted; the rest the engine decides itself.</summary>
    public InningsEndReason Reason { get; set; } = InningsEndReason.Declared;
}

public class CompleteCricketMatchRequest
{
    /// <summary>Sign off a match the laws say is unfinished — an abandonment or a scorer override.</summary>
    public bool Force { get; set; }

    /// <summary>Award the match to a side outright, for a forfeit.</summary>
    public int? AwardWinnerTeamId { get; set; }

    /// <summary>Record it as abandoned with no result, worth a point to each side.</summary>
    public bool NoResult { get; set; }
}
