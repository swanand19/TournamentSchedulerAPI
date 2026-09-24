namespace TournamentScheduler.Api.Models.Cricket;

/// <summary>
/// Everything a scoring screen needs, decided on the server.
///
/// The football match does the same thing through its <c>[NotMapped]</c> flow properties, and for
/// the same reason: cricket law is intricate enough that a client re-deriving "may this bowler bowl
/// now" will eventually disagree with the engine that enforces it. So the engine answers, and the
/// console only draws.
/// </summary>
public class CricketMatchStateDto
{
    public int Id { get; set; }
    public int TournamentId { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public int MatchNumber { get; set; }

    public int? HomeTeamId { get; set; }
    public int? AwayTeamId { get; set; }
    public string HomeTeamName { get; set; } = string.Empty;
    public string AwayTeamName { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;
    public CricketMatchRules Rules { get; set; } = new();

    public int? TossWinnerTeamId { get; set; }
    public string? TossDecision { get; set; }
    public int? TeamBattingFirstId { get; set; }
    public string? TossSummary { get; set; }

    public bool IsSetUp { get; set; }
    public int MaxInnings { get; set; }
    public int CurrentInningsNumber { get; set; }
    public bool FollowOnEnforced { get; set; }

    public int? WinnerTeamId { get; set; }
    public bool IsTie { get; set; }
    public bool IsDraw { get; set; }
    public bool IsNoResult { get; set; }
    public string? ResultSummary { get; set; }

    public List<CricketSquadMember> Squad { get; set; } = new();
    public List<InningsSummary> Innings { get; set; } = new();

    /// <summary>The innings being scored, or null between innings.</summary>
    public InningsLiveState? Current { get; set; }

    public CricketActions Actions { get; set; } = new();
}

public class CricketSquadMember
{
    public int PlayerId { get; set; }
    public int TeamId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public string SquadStatus { get; set; } = string.Empty;
    public bool IsCaptain { get; set; }
    public bool IsWicketKeeper { get; set; }
    public int? BattingOrder { get; set; }
}

public class InningsSummary
{
    public int Id { get; set; }
    public int InningsNumber { get; set; }
    public int BattingTeamId { get; set; }
    public string BattingTeamName { get; set; } = string.Empty;
    public string BowlingTeamName { get; set; } = string.Empty;
    public int Runs { get; set; }
    public int Wickets { get; set; }
    public string Overs { get; set; } = string.Empty;
    public int? OversLimit { get; set; }
    public int? Target { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? EndReason { get; set; }
    public bool IsSuperOver { get; set; }
    public bool IsFollowOn { get; set; }
    public string ScoreLine { get; set; } = string.Empty;
    public double RunRate { get; set; }
}

/// <summary>A batter or bowler as the console shows them: a name and the few numbers beside it.</summary>
public class CreaseBatter
{
    public int PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public int Runs { get; set; }
    public int BallsFaced { get; set; }
    public int Fours { get; set; }
    public int Sixes { get; set; }
    public bool OnStrike { get; set; }
}

public class CreaseBowler
{
    public int PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public string Overs { get; set; } = string.Empty;
    public int Maidens { get; set; }
    public int Runs { get; set; }
    public int Wickets { get; set; }
}

public class BallSummary
{
    public int SequenceNumber { get; set; }
    public int OverNumber { get; set; }

    /// <summary>"4", "W", "wd", "nb2", "1b" — the over as a scorer would write it out.</summary>
    public string Display { get; set; } = string.Empty;

    public int Runs { get; set; }
    public bool IsWicket { get; set; }
    public bool IsBoundary { get; set; }
    public bool IsExtra { get; set; }
    public bool IsFreeHit { get; set; }
}

public class InningsLiveState
{
    public int Id { get; set; }
    public int InningsNumber { get; set; }
    public int BattingTeamId { get; set; }
    public int BowlingTeamId { get; set; }
    public string BattingTeamName { get; set; } = string.Empty;

    public int Runs { get; set; }
    public int Wickets { get; set; }
    public string Overs { get; set; } = string.Empty;
    public int? OversLimit { get; set; }
    public string ScoreLine { get; set; } = string.Empty;
    public double RunRate { get; set; }

    public int? Target { get; set; }
    public int? RunsRequired { get; set; }
    public int? BallsRemaining { get; set; }
    public double? RequiredRunRate { get; set; }

    public int ExtrasTotal { get; set; }
    public bool FreeHitPending { get; set; }

    public CreaseBatter? Striker { get; set; }
    public CreaseBatter? NonStriker { get; set; }
    public CreaseBowler? Bowler { get; set; }
    public int? PreviousBowlerId { get; set; }

    /// <summary>The over in progress, oldest first.</summary>
    public List<BallSummary> ThisOver { get; set; } = new();

    public int PartnershipRuns { get; set; }
    public int PartnershipBalls { get; set; }
}

/// <summary>What the scorer may do right now, and with whom.</summary>
public class CricketActions
{
    public bool CanSetUp { get; set; }
    public bool CanStartInnings { get; set; }
    public bool CanRecordBall { get; set; }
    public bool CanUndo { get; set; }
    public bool NeedsBatter { get; set; }
    public bool NeedsBowler { get; set; }
    public bool CanDeclare { get; set; }
    public bool CanEndInnings { get; set; }
    public bool CanEnforceFollowOn { get; set; }
    public bool CanStartSuperOver { get; set; }
    public bool CanComplete { get; set; }

    /// <summary>
    /// Who bats next when an innings can be started. The alternation is not obvious — the follow-on
    /// makes a side bat twice running, and a super over is opened by whoever batted second — so the
    /// engine says, rather than the screen guessing.
    /// </summary>
    public int? NextBattingTeamId { get; set; }
    public int? NextBowlingTeamId { get; set; }

    /// <summary>Batters who may walk out next: in the XI, not at the crease, not already out.</summary>
    public List<CricketSquadMember> AvailableBatters { get; set; } = new();

    /// <summary>Bowlers who may take the next over: not the last one, and not at their limit.</summary>
    public List<CricketSquadMember> AvailableBowlers { get; set; } = new();

    /// <summary>Dismissals that are possible off the very next delivery, given the rules in force.</summary>
    public List<string> PossibleDismissals { get; set; } = new();
}
