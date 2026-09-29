namespace TournamentScheduler.Api.Models.Cricket;

/// <summary>
/// How many innings a side bats and what ends them. Everything else about a format — overs,
/// balls per over, players per side — is a number on <see cref="CricketMatchRules"/> rather than a
/// format of its own, so T5, T10, T20, ODI and The Hundred are all <see cref="LimitedOvers"/>.
/// </summary>
public enum CricketFormat
{
    /// <summary>One innings each, capped by overs. The overwhelming majority of local cricket.</summary>
    LimitedOvers = 0,

    /// <summary>Two innings each, every innings capped by overs. Aggregate scores decide it.</summary>
    MultiInningsLimitedOvers = 1,

    /// <summary>Two innings each with no over limit — declarations, the follow-on, and a draw.</summary>
    MultiInningsTimed = 2
}

public enum CricketMatchStatus
{
    NotStarted = 0,
    InProgress = 1,

    /// <summary>An innings has ended and the next has not yet been opened.</summary>
    InningsBreak = 2,

    SuperOver = 3,
    Completed = 4,
    Abandoned = 5
}

public enum TossDecision
{
    Bat = 0,
    Bowl = 1
}

/// <summary>What happens when the scores finish level.</summary>
public enum TieResolution
{
    AllowTie = 0,
    SuperOver = 1,
    Bowlout = 2,

    /// <summary>Most boundaries wins — used by some leagues instead of a second super over.</summary>
    BoundaryCount = 3
}

public enum BallType
{
    Leather = 0,
    Tennis = 1,
    Tape = 2,
    Other = 3
}

public enum PitchType
{
    Turf = 0,
    Matting = 1,
    Astroturf = 2,
    Concrete = 3,
    Other = 4
}

public enum InningsStatus
{
    NotStarted = 0,
    InProgress = 1,
    Completed = 2
}

public enum InningsEndReason
{
    OversComplete = 0,
    AllOut = 1,
    TargetChased = 2,
    Declared = 3,
    Abandoned = 4
}

/// <summary>
/// How a batter got out. Which of these credit the bowler with a wicket is decided by
/// <see cref="DismissalRules.CreditsBowler"/> rather than by the caller.
/// </summary>
public enum DismissalType
{
    Bowled = 0,
    Caught = 1,
    CaughtAndBowled = 2,
    LBW = 3,
    RunOut = 4,
    Stumped = 5,
    HitWicket = 6,

    /// <summary>Left the field injured. Not a dismissal — the batter may return.</summary>
    RetiredHurt = 7,

    /// <summary>Retired without the umpires' consent, or chose not to return. Counts as a wicket.</summary>
    RetiredOut = 8,

    ObstructingTheField = 9,
    HitBallTwice = 10,
    TimedOut = 11
}

public enum CricketSquadStatus
{
    Playing = 0,
    Bench = 1,
    Unavailable = 2
}

// ---------------------------------------------------------------------------
// Player attributes.
//
// What a cricketer *is*, as opposed to what they are doing in one match. Captain and
// wicket-keeper deliberately live nowhere near here: both are appointments for a given XI,
// so they sit on CricketMatchPlayer. A keeper who does not keep in one match, or a stand-in
// captain, cannot be expressed by a player-level flag.
// ---------------------------------------------------------------------------

public enum CricketRole
{
    Batter = 0,
    Bowler = 1,
    AllRounder = 2,
    WicketKeeper = 3,
    WicketKeeperBatter = 4
}

public enum BattingStyle
{
    RightHand = 0,
    LeftHand = 1
}

public enum BowlingArm
{
    Right = 0,
    Left = 1
}

/// <summary>
/// The kind of bowling, without the arm. Spin is split by the wrist rather than by the name of
/// the delivery, because the arm is what decides what the pair is called — see
/// <see cref="BowlingStyles.Describe"/>.
/// </summary>
public enum BowlingType
{
    Fast = 0,
    FastMedium = 1,
    MediumFast = 2,
    Medium = 3,

    /// <summary>Off break from a right-armer, slow left-arm orthodox from a left-armer.</summary>
    FingerSpin = 4,

    /// <summary>Leg break from a right-armer, the chinaman from a left-armer.</summary>
    WristSpin = 5
}

/// <summary>
/// Bowling style is stored as an arm plus a type rather than one flat enum, for the same reason
/// <see cref="DismissalRules"/> exists: the naming is a rule, and every layer should read it from
/// one place. The pair also makes nonsense unrepresentable — there is no way to record a
/// right-armer as slow left-arm orthodox.
/// </summary>
public static class BowlingStyles
{
    public static string Describe(BowlingArm arm, BowlingType type) => (arm, type) switch
    {
        (BowlingArm.Right, BowlingType.Fast) => "Right-arm fast",
        (BowlingArm.Right, BowlingType.FastMedium) => "Right-arm fast-medium",
        (BowlingArm.Right, BowlingType.MediumFast) => "Right-arm medium-fast",
        (BowlingArm.Right, BowlingType.Medium) => "Right-arm medium",
        (BowlingArm.Right, BowlingType.FingerSpin) => "Right-arm off break",
        (BowlingArm.Right, BowlingType.WristSpin) => "Right-arm leg break",

        (BowlingArm.Left, BowlingType.Fast) => "Left-arm fast",
        (BowlingArm.Left, BowlingType.FastMedium) => "Left-arm fast-medium",
        (BowlingArm.Left, BowlingType.MediumFast) => "Left-arm medium-fast",
        (BowlingArm.Left, BowlingType.Medium) => "Left-arm medium",
        (BowlingArm.Left, BowlingType.FingerSpin) => "Slow left-arm orthodox",
        (BowlingArm.Left, BowlingType.WristSpin) => "Left-arm wrist spin (chinaman)",

        _ => $"{arm}-arm {type}"
    };

    /// <summary>Null when either half is missing — a player who has not said how they bowl.</summary>
    public static string? Describe(BowlingArm? arm, BowlingType? type) =>
        arm.HasValue && type.HasValue ? Describe(arm.Value, type.Value) : null;

    public static bool IsSpin(BowlingType type) =>
        type is BowlingType.FingerSpin or BowlingType.WristSpin;
}

public static class CricketRoles
{
    public static string Describe(CricketRole role) => role switch
    {
        CricketRole.Batter => "Batter",
        CricketRole.Bowler => "Bowler",
        CricketRole.AllRounder => "All-rounder",
        CricketRole.WicketKeeper => "Wicket-keeper",
        CricketRole.WicketKeeperBatter => "Wicket-keeper batter",
        _ => role.ToString()
    };

    /// <summary>Whether a bowling style is worth asking for. A pure batter is not asked.</summary>
    public static bool Bowls(CricketRole role) =>
        role is CricketRole.Bowler or CricketRole.AllRounder;

    /// <summary>Who may be nominated as the keeper for an XI.</summary>
    public static bool Keeps(CricketRole role) =>
        role is CricketRole.WicketKeeper or CricketRole.WicketKeeperBatter;
}

/// <summary>
/// The wicket-credit and fielder rules, kept beside the enum so every layer agrees rather than each
/// re-deciding. Getting this wrong is what makes a bowling leaderboard quietly incorrect.
/// </summary>
public static class DismissalRules
{
    /// <summary>A run-out is not the bowler's wicket, however it looks on the scorecard.</summary>
    public static bool CreditsBowler(DismissalType type) => type switch
    {
        DismissalType.Bowled => true,
        DismissalType.Caught => true,
        DismissalType.CaughtAndBowled => true,
        DismissalType.LBW => true,
        DismissalType.Stumped => true,
        DismissalType.HitWicket => true,
        _ => false
    };

    /// <summary>Whether a fielder should be recorded — the catcher, the thrower, or the keeper.</summary>
    public static bool TakesFielder(DismissalType type) => type switch
    {
        DismissalType.Caught => true,
        DismissalType.RunOut => true,
        DismissalType.Stumped => true,
        _ => false
    };

    /// <summary>
    /// Most dismissals only befall the batter facing. A run-out or obstruction can take either,
    /// and either batter can retire.
    /// </summary>
    public static bool CanDismissNonStriker(DismissalType type) => type switch
    {
        DismissalType.RunOut => true,
        DismissalType.ObstructingTheField => true,
        DismissalType.RetiredHurt => true,
        DismissalType.RetiredOut => true,
        _ => false
    };

    /// <summary>Retiring hurt costs the side nothing; the batter can come back later in the innings.</summary>
    public static bool CountsAsWicket(DismissalType type) => type != DismissalType.RetiredHurt;

    /// <summary>
    /// The ball is not in play in the usual sense on a wide, so only these can happen off one.
    /// </summary>
    public static bool PossibleOffWide(DismissalType type) => type switch
    {
        DismissalType.Stumped => true,
        DismissalType.RunOut => true,
        DismissalType.HitWicket => true,
        DismissalType.ObstructingTheField => true,
        _ => false
    };

    /// <summary>A free hit protects the batter from everything the bowler could earn.</summary>
    public static bool PossibleOnFreeHit(DismissalType type) => type switch
    {
        DismissalType.RunOut => true,
        DismissalType.Stumped => true,
        DismissalType.ObstructingTheField => true,
        DismissalType.HitBallTwice => true,
        _ => false
    };
}
