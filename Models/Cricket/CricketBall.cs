using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace TournamentScheduler.Api.Models.Cricket;

/// <summary>
/// One delivery: the ledger the whole match is folded from.
///
/// Every total on <see cref="CricketInnings"/> — the score, the extras, who is on strike, whose
/// over it is — is recomputed from these rows rather than nudged in place, which is what makes undo
/// exact. Nothing here is a summary; if it cannot be read off a delivery it does not belong on one.
/// </summary>
public class CricketBall
{
    public int Id { get; set; }

    public int InningsId { get; set; }
    [JsonIgnore]
    public CricketInnings? Innings { get; set; }

    /// <summary>1-based within the innings, counting every delivery legal or not. The undo key.</summary>
    public int SequenceNumber { get; set; }

    /// <summary>1-based.</summary>
    public int OverNumber { get; set; }

    /// <summary>Which legal ball of the over this is, 1-based; repeated by a wide or no-ball.</summary>
    public int BallInOver { get; set; }

    public int BowlerId { get; set; }
    public int StrikerId { get; set; }

    /// <summary>Null when the last batter is carrying on alone and there is nobody at the other end.</summary>
    public int? NonStrikerId { get; set; }

    // --- Runs ---------------------------------------------------------

    /// <summary>Scored by the bat. Always 0 on a wide — the ball was not hittable.</summary>
    public int RunsOffBat { get; set; }

    public bool IsWide { get; set; }
    public bool IsNoBall { get; set; }

    /// <summary>Runs physically run on a wide, beyond its penalty. All of it counts as wides.</summary>
    public int WideExtraRuns { get; set; }

    public int Byes { get; set; }
    public int LegByes { get; set; }

    /// <summary>Award penalties (five runs for the ball hitting a helmet, and so on).</summary>
    public int PenaltyRuns { get; set; }

    /// <summary>
    /// The penalties as the rules stood when this ball was bowled. Snapshotted so the ledger stays
    /// self-contained — a scorecard must not change because someone edited the rules afterwards.
    /// </summary>
    public int WidePenalty { get; set; }
    public int NoBallPenalty { get; set; }

    /// <summary>Whether this delivery was itself a free hit.</summary>
    public bool IsFreeHit { get; set; }

    // --- Wicket -------------------------------------------------------

    public DismissalType? WicketType { get; set; }
    public int? DismissedPlayerId { get; set; }

    /// <summary>The catcher, the thrower, or the keeper — see <see cref="DismissalRules.TakesFielder"/>.</summary>
    public int? FielderId { get; set; }

    /// <summary>
    /// A run-out that hit the stumps straight from the fielder, with nobody else touching the ball.
    /// <see cref="FielderId"/> is then the only fielder involved.
    /// </summary>
    public bool IsDirectHit { get; set; }

    /// <summary>
    /// On a two-fielder run-out, whoever gathered the throw and broke the wicket. Recorded for the
    /// scorecard ("run out (Jadeja/Dhoni)") but credited with nothing: the run-out belongs to the
    /// thrower, who is <see cref="FielderId"/>.
    /// </summary>
    public int? RunOutReceiverId { get; set; }

    /// <summary>
    /// Whether the batters passed each other. Only matters on a catch, where the runs do not count
    /// but the crossing still decides which end the new batter walks to.
    /// </summary>
    public bool BattersCrossed { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // --- Derived ------------------------------------------------------
    // Computed here, the way Match exposes its flow state, so the engine, the scorecard and the
    // client never each write their own version of the arithmetic.

    [NotMapped]
    public bool IsLegalDelivery => !IsWide && !IsNoBall;

    [NotMapped]
    public int WideRuns => IsWide ? WidePenalty + WideExtraRuns : 0;

    [NotMapped]
    public int NoBallRuns => IsNoBall ? NoBallPenalty : 0;

    [NotMapped]
    public int ExtraRuns => WideRuns + NoBallRuns + Byes + LegByes + PenaltyRuns;

    [NotMapped]
    public int TotalRuns => RunsOffBat + ExtraRuns;

    /// <summary>Byes, leg byes and award penalties are not the bowler's fault; the rest is.</summary>
    [NotMapped]
    public int RunsChargedToBowler => RunsOffBat + WideRuns + NoBallRuns;

    /// <summary>Runs actually run or hit, which is what decides who ends up on strike.</summary>
    [NotMapped]
    public int RunsCompleted => RunsOffBat + Byes + LegByes + WideExtraRuns;

    [NotMapped]
    public bool IsFour => RunsOffBat == 4;

    [NotMapped]
    public bool IsSix => RunsOffBat == 6;

    [NotMapped]
    public bool CountsAsWicket =>
        WicketType.HasValue && DismissalRules.CountsAsWicket(WicketType.Value);

    [NotMapped]
    public bool CreditsBowler =>
        WicketType.HasValue && DismissalRules.CreditsBowler(WicketType.Value);

    /// <summary>A maiden counts deliveries the bowler was charged for, so a bye keeps it alive.</summary>
    [NotMapped]
    public bool IsDotForBowler => RunsChargedToBowler == 0;
}
