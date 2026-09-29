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
    /// <summary>The catcher, the keeper, or on a run-out the fielder who made it (the thrower).</summary>
    public int? FielderId { get; set; }

    /// <summary>Run-outs only: the fielder hit the stumps directly.</summary>
    public bool IsDirectHit { get; set; }

    /// <summary>Run-outs only, when not a direct hit: who took the throw and broke the wicket.</summary>
    public int? RunOutReceiverId { get; set; }

    /// <summary>Whether the batters passed each other — only consulted on a catch.</summary>
    public bool BattersCrossed { get; set; }
}

/// <summary>Overs lost to rain or delay, taken off the innings being played.</summary>
public class ReduceOversRequest
{
    /// <summary>The innings' new total, in overs. Must be fewer than now and not below overs bowled.</summary>
    public int NewOversLimit { get; set; }
}

public class NewBatterRequest
{
    public int PlayerId { get; set; }

    /// <summary>
    /// Whether the incoming batter faces the next ball. Null keeps the end the engine worked out
    /// (the one left empty); true or false puts the new batter on or off strike regardless.
    /// </summary>
    public bool? OnStrike { get; set; }
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
