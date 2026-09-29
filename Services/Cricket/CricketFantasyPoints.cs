using TournamentScheduler.Api.Models.Cricket;

namespace TournamentScheduler.Api.Services.Cricket;

/// <summary>
/// Fantasy-style MVP points, per player per match.
///
/// Modelled on the classic T20 fantasy-cricket table (the Dream11 system most scoring apps borrow):
/// every contribution earns points, so a 30 off 12 balls, a tight four-over spell and a direct-hit
/// run-out can all be weighed against each other. The values sit in one place so a league can tune
/// them without touching the arithmetic.
///
/// Run-outs follow this league's own rule: a direct hit is worth the full run-out; when two fielders
/// are involved the thrower takes the credit and the receiver takes none.
/// </summary>
public static class CricketFantasyPoints
{
    // --- Batting ------------------------------------------------------
    public const int PerRun = 1;
    public const int FourBonus = 1;
    public const int SixBonus = 2;
    public const int ThirtyBonus = 4;
    public const int FiftyBonus = 8;
    public const int HundredBonus = 16;
    /// <summary>Out for nothing — not charged to specialist bowlers.</summary>
    public const int Duck = -2;

    // --- Bowling ------------------------------------------------------
    /// <summary>Every wicket credited to the bowler; run-outs are the fielder's.</summary>
    public const int Wicket = 25;
    public const int BowledOrLbwBonus = 8;
    public const int ThreeWicketBonus = 4;
    public const int FourWicketBonus = 8;
    public const int FiveWicketBonus = 16;
    public const int Maiden = 12;
    public const int DotBall = 1;

    // --- Fielding -----------------------------------------------------
    public const int Catch = 8;
    public const int ThreeCatchBonus = 4;
    public const int Stumping = 12;
    public const int RunOutDirectHit = 12;
    /// <summary>The thrower in a two-fielder run-out. The receiver gets nothing.</summary>
    public const int RunOutThrower = 6;

    public const int PlayingXi = 4;

    // --- Rate bonuses, T20 bands ---------------------------------------
    public const int StrikeRateMinimumBalls = 10;
    public const int EconomyMinimumOvers = 2;

    /// <summary>Points for a batting strike rate, once a batter has faced enough balls.</summary>
    public static int StrikeRatePoints(double strikeRate) => strikeRate switch
    {
        > 170 => 6,
        > 150 => 4,
        >= 130 => 2,
        >= 70.01 => 0,
        >= 60 => -2,
        >= 50 => -4,
        _ => -6
    };

    /// <summary>Points for an economy rate (runs per over), once a bowler has bowled enough.</summary>
    public static int EconomyPoints(double economy) => economy switch
    {
        < 5 => 6,
        < 6 => 4,
        <= 7 => 2,
        < 10 => 0,
        <= 11 => -2,
        <= 12 => -4,
        _ => -6
    };

    /// <summary>The whole table, for the stats page to print next to the MVP board.</summary>
    public static List<(string Category, string Item, int Points)> Describe() => new()
    {
        ("Batting", "Per run", PerRun),
        ("Batting", "Boundary bonus (per four)", FourBonus),
        ("Batting", "Six bonus (per six)", SixBonus),
        ("Batting", "30-run bonus", ThirtyBonus),
        ("Batting", "Half-century bonus (replaces 30)", FiftyBonus),
        ("Batting", "Century bonus (replaces 50)", HundredBonus),
        ("Batting", "Duck (not for specialist bowlers)", Duck),
        ("Bowling", "Wicket (excluding run-outs)", Wicket),
        ("Bowling", "Bonus for bowled or LBW", BowledOrLbwBonus),
        ("Bowling", "3-wicket haul", ThreeWicketBonus),
        ("Bowling", "4-wicket haul (replaces 3)", FourWicketBonus),
        ("Bowling", "5-wicket haul (replaces 4)", FiveWicketBonus),
        ("Bowling", "Maiden over", Maiden),
        ("Bowling", "Dot ball", DotBall),
        ("Fielding", "Catch", Catch),
        ("Fielding", "3-catch bonus", ThreeCatchBonus),
        ("Fielding", "Stumping", Stumping),
        ("Fielding", "Run-out, direct hit", RunOutDirectHit),
        ("Fielding", "Run-out, thrower (receiver gets 0)", RunOutThrower),
        ("Other", "In the playing XI", PlayingXi),
        ("Strike rate (min 10 balls)", "Above 170", 6),
        ("Strike rate (min 10 balls)", "150.01 – 170", 4),
        ("Strike rate (min 10 balls)", "130 – 150", 2),
        ("Strike rate (min 10 balls)", "60 – 70", -2),
        ("Strike rate (min 10 balls)", "50 – 59.99", -4),
        ("Strike rate (min 10 balls)", "Below 50", -6),
        ("Economy (min 2 overs)", "Below 5", 6),
        ("Economy (min 2 overs)", "5 – 5.99", 4),
        ("Economy (min 2 overs)", "6 – 7", 2),
        ("Economy (min 2 overs)", "10 – 11", -2),
        ("Economy (min 2 overs)", "11.01 – 12", -4),
        ("Economy (min 2 overs)", "Above 12", -6),
    };

    /// <summary>One player's points from one match, by discipline.</summary>
    public sealed class Breakdown
    {
        public int Batting { get; set; }
        public int Bowling { get; set; }
        public int Fielding { get; set; }
        public int Playing { get; set; }
        public int Total => Batting + Bowling + Fielding + Playing;
    }

    /// <summary>
    /// Every player's points from a match. Super overs are left out, as they are from every other
    /// statistic. <paramref name="roles"/> is each player's primary role, used for the duck and
    /// strike-rate exemptions a specialist bowler gets.
    /// </summary>
    public static Dictionary<int, Breakdown> ForMatch(CricketMatch match, IReadOnlyDictionary<int, CricketRole> roles)
    {
        var points = new Dictionary<int, Breakdown>();
        Breakdown For(int playerId) =>
            points.TryGetValue(playerId, out var b) ? b : points[playerId] = new Breakdown();

        bool IsBowler(int playerId) => roles.TryGetValue(playerId, out var role) && role == CricketRole.Bowler;

        var innings = match.Innings.Where(i => !i.IsSuperOver).ToList();
        if (innings.All(i => i.Balls.Count == 0)) return points;

        foreach (var member in match.Squad.Where(p => p.SquadStatus == CricketSquadStatus.Playing))
            For(member.PlayerId).Playing += PlayingXi;

        var catches = new Dictionary<int, int>();

        foreach (var inn in innings)
        {
            var balls = inn.Balls.OrderBy(b => b.SequenceNumber).ToList();

            foreach (var row in CricketScoringService.BuildBattingCard(match, inn, balls).Where(r => r.HasBatted))
            {
                var p = row.Runs * PerRun + row.Fours * FourBonus + row.Sixes * SixBonus;
                p += row.Runs >= 100 ? HundredBonus : row.Runs >= 50 ? FiftyBonus : row.Runs >= 30 ? ThirtyBonus : 0;

                if (!IsBowler(row.PlayerId))
                {
                    if (row.IsOut && row.Runs == 0) p += Duck;
                    if (row.BallsFaced >= StrikeRateMinimumBalls)
                        p += StrikeRatePoints(row.Runs * 100.0 / row.BallsFaced);
                }

                For(row.PlayerId).Batting += p;
            }

            foreach (var row in CricketScoringService.BuildBowlingCard(match, inn, balls))
            {
                var p = row.Wickets * Wicket + row.Maidens * Maiden;

                p += balls.Count(b => b.BowlerId == row.PlayerId
                                      && b.WicketType is DismissalType.Bowled or DismissalType.LBW) * BowledOrLbwBonus;
                p += row.Wickets >= 5 ? FiveWicketBonus : row.Wickets == 4 ? FourWicketBonus : row.Wickets == 3 ? ThreeWicketBonus : 0;
                p += balls.Count(b => b.BowlerId == row.PlayerId && b.IsLegalDelivery && b.IsDotForBowler) * DotBall;

                if (row.LegalBalls >= EconomyMinimumOvers * inn.BallsPerOver)
                    p += EconomyPoints(row.Runs * (double)inn.BallsPerOver / row.LegalBalls);

                For(row.PlayerId).Bowling += p;
            }

            foreach (var ball in balls.Where(b => b.WicketType.HasValue))
            {
                switch (ball.WicketType)
                {
                    case DismissalType.Caught when ball.FielderId is int catcher:
                        For(catcher).Fielding += Catch;
                        catches[catcher] = catches.GetValueOrDefault(catcher) + 1;
                        break;
                    case DismissalType.CaughtAndBowled:
                        For(ball.BowlerId).Fielding += Catch;
                        catches[ball.BowlerId] = catches.GetValueOrDefault(ball.BowlerId) + 1;
                        break;
                    case DismissalType.Stumped when ball.FielderId is int keeper:
                        For(keeper).Fielding += Stumping;
                        break;
                    case DismissalType.RunOut when ball.FielderId is int fielder:
                        For(fielder).Fielding += ball.IsDirectHit ? RunOutDirectHit : RunOutThrower;
                        break;
                }
            }
        }

        foreach (var (playerId, count) in catches)
            if (count >= 3) For(playerId).Fielding += ThreeCatchBonus;

        return points;
    }
}
