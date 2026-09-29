using TournamentScheduler.Api.Models.Cricket;

namespace TournamentScheduler.Api.Services.Cricket;

/// <summary>
/// Player of the match and the best batter, bowler and fielder of one completed match. Pure:
/// it reads the loaded match and each player's role, and weighs everyone on
/// <see cref="CricketFantasyPoints"/>, so a match's honours and the tournament's MVP race can
/// never disagree about what a performance was worth. Super overs are left out, as everywhere.
/// </summary>
public static class CricketMatchAwards
{
    private sealed class Line
    {
        public bool Batted, Bowled, NotOut;
        public int Runs, Balls, Fours, Sixes;
        public int Wickets, Conceded, LegalBalls, BallsPerOver = 6;
        public int Catches, Stumpings, RunOuts;
    }

    public static CricketMatchAwardsDto Build(CricketMatch match, IReadOnlyDictionary<int, CricketRole> roles)
    {
        var awards = new CricketMatchAwardsDto { MatchId = match.Id };
        if (match.Status != CricketMatchStatus.Completed) return awards;

        var points = CricketFantasyPoints.ForMatch(match, roles);
        if (points.Count == 0) return awards;

        var lines = BuildLines(match);
        Line LineOf(int id) => lines.GetValueOrDefault(id) ?? new Line();

        var players = match.Squad.ToDictionary(p => p.PlayerId);
        string Name(int id) => players.TryGetValue(id, out var p) ? p.PlayerName : $"Player {id}";
        int TeamOf(int id) => players.TryGetValue(id, out var p) ? p.TeamId : 0;
        string TeamName(int teamId) => teamId == match.HomeTeamId ? match.HomeTeamName
            : teamId == match.AwayTeamId ? match.AwayTeamName : "";

        CricketAward Award(int id, int value, string summary) => new()
        {
            PlayerId = id,
            PlayerName = Name(id),
            TeamId = TeamOf(id),
            TeamName = TeamName(TeamOf(id)),
            Points = value,
            Summary = summary
        };

        // Equal points go to the earlier name, so the same match always gives the same answer.
        int? Best(IEnumerable<int> candidates, Func<int, int> score, Func<int, double>? tieBreak = null) =>
            candidates
                .OrderByDescending(score)
                .ThenByDescending(id => tieBreak?.Invoke(id) ?? 0)
                .ThenBy(Name, StringComparer.OrdinalIgnoreCase)
                .Cast<int?>()
                .FirstOrDefault();

        // Player of the match comes from the side that won; a tie or no result has no such side.
        var pool = match.WinnerTeamId is int winner && points.Keys.Any(id => TeamOf(id) == winner)
            ? points.Keys.Where(id => TeamOf(id) == winner)
            : points.Keys;
        if (Best(pool, id => points[id].Total) is int potm)
            awards.PlayerOfMatch = Award(potm, points[potm].Total, AllRound(LineOf(potm)));

        var batters = points.Keys.Where(id => LineOf(id).Batted);
        if (Best(batters, id => points[id].Batting, id => LineOf(id).Runs) is int bat)
            awards.BestBatter = Award(bat, points[bat].Batting, Batting(LineOf(bat)));

        var bowlers = points.Keys.Where(id => LineOf(id).Bowled);
        if (Best(bowlers, id => points[id].Bowling, id => LineOf(id).Wickets * 1000 - LineOf(id).Conceded) is int bowl)
            awards.BestBowler = Award(bowl, points[bowl].Bowling, Bowling(LineOf(bowl)));

        var fielders = points.Keys.Where(id => points[id].Fielding > 0);
        if (Best(fielders, id => points[id].Fielding) is int field)
            awards.BestFielder = Award(field, points[field].Fielding, Fielding(LineOf(field)));

        return awards;
    }

    private static Dictionary<int, Line> BuildLines(CricketMatch match)
    {
        var lines = new Dictionary<int, Line>();
        Line For(int id) => lines.TryGetValue(id, out var l) ? l : lines[id] = new Line();

        foreach (var inn in match.Innings.Where(i => !i.IsSuperOver))
        {
            var balls = inn.Balls.OrderBy(b => b.SequenceNumber).ToList();

            foreach (var row in CricketScoringService.BuildBattingCard(match, inn, balls).Where(r => r.HasBatted))
            {
                var l = For(row.PlayerId);
                l.Batted = true;
                l.Runs += row.Runs;
                l.Balls += row.BallsFaced;
                l.Fours += row.Fours;
                l.Sixes += row.Sixes;
                l.NotOut = !row.IsOut;
            }

            foreach (var row in CricketScoringService.BuildBowlingCard(match, inn, balls))
            {
                var l = For(row.PlayerId);
                l.Bowled = true;
                l.Wickets += row.Wickets;
                l.Conceded += row.Runs;
                l.LegalBalls += row.LegalBalls;
                l.BallsPerOver = inn.BallsPerOver;
            }

            foreach (var ball in balls.Where(b => b.WicketType.HasValue))
            {
                switch (ball.WicketType)
                {
                    case DismissalType.Caught when ball.FielderId is int catcher:
                        For(catcher).Catches++;
                        break;
                    case DismissalType.CaughtAndBowled:
                        For(ball.BowlerId).Catches++;
                        break;
                    case DismissalType.Stumped when ball.FielderId is int keeper:
                        For(keeper).Stumpings++;
                        break;
                    case DismissalType.RunOut when ball.FielderId is int fielder:
                        For(fielder).RunOuts++;
                        break;
                }
            }
        }

        return lines;
    }

    private static string Batting(Line l)
    {
        var parts = new List<string> { $"{l.Runs}{(l.NotOut ? "*" : "")} ({l.Balls})" };
        if (l.Fours > 0) parts.Add($"{l.Fours}×4");
        if (l.Sixes > 0) parts.Add($"{l.Sixes}×6");
        return string.Join(" · ", parts);
    }

    private static string Bowling(Line l)
    {
        var overs = l.LegalBalls % l.BallsPerOver == 0
            ? $"{l.LegalBalls / l.BallsPerOver}"
            : $"{l.LegalBalls / l.BallsPerOver}.{l.LegalBalls % l.BallsPerOver}";
        return $"{l.Wickets}/{l.Conceded} ({overs} ov)";
    }

    private static string Fielding(Line l)
    {
        var parts = new List<string>();
        if (l.Catches > 0) parts.Add($"{l.Catches} {(l.Catches == 1 ? "catch" : "catches")}");
        if (l.Stumpings > 0) parts.Add($"{l.Stumpings} {(l.Stumpings == 1 ? "stumping" : "stumpings")}");
        if (l.RunOuts > 0) parts.Add($"{l.RunOuts} run-out{(l.RunOuts == 1 ? "" : "s")}");
        return string.Join(" · ", parts);
    }

    private static string AllRound(Line l)
    {
        var parts = new List<string>();
        if (l.Batted) parts.Add($"{l.Runs}{(l.NotOut ? "*" : "")} ({l.Balls})");
        if (l.Bowled) parts.Add($"{l.Wickets}/{l.Conceded}");
        if (l.Catches > 0) parts.Add($"{l.Catches} ct");
        if (l.Stumpings > 0) parts.Add($"{l.Stumpings} st");
        if (l.RunOuts > 0) parts.Add($"{l.RunOuts} run-out{(l.RunOuts == 1 ? "" : "s")}");
        return parts.Count > 0 ? string.Join(" · ", parts) : "In the XI";
    }
}
