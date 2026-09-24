using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace TournamentScheduler.Api.Models.Cricket;

/// <summary>
/// One side's turn at the crease.
///
/// The format numbers it needs — balls per over, the over limit, how many wickets end it — are
/// copied here at the start rather than read back off <see cref="CricketMatchRules"/> every time.
/// An innings is then self-describing, which is what lets a rain-reduced second innings and a
/// two-wicket super over run through exactly the same engine as a normal one.
///
/// Every total below is recomputed from <see cref="Balls"/>, never adjusted in place.
/// </summary>
public class CricketInnings
{
    public int Id { get; set; }

    public int CricketMatchId { get; set; }
    [JsonIgnore]
    public CricketMatch? Match { get; set; }

    /// <summary>1-based across the whole match, continuing past 4 into a super over.</summary>
    public int InningsNumber { get; set; }

    public int BattingTeamId { get; set; }
    public int BowlingTeamId { get; set; }

    /// <summary>Denormalised for the scorecard, as elsewhere in the match module.</summary>
    public string BattingTeamName { get; set; } = string.Empty;
    public string BowlingTeamName { get; set; } = string.Empty;

    /// <summary>Whether this is the side's first or second knock, for the multi-innings formats.</summary>
    public int BattingTeamInningsIndex { get; set; } = 1;

    public InningsStatus Status { get; set; } = InningsStatus.NotStarted;
    public InningsEndReason? EndReason { get; set; }

    // --- Format, fixed when the innings opens -------------------------

    public int BallsPerOver { get; set; } = 6;

    /// <summary>Null for an uncapped innings in a timed match.</summary>
    public int? OversLimit { get; set; }

    /// <summary>Usually one short of the side's strength; 2 in a super over.</summary>
    public int WicketsToEndInnings { get; set; } = 10;

    /// <summary>How many batters the side has, which is what decides when nobody is left to come in.</summary>
    public int BattingSideSize { get; set; } = 11;

    public int? MaxOversPerBowler { get; set; }

    // --- Totals, folded from the ledger -------------------------------

    public int Runs { get; set; }
    public int Wickets { get; set; }
    public int LegalBalls { get; set; }

    /// <summary>Every run credited as a wide: the penalties plus anything run off them.</summary>
    public int Wides { get; set; }

    /// <summary>No-ball penalties only. Runs off the bat from a no-ball belong to the batter.</summary>
    public int NoBalls { get; set; }

    public int Byes { get; set; }
    public int LegByes { get; set; }
    public int PenaltyRuns { get; set; }

    // --- Opening line-up ----------------------------------------------
    //
    // Kept permanently, not just until the first ball: the live pointers below are rebuilt by
    // replaying the ledger, and a replay of an empty ledger has to land somewhere. Without these,
    // undoing the first ball of an innings would lose the openers.

    public int? OpeningStrikerId { get; set; }
    public int? OpeningNonStrikerId { get; set; }
    public int? OpeningBowlerId { get; set; }

    // --- Live state ---------------------------------------------------

    /// <summary>Null when a wicket has fallen and the next batter has not walked out.</summary>
    public int? StrikerId { get; set; }
    public int? NonStrikerId { get; set; }

    /// <summary>Null between overs, until the next bowler is nominated.</summary>
    public int? CurrentBowlerId { get; set; }

    /// <summary>Who bowled the last completed over, and so may not bowl the next.</summary>
    public int? PreviousBowlerId { get; set; }

    /// <summary>Set by a no-ball (or a wide, in leagues that do that) and survives until a legal ball.</summary>
    public bool FreeHitPending { get; set; }

    /// <summary>
    /// The last batter is carrying on alone — the gully rule, switched on by
    /// <see cref="CricketMatchRules.LastManStanding"/> once everyone else has batted. One end stays
    /// empty, and with nobody to run with, the ends never change.
    /// </summary>
    public bool LoneBatter { get; set; }

    // --- Context ------------------------------------------------------

    /// <summary>Runs needed to win. Set on a chase; null when there is nothing to chase yet.</summary>
    public int? Target { get; set; }

    public bool IsFollowOn { get; set; }
    public bool IsSuperOver { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public List<CricketBall> Balls { get; set; } = new();

    // --- Derived ------------------------------------------------------

    [NotMapped]
    public int CompletedOvers => LegalBalls / BallsPerOver;

    [NotMapped]
    public int BallsIntoOver => LegalBalls % BallsPerOver;

    /// <summary>"14.3" — overs bowled, in cricket's own notation rather than as a decimal.</summary>
    [NotMapped]
    public string OversText => $"{CompletedOvers}.{BallsIntoOver}";

    [NotMapped]
    public int? BallsAllowed => OversLimit.HasValue ? OversLimit.Value * BallsPerOver : null;

    [NotMapped]
    public int? BallsRemaining =>
        BallsAllowed.HasValue ? Math.Max(0, BallsAllowed.Value - LegalBalls) : null;

    [NotMapped]
    public int ExtrasTotal => Wides + NoBalls + Byes + LegByes + PenaltyRuns;

    [NotMapped]
    public bool IsChase => Target.HasValue;

    [NotMapped]
    public int? RunsRequired => Target.HasValue ? Math.Max(0, Target.Value - Runs) : null;

    [NotMapped]
    public int WicketsRemaining => Math.Max(0, WicketsToEndInnings - Wickets);

    /// <summary>"148/6 (20.0)", or "148-6 declared" once it is over.</summary>
    [NotMapped]
    public string ScoreLine => $"{Runs}/{Wickets} ({OversText})";

    [NotMapped]
    public double RunRate => LegalBalls == 0 ? 0 : Math.Round(Runs * (double)BallsPerOver / LegalBalls, 2);

    /// <summary>What the chase now needs per over. Null when this innings is not a chase.</summary>
    [NotMapped]
    public double? RequiredRunRate =>
        Target.HasValue && BallsRemaining is > 0
            ? Math.Round((Target.Value - Runs) * (double)BallsPerOver / BallsRemaining.Value, 2)
            : null;

    [NotMapped]
    public bool IsComplete => Status == InningsStatus.Completed;

    /// <summary>
    /// A wicket has fallen and nobody has replaced the batter yet. Not true once the last batter is
    /// alone: there is nobody left to send, and that is the whole point of the rule.
    /// </summary>
    [NotMapped]
    public bool NeedsBatter =>
        Status == InningsStatus.InProgress
        && !LoneBatter
        && (StrikerId == null || NonStrikerId == null);

    /// <summary>Between overs. The next bowler must still be nominated.</summary>
    [NotMapped]
    public bool NeedsBowler => Status == InningsStatus.InProgress && CurrentBowlerId == null;

    [NotMapped]
    public bool CanRecordBall =>
        Status == InningsStatus.InProgress && !NeedsBatter && !NeedsBowler;
}
