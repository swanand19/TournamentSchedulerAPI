using TournamentScheduler.Api.Models.Cricket;

namespace TournamentScheduler.Api.Services.Cricket;

/// <summary>
/// The scorecard, folded from the deliveries on the way out.
///
/// Nothing here is stored. A batter's runs are the runs off their bat, a bowler's figures are what
/// they were charged for, and both are recounted every time the card is asked for — so the card and
/// the ball-by-ball can never tell different stories.
/// </summary>
public partial class CricketScoringService
{
    public async Task<CricketScorecardDto?> BuildScorecardAsync(int matchId)
    {
        var match = await LoadAsync(matchId);
        if (match == null) return null;

        var card = new CricketScorecardDto
        {
            MatchId = match.Id,
            HomeTeamName = match.HomeTeamName,
            AwayTeamName = match.AwayTeamName,
            Status = match.Status.ToString(),
            ResultSummary = match.ResultSummary,
            TossSummary = match.TossWinnerTeamId is int tossWinner && match.TossDecision is { } decision
                ? $"{TeamName(match, tossWinner)} won the toss and chose to " +
                  (decision == TossDecision.Bat ? "bat" : "bowl")
                : null
        };

        foreach (var innings in match.Innings.OrderBy(i => i.InningsNumber))
            card.Innings.Add(BuildInningsCard(match, innings));

        return card;
    }

    private static InningsCard BuildInningsCard(CricketMatch match, CricketInnings innings)
    {
        var balls = innings.Balls.OrderBy(b => b.SequenceNumber).ToList();

        var card = new InningsCard
        {
            InningsId = innings.Id,
            InningsNumber = innings.InningsNumber,
            BattingTeamId = innings.BattingTeamId,
            BattingTeamName = innings.BattingTeamName,
            BowlingTeamName = innings.BowlingTeamName,
            Status = innings.Status.ToString(),
            EndReason = innings.EndReason?.ToString(),
            IsSuperOver = innings.IsSuperOver,
            IsFollowOn = innings.IsFollowOn,
            Runs = innings.Runs,
            Wickets = innings.Wickets,
            Overs = innings.OversText,
            OversLimit = innings.OversLimit,
            RunRate = innings.RunRate,
            Target = innings.Target,
            Wides = innings.Wides,
            NoBalls = innings.NoBalls,
            Byes = innings.Byes,
            LegByes = innings.LegByes,
            PenaltyRuns = innings.PenaltyRuns,
            ExtrasTotal = innings.ExtrasTotal
        };

        card.Batting = BuildBattingCard(match, innings, balls);
        card.Bowling = BuildBowlingCard(match, innings, balls);
        card.FallOfWickets = BuildFallOfWickets(match, innings, balls);

        var (partnershipRuns, partnershipBalls) = CurrentPartnership(balls);
        card.PartnershipRuns = partnershipRuns;
        card.PartnershipBalls = partnershipBalls;

        return card;
    }

    internal static List<BattingCardRow> BuildBattingCard(
        CricketMatch match, CricketInnings innings, List<CricketBall> balls)
    {
        // Order of arrival at the crease, which is the order a scorecard reads in — not the order
        // the XI was typed in.
        var arrival = new List<int>();
        void Seen(int? playerId)
        {
            if (playerId is int id && !arrival.Contains(id)) arrival.Add(id);
        }

        Seen(innings.OpeningStrikerId);
        Seen(innings.OpeningNonStrikerId);
        foreach (var ball in balls)
        {
            Seen(ball.StrikerId);
            Seen(ball.NonStrikerId);
        }

        var rows = new List<BattingCardRow>();

        foreach (var playerId in arrival)
        {
            var faced = balls.Where(b => b.StrikerId == playerId).ToList();
            var dismissal = balls.LastOrDefault(b => b.DismissedPlayerId == playerId);

            rows.Add(new BattingCardRow
            {
                PlayerId = playerId,
                PlayerName = NameOf(match, playerId),
                Position = rows.Count + 1,
                Runs = faced.Sum(b => b.RunsOffBat),
                // A wide is not a ball faced; a no-ball is.
                BallsFaced = faced.Count(b => !b.IsWide),
                Fours = faced.Count(b => b.IsFour),
                Sixes = faced.Count(b => b.IsSix),
                IsOut = dismissal?.CountsAsWicket ?? false,
                DismissalText = DescribeDismissal(match, dismissal),
                IsStriker = innings.StrikerId == playerId,
                IsNonStriker = innings.NonStrikerId == playerId,
                HasBatted = true
            });
        }

        // Then everyone still padded up, so the card shows a full XI.
        var yetToBat = match.Squad
            .Where(p => p.TeamId == innings.BattingTeamId
                        && p.SquadStatus == CricketSquadStatus.Playing
                        && !arrival.Contains(p.PlayerId))
            .OrderBy(p => p.BattingOrder ?? int.MaxValue)
            .ThenBy(p => p.PlayerName);

        foreach (var player in yetToBat)
        {
            rows.Add(new BattingCardRow
            {
                PlayerId = player.PlayerId,
                PlayerName = player.PlayerName,
                Position = rows.Count + 1,
                DismissalText = "did not bat",
                HasBatted = false
            });
        }

        return rows;
    }

    /// <summary>"c Sharma b Bumrah", "lbw b Ashwin", "run out (Jadeja)" — cricket's own shorthand.</summary>
    private static string DescribeDismissal(CricketMatch match, CricketBall? ball)
    {
        if (ball?.WicketType is not { } wicket) return "not out";

        var bowler = NameOf(match, ball.BowlerId);
        var fielder = ball.FielderId is int f ? NameOf(match, f) : null;

        return wicket switch
        {
            DismissalType.Bowled => $"b {bowler}",
            DismissalType.LBW => $"lbw b {bowler}",
            DismissalType.CaughtAndBowled => $"c & b {bowler}",
            DismissalType.Caught => ball.FielderId == ball.BowlerId
                ? $"c & b {bowler}"
                : $"c {fielder ?? "?"} b {bowler}",
            DismissalType.Stumped => $"st {fielder ?? "?"} b {bowler}",
            DismissalType.HitWicket => $"hit wicket b {bowler}",
            DismissalType.RunOut => fielder is null ? "run out"
                : ball.RunOutReceiverId is int r ? $"run out ({fielder}/{NameOf(match, r)})"
                : $"run out ({fielder})",
            DismissalType.RetiredHurt => "retired hurt",
            DismissalType.RetiredOut => "retired out",
            DismissalType.ObstructingTheField => "obstructing the field",
            DismissalType.HitBallTwice => "hit the ball twice",
            DismissalType.TimedOut => "timed out",
            _ => wicket.ToString()
        };
    }

    internal static List<BowlingCardRow> BuildBowlingCard(
        CricketMatch match, CricketInnings innings, List<CricketBall> balls)
    {
        return balls
            .GroupBy(b => b.BowlerId)
            .Select(g => new BowlingCardRow
            {
                PlayerId = g.Key,
                PlayerName = NameOf(match, g.Key),
                BallsPerOver = innings.BallsPerOver,
                LegalBalls = g.Count(b => b.IsLegalDelivery),
                Runs = g.Sum(b => b.RunsChargedToBowler),
                Wickets = g.Count(b => b.CreditsBowler),
                Wides = g.Sum(b => b.WideRuns),
                NoBalls = g.Sum(b => b.NoBallRuns),
                Maidens = CountMaidens(g, innings.BallsPerOver),
                // Bowlers read in the order they were brought on, as on a printed card.
                FirstBall = g.Min(b => b.SequenceNumber)
            })
            .OrderBy(r => r.FirstBall)
            .ToList();
    }

    /// <summary>
    /// A maiden is a completed over that cost the bowler nothing. Byes keep it alive, because they
    /// were never charged to the bowler in the first place.
    /// </summary>
    internal static int CountMaidens(IEnumerable<CricketBall> bowlerBalls, int ballsPerOver) =>
        bowlerBalls
            .GroupBy(b => b.OverNumber)
            .Count(over => over.Count(b => b.IsLegalDelivery) == ballsPerOver
                           && over.Sum(b => b.RunsChargedToBowler) == 0);

    private static List<FallOfWicket> BuildFallOfWickets(
        CricketMatch match, CricketInnings innings, List<CricketBall> balls)
    {
        var falls = new List<FallOfWicket>();
        var runs = 0;
        var legal = 0;

        foreach (var ball in balls)
        {
            runs += ball.TotalRuns;
            if (ball.IsLegalDelivery) legal++;

            if (!ball.CountsAsWicket) continue;

            falls.Add(new FallOfWicket
            {
                WicketNumber = falls.Count + 1,
                Runs = runs,
                Overs = $"{legal / innings.BallsPerOver}.{legal % innings.BallsPerOver}",
                PlayerId = ball.DismissedPlayerId ?? 0,
                PlayerName = ball.DismissedPlayerId is int id ? NameOf(match, id) : ""
            });
        }

        return falls;
    }

    /// <summary>What the pair currently in has put on, measured from the last wicket to fall.</summary>
    private static (int Runs, int Balls) CurrentPartnership(List<CricketBall> balls)
    {
        var runs = 0;
        var faced = 0;

        foreach (var ball in balls)
        {
            if (ball.CountsAsWicket)
            {
                runs = 0;
                faced = 0;
                continue;
            }

            runs += ball.TotalRuns;
            if (!ball.IsWide) faced++;
        }

        return (runs, faced);
    }
}
