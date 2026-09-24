namespace TournamentScheduler.Api.Models.Cricket;

/// <summary>
/// The read models behind the scorecard. Every number here is folded from
/// <see cref="CricketBall"/> on the way out — none of it is stored, so a card can never drift from
/// the deliveries it claims to summarise.
/// </summary>
public class BattingCardRow
{
    public int PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;

    /// <summary>Order of arrival at the crease, 1-based.</summary>
    public int Position { get; set; }

    public int Runs { get; set; }

    /// <summary>Legal deliveries plus no-balls. A wide is not a ball faced.</summary>
    public int BallsFaced { get; set; }

    public int Fours { get; set; }
    public int Sixes { get; set; }

    public double StrikeRate => BallsFaced == 0 ? 0 : Math.Round(Runs * 100.0 / BallsFaced, 2);

    public bool IsOut { get; set; }

    /// <summary>"c Sharma b Bumrah", "lbw b Ashwin", "not out", "retired hurt".</summary>
    public string DismissalText { get; set; } = "not out";

    public bool IsStriker { get; set; }
    public bool IsNonStriker { get; set; }

    /// <summary>Whether this row is a batter still at the crease, as opposed to one yet to bat.</summary>
    public bool HasBatted { get; set; }
}

public class BowlingCardRow
{
    public int PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;

    public int LegalBalls { get; set; }
    public int BallsPerOver { get; set; } = 6;

    /// <summary>"3.4" — completed overs and the balls into the current one.</summary>
    public string Overs => $"{LegalBalls / BallsPerOver}.{LegalBalls % BallsPerOver}";

    public int Maidens { get; set; }
    public int Runs { get; set; }
    public int Wickets { get; set; }
    public int Wides { get; set; }
    public int NoBalls { get; set; }

    public double Economy =>
        LegalBalls == 0 ? 0 : Math.Round(Runs * (double)BallsPerOver / LegalBalls, 2);

    /// <summary>The delivery this bowler's first spell began on — the card's running order.</summary>
    public int FirstBall { get; set; }
}

public class FallOfWicket
{
    public int WicketNumber { get; set; }
    public int Runs { get; set; }
    public string Overs { get; set; } = string.Empty;
    public int PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
}

public class InningsCard
{
    public int InningsId { get; set; }
    public int InningsNumber { get; set; }
    public string BattingTeamName { get; set; } = string.Empty;
    public string BowlingTeamName { get; set; } = string.Empty;
    public int BattingTeamId { get; set; }

    public string Status { get; set; } = string.Empty;
    public string? EndReason { get; set; }
    public bool IsSuperOver { get; set; }
    public bool IsFollowOn { get; set; }

    public int Runs { get; set; }
    public int Wickets { get; set; }
    public string Overs { get; set; } = string.Empty;
    public int? OversLimit { get; set; }
    public double RunRate { get; set; }
    public int? Target { get; set; }

    public int Wides { get; set; }
    public int NoBalls { get; set; }
    public int Byes { get; set; }
    public int LegByes { get; set; }
    public int PenaltyRuns { get; set; }
    public int ExtrasTotal { get; set; }

    public List<BattingCardRow> Batting { get; set; } = new();
    public List<BowlingCardRow> Bowling { get; set; } = new();
    public List<FallOfWicket> FallOfWickets { get; set; } = new();

    /// <summary>Runs added by the pair currently batting, and how many balls it has taken.</summary>
    public int PartnershipRuns { get; set; }
    public int PartnershipBalls { get; set; }
}

public class CricketScorecardDto
{
    public int MatchId { get; set; }
    public string HomeTeamName { get; set; } = string.Empty;
    public string AwayTeamName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? TossSummary { get; set; }
    public string? ResultSummary { get; set; }
    public List<InningsCard> Innings { get; set; } = new();
}
