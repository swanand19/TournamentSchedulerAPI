using TournamentScheduler.Api.Models.Cricket;
using TournamentScheduler.Api.Models.Watch;
using TournamentScheduler.Api.Services.Cricket;

namespace TournamentScheduler.Api.Services.Watch;

/// <summary>
/// A cricket match told ball by ball, built fresh from the ledger each time — so an undo or a
/// correction is reflected at once and nothing has to be kept in step. The moments that matter are
/// lines of their own: wickets, boundaries, fifties and hundreds (batters, partnerships, the team),
/// a five-wicket haul, and a summary at the end of every over.
/// </summary>
/// <remarks>
/// Non-ball events are placed by what they are rather than by clock: an innings starting goes before
/// its first ball, its ending after its last. Only corrections and rain (overs reduced), which happen
/// mid-innings, are placed among the balls by when they were recorded. Batters coming in and bowlers
/// coming on are left out: every ball already names them.
/// </remarks>
internal static class CricketActivity
{
    private static readonly HashSet<CricketMatchEventType> AfterInnings =
    [
        CricketMatchEventType.InningsEnded, CricketMatchEventType.Declared, CricketMatchEventType.FollowOnEnforced,
        CricketMatchEventType.SuperOverStarted, CricketMatchEventType.MatchCompleted, CricketMatchEventType.MatchAbandoned
    ];

    private static readonly HashSet<CricketMatchEventType> DuringInnings =
        [CricketMatchEventType.OversReduced, CricketMatchEventType.PlayerReplaced];

    /// <summary>The whole match, oldest first.</summary>
    public static List<ActivityItemDto> Build(CricketMatch match, IReadOnlyList<CricketMatchEvent> events)
    {
        var feed = new ActivityFeed();
        var placed = new HashSet<int>();

        foreach (var toss in events.Where(e => e.EventType == CricketMatchEventType.TossCompleted))
        {
            feed.Add(ActivityKind.Toss, toss.Description, teamId: toss.TeamId);
            placed.Add(toss.Id);
        }

        foreach (var innings in match.Innings.OrderBy(i => i.InningsNumber))
        {
            var mine = events.Where(e => e.InningsNumber == innings.InningsNumber).OrderBy(e => e.Id).ToList();

            foreach (var e in mine.Where(e => e.EventType == CricketMatchEventType.InningsStarted))
            {
                feed.Add(ActivityKind.Innings, e.Description, teamId: e.TeamId, innings: innings.InningsNumber);
                placed.Add(e.Id);
            }

            var during = new Queue<CricketMatchEvent>(mine.Where(e => DuringInnings.Contains(e.EventType)).OrderBy(e => e.CreatedAt).ThenBy(e => e.Id));
            new InningsWalk(match, innings, feed).Run(during, placed);
            foreach (var e in during)
            {
                Note(feed, e, innings.InningsNumber);
                placed.Add(e.Id);
            }

            foreach (var e in mine.Where(e => AfterInnings.Contains(e.EventType)))
            {
                feed.Add(Kind(e.EventType), e.Description, teamId: e.TeamId, innings: innings.InningsNumber);
                placed.Add(e.Id);
            }
        }

        // An abandonment or result with no innings to hang it on (a forfeit, rain before the start).
        foreach (var e in events.Where(e => !placed.Contains(e.Id) && (AfterInnings.Contains(e.EventType) || DuringInnings.Contains(e.EventType))).OrderBy(e => e.Id))
            feed.Add(Kind(e.EventType), e.Description, teamId: e.TeamId, innings: e.InningsNumber);

        return feed.Items;
    }

    private static ActivityKind Kind(CricketMatchEventType type) => type switch
    {
        CricketMatchEventType.MatchCompleted or CricketMatchEventType.MatchAbandoned => ActivityKind.Result,
        CricketMatchEventType.OversReduced or CricketMatchEventType.PlayerReplaced => ActivityKind.Note,
        _ => ActivityKind.Innings
    };

    private static void Note(ActivityFeed feed, CricketMatchEvent e, int innings) =>
        feed.Add(ActivityKind.Note, e.Description, teamId: e.TeamId, innings: innings);

    /// <summary>One innings' deliveries, keeping the running figures the milestones and over summaries need.</summary>
    private sealed class InningsWalk(CricketMatch match, CricketInnings innings, ActivityFeed feed)
    {
        private sealed class Batter { public int Runs, Balls, Fours, Sixes; }
        private sealed class Bowler { public int Legal, Runs, Wickets, Maidens; }

        private readonly Dictionary<int, Batter> _batters = new();
        private readonly Dictionary<int, Bowler> _bowlers = new();
        private int _runs, _wickets, _legal;
        private int _overRuns, _overCharged;
        private int _partnershipRuns, _partnershipBalls;

        private string Name(int playerId) => CricketScoringService.NameOf(match, playerId);
        private Batter BatterOf(int id) => _batters.TryGetValue(id, out var b) ? b : _batters[id] = new Batter();
        private Bowler BowlerOf(int id) => _bowlers.TryGetValue(id, out var b) ? b : _bowlers[id] = new Bowler();
        private int Bpo => innings.BallsPerOver;

        public void Run(Queue<CricketMatchEvent> during, HashSet<int> placed)
        {
            foreach (var ball in innings.Balls.OrderBy(b => b.SequenceNumber))
            {
                while (during.Count > 0 && during.Peek().CreatedAt < ball.CreatedAt)
                {
                    var e = during.Dequeue();
                    Note(feed, e, innings.InningsNumber);
                    placed.Add(e.Id);
                }
                Ball(ball);
            }
        }

        private void Ball(CricketBall ball)
        {
            var striker = BatterOf(ball.StrikerId);
            var bowler = BowlerOf(ball.BowlerId);
            var runsBefore = striker.Runs;
            var teamBefore = _runs;
            var partnershipBefore = _partnershipRuns;

            if (!ball.IsWide)
            {
                striker.Balls++;
                striker.Runs += ball.RunsOffBat;
                if (ball.IsFour) striker.Fours++;
                if (ball.IsSix) striker.Sixes++;
            }
            if (ball.IsLegalDelivery)
            {
                _legal++;
                bowler.Legal++;
                _partnershipBalls++;
            }
            _runs += ball.TotalRuns;
            _overRuns += ball.TotalRuns;
            _overCharged += ball.RunsChargedToBowler;
            _partnershipRuns += ball.TotalRuns;
            bowler.Runs += ball.RunsChargedToBowler;
            if (ball.CountsAsWicket) _wickets++;
            if (ball.CreditsBowler) bowler.Wickets++;

            feed.Add(BallKind(ball), $"{Name(ball.BowlerId)} to {Name(ball.StrikerId)}, {What(ball)}",
                detail: BallDetail(ball),
                marker: $"{ball.OverNumber - 1}.{ball.BallInOver}",
                chip: CricketScoringService.ToBallSummary(ball).Display,
                teamId: innings.BattingTeamId, innings: innings.InningsNumber);

            if (ball.WicketType != null && !ball.CountsAsWicket && ball.DismissedPlayerId is int retired)
                feed.Add(ActivityKind.Note, $"{Name(retired)} retired hurt.", teamId: innings.BattingTeamId, innings: innings.InningsNumber);

            Milestones(ball, striker, runsBefore, teamBefore, partnershipBefore);

            if (ball.CountsAsWicket)
            {
                _partnershipRuns = 0;
                _partnershipBalls = 0;
            }

            if (ball.IsLegalDelivery && ball.BallInOver == Bpo) OverEnd(ball, bowler);
        }

        private static ActivityKind BallKind(CricketBall ball) =>
            ball.CountsAsWicket ? ActivityKind.Wicket
            : ball.IsSix ? ActivityKind.Six
            : ball.IsFour ? ActivityKind.Four
            : !ball.IsLegalDelivery || ball.Byes > 0 || ball.LegByes > 0 || ball.PenaltyRuns > 0 ? ActivityKind.Extra
            : ball.RunsOffBat == 0 ? ActivityKind.Dot
            : ActivityKind.Run;

        private static string Runs(int n, string one = "run", string many = "runs") => $"{n} {(n == 1 ? one : many)}";

        /// <summary>"FOUR", "1 run", "wide, 3 runs", "no ball, 2 off the bat", "2 leg byes", "OUT".</summary>
        private static string What(CricketBall ball)
        {
            if (ball.CountsAsWicket) return "OUT";

            var parts = new List<string>();
            if (ball.IsWide) parts.Add(ball.WideRuns > 1 ? $"wide, {Runs(ball.WideRuns)}" : "wide");
            if (ball.IsNoBall) parts.Add("no ball");
            if (ball.IsSix) parts.Add("SIX");
            else if (ball.IsFour) parts.Add("FOUR");
            else if (ball.RunsOffBat > 0) parts.Add(ball.IsNoBall ? $"{ball.RunsOffBat} off the bat" : Runs(ball.RunsOffBat));
            if (ball.Byes > 0) parts.Add(Runs(ball.Byes, "bye", "byes"));
            if (ball.LegByes > 0) parts.Add(Runs(ball.LegByes, "leg bye", "leg byes"));
            if (ball.PenaltyRuns > 0) parts.Add($"{Runs(ball.PenaltyRuns, "penalty run", "penalty runs")}");
            return parts.Count > 0 ? string.Join(", ", parts) : "no run";
        }

        /// <summary>The dismissal in scorecard shorthand with the batter's score, and the free hit.</summary>
        private string? BallDetail(CricketBall ball)
        {
            var parts = new List<string>();
            if (ball.CountsAsWicket && ball.DismissedPlayerId is int outId)
            {
                var b = BatterOf(outId);
                parts.Add($"{Name(outId)} {CricketScoringService.DescribeDismissal(match, ball)} {b.Runs} ({b.Balls})");
                if (ball.TotalRuns > 0) parts.Add($"{Runs(ball.TotalRuns)} before the wicket");
            }
            if (ball.IsFreeHit) parts.Add("Free hit");
            return parts.Count > 0 ? string.Join(" · ", parts) : null;
        }

        private void Milestones(CricketBall ball, Batter striker, int runsBefore, int teamBefore, int partnershipBefore)
        {
            foreach (var m in Crossed(runsBefore, striker.Runs))
                feed.Add(ActivityKind.Milestone, $"{m} for {Name(ball.StrikerId)}",
                    detail: $"Off {striker.Balls} balls ({striker.Fours}×4, {striker.Sixes}×6)",
                    teamId: innings.BattingTeamId, innings: innings.InningsNumber);

            if (ball.NonStrikerId is int other)
                foreach (var m in Crossed(partnershipBefore, _partnershipRuns))
                    feed.Add(ActivityKind.Milestone, $"{m} partnership",
                        detail: $"{Name(ball.StrikerId)} and {Name(other)}, off {_partnershipBalls} balls",
                        teamId: innings.BattingTeamId, innings: innings.InningsNumber);

            if (!innings.IsSuperOver)
                foreach (var m in Crossed(teamBefore, _runs))
                    feed.Add(ActivityKind.Milestone, $"{m} up for {innings.BattingTeamName}",
                        detail: $"In {Overs(_legal)} overs",
                        teamId: innings.BattingTeamId, innings: innings.InningsNumber);

            if (ball.CreditsBowler && BowlerOf(ball.BowlerId) is { Wickets: 5 } five)
                feed.Add(ActivityKind.Milestone, $"Five wickets for {Name(ball.BowlerId)}",
                    detail: Figures(five),
                    teamId: innings.BowlingTeamId, innings: innings.InningsNumber);
        }

        /// <summary>Every multiple of 50 passed between two scores (a six can't pass two, but runs can be amended).</summary>
        private static IEnumerable<int> Crossed(int before, int after)
        {
            for (var m = (before / 50 + 1) * 50; m <= after; m += 50) yield return m;
        }

        private string Overs(int legal) => $"{legal / Bpo}.{legal % Bpo}";

        private string Figures(Bowler b) => $"{Overs(b.Legal)}-{b.Maidens}-{b.Runs}-{b.Wickets}";

        private void OverEnd(CricketBall ball, Bowler bowler)
        {
            if (_overCharged == 0) bowler.Maidens++;

            var score = $"{innings.BattingTeamName} {_runs}/{_wickets}";
            var chase = innings.Target is int target && target > _runs && innings.BallsAllowed is int allowed && allowed > _legal
                ? $" · Need {target - _runs} off {allowed - _legal}"
                : "";
            feed.Add(ActivityKind.OverEnd, $"End of over {ball.OverNumber} · {Runs(_overRuns)}",
                detail: $"{score}{chase} · {Name(ball.BowlerId)} {Figures(bowler)}",
                teamId: innings.BattingTeamId, innings: innings.InningsNumber);

            _overRuns = 0;
            _overCharged = 0;
        }
    }
}

/// <summary>Numbers the lines as they're added, oldest first: the id is the paging cursor.</summary>
internal sealed class ActivityFeed
{
    public List<ActivityItemDto> Items { get; } = new();

    public void Add(ActivityKind kind, string text, string? detail = null, string? marker = null, string? chip = null,
        int? teamId = null, int? innings = null) =>
        Items.Add(new ActivityItemDto
        {
            Id = Items.Count + 1,
            Kind = kind,
            Text = text,
            Detail = detail,
            Marker = marker,
            Chip = chip,
            TeamId = teamId,
            InningsNumber = innings
        });
}
