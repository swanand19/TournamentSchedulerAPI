using System.Globalization;
using System.Text.RegularExpressions;
using TournamentScheduler.Api.Data.DataServices;
using TournamentScheduler.Api.Data.Queries;
using TournamentScheduler.Api.Models;
using TournamentScheduler.Api.Models.Cricket;

namespace TournamentScheduler.Api.Services.Cricket;

public interface ICricketStatsService
{
    /// <summary>Null when the tournament does not exist.</summary>
    Task<CricketTournamentStats?> BuildAsync(int tournamentId);
}

/// <summary>
/// The cricket stats page: points table and net run rate, leaderboards, MVP and records.
///
/// Folded from the ball-by-ball ledger every time, the same way the scorecard is — so a leaderboard
/// can never disagree with the card it was built from. Super overs are left out of player figures,
/// as scorers everywhere do; they only decide the match.
/// </summary>
public class CricketStatsService : ICricketStatsService
{
    // --- Points --------------------------------------------------------
    public const int PointsForWin = 2;
    public const int PointsForTie = 1;
    public const int PointsForNoResult = 1;
    public const int PointsForDraw = 1;

    // --- Who qualifies for a rate board ---------------------------------
    public const int AverageMinimumInnings = 2;
    public const int StrikeRateMinimumBalls = 20;
    public const double EconomyMinimumOvers = 3;
    public const int BowlingRateMinimumWickets = 3;

    private const int BoardSize = 10;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private readonly IDataService<Tournament> _tournaments;
    private readonly IDataService<CricketMatch> _matches;
    private readonly IDataService<PlayerCricketProfile> _profiles;

    public CricketStatsService(
        IDataService<Tournament> tournaments,
        IDataService<CricketMatch> matches,
        IDataService<PlayerCricketProfile> profiles)
    {
        _tournaments = tournaments;
        _matches = matches;
        _profiles = profiles;
    }

    public async Task<CricketTournamentStats?> BuildAsync(int tournamentId)
    {
        var tournament = await _tournaments.FirstOrDefaultAsync(t => t.Id == tournamentId);
        if (tournament == null) return null;

        var matches = await _matches.ListAsync(m => m.TournamentId == tournamentId, CricketMatchQueries.Full);

        var playerIds = matches.SelectMany(m => m.Squad).Select(p => p.PlayerId).Distinct().ToList();
        var roles = (await _profiles.ListAsync(p => playerIds.Contains(p.PlayerId)))
            .ToDictionary(p => p.PlayerId, p => p.PrimaryRole);

        return Build(tournament.Id, tournament.Name, matches, roles);
    }

    /// <summary>Pure: everything below works on already-loaded matches, which is what the tests use.</summary>
    public static CricketTournamentStats Build(
        int tournamentId, string tournamentName, List<CricketMatch> matches,
        IReadOnlyDictionary<int, CricketRole> roles)
    {
        var ledger = new Ledger(matches, roles);

        var completed = matches.Count(m => m.Status == CricketMatchStatus.Completed);
        var inProgress = matches.Count(m => m.Status is CricketMatchStatus.InProgress
            or CricketMatchStatus.InningsBreak or CricketMatchStatus.SuperOver);

        var stats = new CricketTournamentStats
        {
            TournamentId = tournamentId,
            TournamentName = tournamentName,
            Basis = inProgress > 0
                ? $"{completed} of {matches.Count} matches complete · player figures include the {inProgress} in progress"
                : $"{completed} of {matches.Count} matches complete",
            PointsTable = BuildPointsTable(matches),
            BattingBoards = ledger.BattingBoards(),
            BowlingBoards = ledger.BowlingBoards(),
            FieldingBoards = ledger.FieldingBoards(),
            Mvp = ledger.MvpBoard(),
            PlayersOfMatch = ledger.PlayersOfMatch(),
            PointsSystem = CricketFantasyPoints.Describe()
                .Select(r => new CricketPointsRule { Category = r.Category, Item = r.Item, Points = r.Points })
                .ToList(),
            Records = ledger.Records(),
            Notes = new List<string>
            {
                $"Points: {PointsForWin} for a win, {PointsForTie} for a tie or no result, 0 for a loss. Ties on points are split by net run rate, then head-to-head, then wins.",
                "Net run rate is runs scored per over less runs conceded per over. A side bowled out is counted as facing its full overs; in a rain-affected chase the side batting first is credited with one run less than the revised target from the chasing side's overs. No-results, awarded matches and super overs don't count.",
                "Player figures leave out super overs. Averages need at least 2 innings and a dismissal; strike rates 20 balls faced; economy 3 overs; bowling average and strike rate 3 wickets.",
                "MVP points follow the classic T20 fantasy table shown below. A direct-hit run-out earns the fielder the full run-out; in a two-fielder run-out the thrower is credited and the receiver is not.",
            }
        };

        stats.Summary = ledger.Summary(matches, completed, inProgress);
        return stats;
    }

    // -----------------------------------------------------------------
    // Points table and net run rate
    // -----------------------------------------------------------------

    private sealed class TeamLine
    {
        public int TeamId;
        public string Name = "";
        public int Played, Won, Lost, Tied, Drawn, NoResult, Points;
        public int RunsFor, RunsAgainst;
        public double OversFor, OversAgainst;
        public readonly List<(DateTime When, int Number, string Result)> Results = new();
        public readonly Dictionary<int, int> WinsAgainst = new();

        public double NetRunRate =>
            (OversFor > 0 ? RunsFor / OversFor : 0) - (OversAgainst > 0 ? RunsAgainst / OversAgainst : 0);
    }

    private static List<CricketPointsTableGroup> BuildPointsTable(List<CricketMatch> matches)
    {
        var groups = new List<CricketPointsTableGroup>();

        foreach (var group in matches.GroupBy(m => string.IsNullOrWhiteSpace(m.GroupName) ? "League" : m.GroupName)
                     .OrderBy(g => g.Key))
        {
            var lines = new Dictionary<int, TeamLine>();
            TeamLine Line(int id, string name) =>
                lines.TryGetValue(id, out var l) ? l : lines[id] = new TeamLine { TeamId = id, Name = name };

            foreach (var m in group)
            {
                if (m.HomeTeamId is int h) Line(h, m.HomeTeamName);
                if (m.AwayTeamId is int a) Line(a, m.AwayTeamName);
            }

            foreach (var m in group.Where(m => m.Status == CricketMatchStatus.Completed
                                               && m.HomeTeamId.HasValue && m.AwayTeamId.HasValue))
            {
                var home = Line(m.HomeTeamId!.Value, m.HomeTeamName);
                var away = Line(m.AwayTeamId!.Value, m.AwayTeamName);
                var when = m.CompletedAt ?? m.CreatedAt;
                home.Played++;
                away.Played++;

                if (m.IsNoResult)
                {
                    foreach (var t in new[] { home, away })
                    {
                        t.NoResult++;
                        t.Points += PointsForNoResult;
                        t.Results.Add((when, m.MatchNumber, "NR"));
                    }
                    continue;
                }

                if (m.IsTie || m.IsDraw)
                {
                    foreach (var t in new[] { home, away })
                    {
                        if (m.IsTie) { t.Tied++; t.Points += PointsForTie; }
                        else { t.Drawn++; t.Points += PointsForDraw; }
                        t.Results.Add((when, m.MatchNumber, m.IsTie ? "T" : "D"));
                    }
                }
                else if (m.WinnerTeamId is int winnerId)
                {
                    var winner = winnerId == home.TeamId ? home : away;
                    var loser = winner == home ? away : home;
                    winner.Won++;
                    winner.Points += PointsForWin;
                    loser.Lost++;
                    winner.WinsAgainst[loser.TeamId] = winner.WinsAgainst.GetValueOrDefault(loser.TeamId) + 1;
                    winner.Results.Add((when, m.MatchNumber, "W"));
                    loser.Results.Add((when, m.MatchNumber, "L"));
                }

                AddNetRunRate(m, lines);
            }

            var ordered = RankTeams(lines.Values.ToList());

            groups.Add(new CricketPointsTableGroup
            {
                GroupName = group.Key,
                Rows = ordered.Select((t, i) => new CricketStandingRow
                {
                    Rank = i + 1,
                    TeamId = t.TeamId,
                    TeamName = t.Name,
                    Played = t.Played,
                    Won = t.Won,
                    Lost = t.Lost,
                    Tied = t.Tied,
                    Drawn = t.Drawn,
                    NoResult = t.NoResult,
                    Points = t.Points,
                    NetRunRate = Math.Round(t.NetRunRate, 3),
                    RunsFor = t.RunsFor,
                    OversFor = OversText(t.OversFor),
                    RunsAgainst = t.RunsAgainst,
                    OversAgainst = OversText(t.OversAgainst),
                    Form = t.Results.OrderBy(r => r.When).ThenBy(r => r.Number).TakeLast(5).Select(r => r.Result).ToList()
                }).ToList()
            });
        }

        return groups;
    }

    /// <summary>
    /// The ICC's net run rate. Only matches with a result count — a no-result or a match awarded
    /// without being played adds nothing — and only one-innings limited-overs matches, where overs
    /// mean something.
    /// </summary>
    private static void AddNetRunRate(CricketMatch m, Dictionary<int, TeamLine> lines)
    {
        if (m.ForfeitWinnerTeamId.HasValue) return;
        if (m.Rules.InningsPerSide != 1 || m.Rules.OversPerInnings is null) return;

        var first = m.Innings.FirstOrDefault(i => !i.IsSuperOver && i.InningsNumber == 1);
        var second = m.Innings.FirstOrDefault(i => !i.IsSuperOver && i.InningsNumber == 2);
        if (first == null || second == null) return;
        if (!lines.TryGetValue(first.BattingTeamId, out var batFirst)) return;
        if (!lines.TryGetValue(second.BattingTeamId, out var batSecond)) return;

        var (firstRuns, firstOvers) = NrrInnings(first);
        var (secondRuns, secondOvers) = NrrInnings(second);

        // A revised (DLS) chase: team 1 is credited with one less than the target, from the overs
        // team 2 was given.
        if (DlsStandardEdition.Applies(m.Rules) && second.Target is int target && target != first.Runs + 1)
        {
            firstRuns = target - 1;
            firstOvers = AllottedBalls(second) / (double)second.BallsPerOver;
        }

        batFirst.RunsFor += firstRuns;
        batFirst.OversFor += firstOvers;
        batFirst.RunsAgainst += secondRuns;
        batFirst.OversAgainst += secondOvers;

        batSecond.RunsFor += secondRuns;
        batSecond.OversFor += secondOvers;
        batSecond.RunsAgainst += firstRuns;
        batSecond.OversAgainst += firstOvers;
    }

    /// <summary>Runs, and overs as a real number — the full allocation when the side was bowled out.</summary>
    private static (int Runs, double Overs) NrrInnings(CricketInnings innings)
    {
        var balls = innings.EndReason == InningsEndReason.AllOut ? AllottedBalls(innings) : innings.LegalBalls;
        return (innings.Runs, balls / (double)innings.BallsPerOver);
    }

    /// <summary>The innings' final allocation in balls, after any overs rain took away.</summary>
    private static int AllottedBalls(CricketInnings innings) =>
        innings.Interruptions.OrderBy(x => x.Id).LastOrDefault()?.BallsAllowedAfter
        ?? innings.BallsAllowed
        ?? innings.LegalBalls;

    /// <summary>Points, then net run rate, then head-to-head among the sides still level, then wins.</summary>
    private static List<TeamLine> RankTeams(List<TeamLine> teams)
    {
        var result = new List<TeamLine>();

        foreach (var level in teams
                     .GroupBy(t => (t.Points, Nrr: Math.Round(t.NetRunRate, 3)))
                     .OrderByDescending(g => g.Key.Points)
                     .ThenByDescending(g => g.Key.Nrr))
        {
            var members = level.ToList();
            var ids = members.Select(t => t.TeamId).ToHashSet();

            result.AddRange(members
                .OrderByDescending(t => t.WinsAgainst.Where(w => ids.Contains(w.Key)).Sum(w => w.Value))
                .ThenByDescending(t => t.Won)
                .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase));
        }

        return result;
    }

    private static string OversText(double overs)
    {
        var balls = (int)Math.Round(overs * 6);
        return $"{balls / 6}.{balls % 6}";
    }

    // -----------------------------------------------------------------
    // The ledger, folded into players
    // -----------------------------------------------------------------

    private sealed class Batting
    {
        public readonly HashSet<int> Matches = new();
        public int Innings, NotOuts, Runs, Balls, Fours, Sixes, Dots, Thirties, Fifties, Hundreds, Ducks;
        public int High = -1;
        public bool HighNotOut;
        public int Dismissals => Innings - NotOuts;
        public double? Average => Dismissals > 0 ? Runs / (double)Dismissals : null;
        public double StrikeRate => Balls > 0 ? Runs * 100.0 / Balls : 0;
    }

    private sealed class Bowling
    {
        public readonly HashSet<int> Matches = new();
        public int Innings, LegalBalls, Runs, Wickets, Maidens, Dots, Wides, NoBalls, Hauls3, Hauls5;
        public double Overs;
        public int BestWickets = -1, BestRuns;
        public double Economy => Overs > 0 ? Runs / Overs : 0;
        public double? Average => Wickets > 0 ? Runs / (double)Wickets : null;
        public double? StrikeRate => Wickets > 0 ? LegalBalls / (double)Wickets : null;
    }

    private sealed class Fielding
    {
        public int Catches, KeeperCatches, Stumpings, DirectHits, RunOutsThrown, RunOutsReceived;
        public int RunOuts => DirectHits + RunOutsThrown;
        public int Dismissals => Catches + Stumpings + RunOuts;
    }

    private sealed class MatchLine
    {
        public int Runs, Balls, Wickets, Conceded, Catches, RunOuts, Stumpings;
        public bool Batted, Bowled, NotOut;
    }

    private sealed record InningsRecord(
        int PlayerId, int MatchNumber, string Opponent, int Runs, int Balls, int Fours, int Sixes, bool NotOut);

    private sealed record SpellRecord(
        int PlayerId, int MatchNumber, string Opponent, int Wickets, int Runs, int LegalBalls, int BallsPerOver);

    private sealed record PartnershipRecord(
        string TeamName, string Opponent, int MatchNumber, int Wicket, int Runs, int Balls, string Batters);

    private sealed class Ledger
    {
        private readonly Dictionary<int, Batting> _bat = new();
        private readonly Dictionary<int, Bowling> _bowl = new();
        private readonly Dictionary<int, Fielding> _field = new();
        private readonly Dictionary<int, (string Name, int TeamId, string Team)> _who = new();
        private readonly Dictionary<int, HashSet<int>> _appearances = new();
        private readonly Dictionary<int, CricketFantasyPoints.Breakdown> _mvp = new();
        private readonly List<InningsRecord> _innings = new();
        private readonly List<SpellRecord> _spells = new();
        private readonly List<PartnershipRecord> _partnerships = new();
        private readonly List<CricketPlayerOfMatch> _playersOfMatch = new();
        private readonly List<(CricketInnings Innings, CricketMatch Match)> _teamInnings = new();
        private readonly List<CricketMatch> _decided = new();

        public int Fours, Sixes;

        public Ledger(List<CricketMatch> matches, IReadOnlyDictionary<int, CricketRole> roles)
        {
            foreach (var match in matches.OrderBy(m => m.MatchNumber))
            {
                var main = match.Innings.Where(i => !i.IsSuperOver).OrderBy(i => i.InningsNumber).ToList();
                if (main.All(i => i.Balls.Count == 0)) continue;

                foreach (var member in match.Squad)
                {
                    _who[member.PlayerId] = (member.PlayerName, member.TeamId,
                        member.TeamId == match.HomeTeamId ? match.HomeTeamName : match.AwayTeamName);

                    if (member.SquadStatus == CricketSquadStatus.Playing)
                    {
                        if (!_appearances.TryGetValue(member.PlayerId, out var set))
                            _appearances[member.PlayerId] = set = new HashSet<int>();
                        set.Add(match.Id);
                    }
                }

                var lines = new Dictionary<int, MatchLine>();
                MatchLine LineFor(int id) => lines.TryGetValue(id, out var l) ? l : lines[id] = new MatchLine();

                foreach (var inn in main)
                {
                    var balls = inn.Balls.OrderBy(b => b.SequenceNumber).ToList();
                    if (inn.Status != InningsStatus.NotStarted) _teamInnings.Add((inn, match));

                    FoldBatting(match, inn, balls, LineFor);
                    FoldBowling(match, inn, balls, LineFor);
                    FoldFielding(match, balls, LineFor);
                    FoldPartnerships(match, inn, balls);

                    Fours += balls.Count(b => b.IsFour);
                    Sixes += balls.Count(b => b.IsSix);
                }

                var points = CricketFantasyPoints.ForMatch(match, roles);
                foreach (var (playerId, p) in points)
                {
                    if (!_mvp.TryGetValue(playerId, out var total)) _mvp[playerId] = total = new CricketFantasyPoints.Breakdown();
                    total.Batting += p.Batting;
                    total.Bowling += p.Bowling;
                    total.Fielding += p.Fielding;
                    total.Playing += p.Playing;
                }

                if (match.Status == CricketMatchStatus.Completed)
                {
                    _decided.Add(match);
                    if (points.Count > 0) AddPlayerOfMatch(match, points, lines);
                }
            }
        }

        private string Opponent(CricketMatch match, int teamId) =>
            teamId == match.HomeTeamId ? match.AwayTeamName : match.HomeTeamName;

        private void FoldBatting(CricketMatch match, CricketInnings inn, List<CricketBall> balls,
            Func<int, MatchLine> lineFor)
        {
            foreach (var row in CricketScoringService.BuildBattingCard(match, inn, balls).Where(r => r.HasBatted))
            {
                if (!_bat.TryGetValue(row.PlayerId, out var b)) _bat[row.PlayerId] = b = new Batting();

                b.Matches.Add(match.Id);
                b.Innings++;
                if (!row.IsOut) b.NotOuts++;
                b.Runs += row.Runs;
                b.Balls += row.BallsFaced;
                b.Fours += row.Fours;
                b.Sixes += row.Sixes;
                b.Dots += balls.Count(x => x.StrikerId == row.PlayerId && !x.IsWide && x.RunsOffBat == 0);

                if (row.Runs >= 100) b.Hundreds++;
                else if (row.Runs >= 50) b.Fifties++;
                else if (row.Runs >= 30) b.Thirties++;
                if (row.IsOut && row.Runs == 0) b.Ducks++;

                if (row.Runs > b.High || (row.Runs == b.High && !row.IsOut))
                {
                    b.High = row.Runs;
                    b.HighNotOut = !row.IsOut;
                }

                _innings.Add(new InningsRecord(row.PlayerId, match.MatchNumber, Opponent(match, inn.BattingTeamId),
                    row.Runs, row.BallsFaced, row.Fours, row.Sixes, !row.IsOut));

                var line = lineFor(row.PlayerId);
                line.Batted = true;
                line.Runs += row.Runs;
                line.Balls += row.BallsFaced;
                line.NotOut = !row.IsOut;
            }
        }

        private void FoldBowling(CricketMatch match, CricketInnings inn, List<CricketBall> balls,
            Func<int, MatchLine> lineFor)
        {
            foreach (var row in CricketScoringService.BuildBowlingCard(match, inn, balls))
            {
                if (!_bowl.TryGetValue(row.PlayerId, out var b)) _bowl[row.PlayerId] = b = new Bowling();

                b.Matches.Add(match.Id);
                b.Innings++;
                b.LegalBalls += row.LegalBalls;
                b.Overs += row.LegalBalls / (double)inn.BallsPerOver;
                b.Runs += row.Runs;
                b.Wickets += row.Wickets;
                b.Maidens += row.Maidens;
                b.Wides += row.Wides;
                b.NoBalls += row.NoBalls;
                b.Dots += balls.Count(x => x.BowlerId == row.PlayerId && x.IsLegalDelivery && x.IsDotForBowler);
                if (row.Wickets >= 5) b.Hauls5++;
                else if (row.Wickets >= 3) b.Hauls3++;

                if (row.Wickets > b.BestWickets || (row.Wickets == b.BestWickets && row.Runs < b.BestRuns))
                {
                    b.BestWickets = row.Wickets;
                    b.BestRuns = row.Runs;
                }

                _spells.Add(new SpellRecord(row.PlayerId, match.MatchNumber, Opponent(match, inn.BowlingTeamId),
                    row.Wickets, row.Runs, row.LegalBalls, inn.BallsPerOver));

                var line = lineFor(row.PlayerId);
                line.Bowled = true;
                line.Wickets += row.Wickets;
                line.Conceded += row.Runs;
            }
        }

        private void FoldFielding(CricketMatch match, List<CricketBall> balls, Func<int, MatchLine> lineFor)
        {
            Fielding F(int id) => _field.TryGetValue(id, out var f) ? f : _field[id] = new Fielding();
            bool IsKeeper(int id) => match.Squad.Any(p => p.PlayerId == id && p.IsWicketKeeper);

            foreach (var ball in balls.Where(b => b.WicketType.HasValue))
            {
                switch (ball.WicketType)
                {
                    case DismissalType.Caught when ball.FielderId is int catcher:
                        F(catcher).Catches++;
                        if (IsKeeper(catcher)) F(catcher).KeeperCatches++;
                        lineFor(catcher).Catches++;
                        break;
                    case DismissalType.CaughtAndBowled:
                        F(ball.BowlerId).Catches++;
                        lineFor(ball.BowlerId).Catches++;
                        break;
                    case DismissalType.Stumped when ball.FielderId is int keeper:
                        F(keeper).Stumpings++;
                        lineFor(keeper).Stumpings++;
                        break;
                    case DismissalType.RunOut when ball.FielderId is int fielder:
                        if (ball.IsDirectHit) F(fielder).DirectHits++;
                        else F(fielder).RunOutsThrown++;
                        if (ball.RunOutReceiverId is int receiver) F(receiver).RunOutsReceived++;
                        lineFor(fielder).RunOuts++;
                        break;
                }
            }
        }

        /// <summary>Stands between wickets — any departure, a retirement included, ends one.</summary>
        private void FoldPartnerships(CricketMatch match, CricketInnings inn, List<CricketBall> balls)
        {
            var runs = 0;
            var faced = 0;
            var wicket = 1;
            int? a = inn.OpeningStrikerId, b = inn.OpeningNonStrikerId;

            void Close()
            {
                if (runs == 0 && faced == 0) return;
                var names = string.Join(" & ", new[] { a, b }.Where(x => x.HasValue)
                    .Select(x => _who.TryGetValue(x!.Value, out var w) ? w.Name : $"Player {x}"));
                _partnerships.Add(new PartnershipRecord(inn.BattingTeamName, inn.BowlingTeamName,
                    match.MatchNumber, wicket, runs, faced, names));
            }

            foreach (var ball in balls)
            {
                a = ball.StrikerId;
                b = ball.NonStrikerId;
                runs += ball.TotalRuns;
                if (!ball.IsWide) faced++;

                if (ball.DismissedPlayerId.HasValue)
                {
                    Close();
                    runs = faced = 0;
                    if (ball.CountsAsWicket) wicket++;
                }
            }

            Close();
        }

        private void AddPlayerOfMatch(CricketMatch match, Dictionary<int, CricketFantasyPoints.Breakdown> points,
            Dictionary<int, MatchLine> lines)
        {
            var (playerId, best) = points.OrderByDescending(p => p.Value.Total).First();
            var line = lines.GetValueOrDefault(playerId) ?? new MatchLine();

            var parts = new List<string>();
            if (line.Batted) parts.Add($"{line.Runs}{(line.NotOut ? "*" : "")} ({line.Balls})");
            if (line.Bowled) parts.Add($"{line.Wickets}/{line.Conceded}");
            if (line.Catches > 0) parts.Add($"{line.Catches} ct");
            if (line.RunOuts > 0) parts.Add($"{line.RunOuts} run-out{(line.RunOuts == 1 ? "" : "s")}");
            if (line.Stumpings > 0) parts.Add($"{line.Stumpings} st");

            _playersOfMatch.Add(new CricketPlayerOfMatch
            {
                MatchId = match.Id,
                MatchNumber = match.MatchNumber,
                Fixture = $"{match.HomeTeamName} v {match.AwayTeamName}",
                PlayerId = playerId,
                PlayerName = Name(playerId),
                TeamName = Team(playerId),
                Points = best.Total,
                Summary = string.Join(" · ", parts)
            });
        }

        private string Name(int id) => _who.TryGetValue(id, out var w) ? w.Name : $"Player {id}";
        private string Team(int id) => _who.TryGetValue(id, out var w) ? w.Team : "";
        private int TeamId(int id) => _who.TryGetValue(id, out var w) ? w.TeamId : 0;
        private int MatchesPlayed(int id) => _appearances.TryGetValue(id, out var s) ? s.Count : 0;

        // --- Boards ---------------------------------------------------

        private sealed record Entry(
            int PlayerId, double Primary, double Secondary, string Value,
            Dictionary<string, string> Detail, string? Note = null);

        /// <summary>
        /// Ranks entries on their primary figure, then the secondary one (lower is better), and
        /// gives equal entries equal rank the way a leaderboard does (1, 2, 2, 4).
        /// </summary>
        private CricketBoard Board(string key, string title, string valueLabel, IEnumerable<Entry> entries,
            bool higherIsBetter, List<string> columns, string? qualification = null, int take = BoardSize)
        {
            var sorted = (higherIsBetter
                    ? entries.OrderByDescending(e => e.Primary)
                    : entries.OrderBy(e => e.Primary))
                .ThenBy(e => e.Secondary)
                .ThenBy(e => Name(e.PlayerId), StringComparer.OrdinalIgnoreCase)
                .Take(take)
                .ToList();

            var rows = new List<CricketBoardRow>();
            for (var i = 0; i < sorted.Count; i++)
            {
                var e = sorted[i];
                var tiedWithPrevious = i > 0
                                       && sorted[i - 1].Primary.Equals(e.Primary)
                                       && sorted[i - 1].Secondary.Equals(e.Secondary);
                rows.Add(new CricketBoardRow
                {
                    Rank = tiedWithPrevious ? rows[^1].Rank : i + 1,
                    PlayerId = e.PlayerId,
                    PlayerName = Name(e.PlayerId),
                    TeamId = TeamId(e.PlayerId),
                    TeamName = Team(e.PlayerId),
                    Value = e.Value,
                    Detail = e.Detail,
                    Note = e.Note
                });
            }

            return new CricketBoard
            {
                Key = key,
                Title = title,
                ValueLabel = valueLabel,
                Qualification = qualification,
                Columns = columns,
                Rows = rows
            };
        }

        private static string F2(double? v) => v is double d ? d.ToString("0.00", Inv) : "—";
        private static string F1(double? v) => v is double d ? d.ToString("0.0", Inv) : "—";

        private static string HighScore(Batting b) => b.High < 0 ? "—" : $"{b.High}{(b.HighNotOut ? "*" : "")}";

        private static string OversOf(Bowling b)
        {
            var whole = (int)Math.Floor(b.Overs + 1e-9);
            var rest = (int)Math.Round((b.Overs - whole) * 6);
            return $"{whole}.{rest}";
        }

        public List<CricketBoard> BattingBoards()
        {
            var all = _bat.Where(p => p.Value.Innings > 0).ToList();

            Dictionary<string, string> Standard(int id, Batting b) => new()
            {
                ["Mat"] = MatchesPlayed(id).ToString(),
                ["Inns"] = b.Innings.ToString(),
                ["NO"] = b.NotOuts.ToString(),
                ["HS"] = HighScore(b),
                ["Avg"] = F2(b.Average),
                ["SR"] = F2(b.StrikeRate),
                ["4s"] = b.Fours.ToString(),
                ["6s"] = b.Sixes.ToString()
            };

            return new List<CricketBoard>
            {
                Board("mostRuns", "Most runs", "Runs",
                    all.Where(p => p.Value.Runs > 0)
                        .Select(p => new Entry(p.Key, p.Value.Runs, p.Value.Balls, p.Value.Runs.ToString(), Standard(p.Key, p.Value))),
                    true, new() { "Avg", "SR", "HS", "Inns", "NO", "4s", "6s", "Mat" }),

                Board("highestScore", "Highest score", "Runs",
                    _innings.Select(r => new Entry(r.PlayerId, r.Runs + (r.NotOut ? 0.5 : 0), r.Balls,
                        $"{r.Runs}{(r.NotOut ? "*" : "")}",
                        new()
                        {
                            ["Balls"] = r.Balls.ToString(),
                            ["4s"] = r.Fours.ToString(),
                            ["6s"] = r.Sixes.ToString(),
                            ["SR"] = r.Balls > 0 ? F2(r.Runs * 100.0 / r.Balls) : "—"
                        },
                        $"vs {r.Opponent} · M{r.MatchNumber}")),
                    true, new() { "Balls", "4s", "6s", "SR" }),

                Board("bestAverage", "Best batting average", "Avg",
                    all.Where(p => p.Value.Innings >= AverageMinimumInnings && p.Value.Dismissals > 0)
                        .Select(p => new Entry(p.Key, p.Value.Average!.Value, -p.Value.Runs, F2(p.Value.Average), Standard(p.Key, p.Value))),
                    true, new() { "Inns", "NO", "Runs", "HS", "SR" },
                    $"Minimum {AverageMinimumInnings} innings and one dismissal"),

                Board("bestStrikeRate", "Best strike rate", "SR",
                    all.Where(p => p.Value.Balls >= StrikeRateMinimumBalls)
                        .Select(p => new Entry(p.Key, p.Value.StrikeRate, -p.Value.Runs, F2(p.Value.StrikeRate),
                            new()
                            {
                                ["Runs"] = p.Value.Runs.ToString(),
                                ["Balls"] = p.Value.Balls.ToString(),
                                ["4s"] = p.Value.Fours.ToString(),
                                ["6s"] = p.Value.Sixes.ToString()
                            })),
                    true, new() { "Runs", "Balls", "4s", "6s" },
                    $"Minimum {StrikeRateMinimumBalls} balls faced"),

                Board("mostSixes", "Most sixes", "6s",
                    all.Where(p => p.Value.Sixes > 0)
                        .Select(p => new Entry(p.Key, p.Value.Sixes, p.Value.Balls, p.Value.Sixes.ToString(), Standard(p.Key, p.Value))),
                    true, new() { "Inns", "Runs", "4s", "SR" }),

                Board("mostFours", "Most fours", "4s",
                    all.Where(p => p.Value.Fours > 0)
                        .Select(p => new Entry(p.Key, p.Value.Fours, p.Value.Balls, p.Value.Fours.ToString(), Standard(p.Key, p.Value))),
                    true, new() { "Inns", "Runs", "6s", "SR" }),

                Board("mostFifties", "Most 50+ scores", "50+",
                    all.Where(p => p.Value.Fifties + p.Value.Hundreds > 0)
                        .Select(p => new Entry(p.Key, p.Value.Fifties + p.Value.Hundreds, -p.Value.Runs,
                            (p.Value.Fifties + p.Value.Hundreds).ToString(),
                            new()
                            {
                                ["100s"] = p.Value.Hundreds.ToString(),
                                ["50s"] = p.Value.Fifties.ToString(),
                                ["30s"] = p.Value.Thirties.ToString(),
                                ["Runs"] = p.Value.Runs.ToString(),
                                ["HS"] = HighScore(p.Value)
                            })),
                    true, new() { "100s", "50s", "30s", "Runs", "HS" }),
            };
        }

        public List<CricketBoard> BowlingBoards()
        {
            var all = _bowl.Where(p => p.Value.LegalBalls > 0).ToList();

            Dictionary<string, string> Standard(int id, Bowling b) => new()
            {
                ["Mat"] = MatchesPlayed(id).ToString(),
                ["Inns"] = b.Innings.ToString(),
                ["Overs"] = OversOf(b),
                ["Runs"] = b.Runs.ToString(),
                ["Wkts"] = b.Wickets.ToString(),
                ["Avg"] = F2(b.Average),
                ["Econ"] = F2(b.Economy),
                ["SR"] = F1(b.StrikeRate),
                ["BBI"] = b.BestWickets < 0 ? "—" : $"{b.BestWickets}/{b.BestRuns}",
                ["Dots"] = b.Dots.ToString(),
                ["Mdns"] = b.Maidens.ToString()
            };

            return new List<CricketBoard>
            {
                Board("mostWickets", "Most wickets", "Wkts",
                    all.Where(p => p.Value.Wickets > 0)
                        .Select(p => new Entry(p.Key, p.Value.Wickets, p.Value.Runs, p.Value.Wickets.ToString(), Standard(p.Key, p.Value))),
                    true, new() { "Econ", "Avg", "BBI", "Overs", "Runs", "Mat" }),

                Board("bestFigures", "Best bowling figures", "Figures",
                    _spells.Where(s => s.Wickets > 0).Select(s => new Entry(s.PlayerId, s.Wickets, s.Runs, $"{s.Wickets}/{s.Runs}",
                        new()
                        {
                            ["Overs"] = $"{s.LegalBalls / s.BallsPerOver}.{s.LegalBalls % s.BallsPerOver}",
                            ["Econ"] = F2(s.Runs * (double)s.BallsPerOver / s.LegalBalls)
                        },
                        $"vs {s.Opponent} · M{s.MatchNumber}")),
                    true, new() { "Overs", "Econ" }),

                Board("bestEconomy", "Best economy", "Econ",
                    all.Where(p => p.Value.Overs >= EconomyMinimumOvers)
                        .Select(p => new Entry(p.Key, p.Value.Economy, -p.Value.Overs, F2(p.Value.Economy), Standard(p.Key, p.Value))),
                    false, new() { "Overs", "Runs", "Wkts", "Dots" },
                    $"Minimum {EconomyMinimumOvers:0} overs"),

                Board("bestBowlingAverage", "Best bowling average", "Avg",
                    all.Where(p => p.Value.Wickets >= BowlingRateMinimumWickets)
                        .Select(p => new Entry(p.Key, p.Value.Average!.Value, -p.Value.Wickets, F2(p.Value.Average), Standard(p.Key, p.Value))),
                    false, new() { "Wkts", "Runs", "Overs", "Econ" },
                    $"Minimum {BowlingRateMinimumWickets} wickets"),

                Board("bestBowlingStrikeRate", "Best bowling strike rate", "Balls/wkt",
                    all.Where(p => p.Value.Wickets >= BowlingRateMinimumWickets)
                        .Select(p => new Entry(p.Key, p.Value.StrikeRate!.Value, -p.Value.Wickets, F1(p.Value.StrikeRate), Standard(p.Key, p.Value))),
                    false, new() { "Wkts", "Overs", "Avg", "Econ" },
                    $"Minimum {BowlingRateMinimumWickets} wickets"),

                Board("mostDots", "Most dot balls", "Dots",
                    all.Where(p => p.Value.Dots > 0)
                        .Select(p =>
                        {
                            var d = Standard(p.Key, p.Value);
                            d["Dot %"] = F1(p.Value.Dots * 100.0 / p.Value.LegalBalls);
                            return new Entry(p.Key, p.Value.Dots, p.Value.LegalBalls, p.Value.Dots.ToString(), d);
                        }),
                    true, new() { "Overs", "Dot %", "Econ" }),

                Board("mostMaidens", "Most maidens", "Mdns",
                    all.Where(p => p.Value.Maidens > 0)
                        .Select(p => new Entry(p.Key, p.Value.Maidens, p.Value.Runs, p.Value.Maidens.ToString(), Standard(p.Key, p.Value))),
                    true, new() { "Overs", "Runs", "Econ" }),
            };
        }

        public List<CricketBoard> FieldingBoards()
        {
            var all = _field.ToList();

            Dictionary<string, string> Standard(int id, Fielding f) => new()
            {
                ["Mat"] = MatchesPlayed(id).ToString(),
                ["Ct"] = f.Catches.ToString(),
                ["As keeper"] = f.KeeperCatches.ToString(),
                ["St"] = f.Stumpings.ToString(),
                ["RO"] = f.RunOuts.ToString(),
                ["Direct hits"] = f.DirectHits.ToString(),
                ["As thrower"] = f.RunOutsThrown.ToString(),
                ["Received"] = f.RunOutsReceived.ToString()
            };

            return new List<CricketBoard>
            {
                Board("mostCatches", "Most catches", "Ct",
                    all.Where(p => p.Value.Catches > 0)
                        .Select(p => new Entry(p.Key, p.Value.Catches, 0, p.Value.Catches.ToString(), Standard(p.Key, p.Value))),
                    true, new() { "As keeper", "Mat" }),

                Board("mostRunOuts", "Most run-outs", "RO",
                    all.Where(p => p.Value.RunOuts > 0)
                        .Select(p => new Entry(p.Key, p.Value.RunOuts, -p.Value.DirectHits, p.Value.RunOuts.ToString(), Standard(p.Key, p.Value))),
                    true, new() { "Direct hits", "As thrower", "Received" }),

                Board("mostStumpings", "Most stumpings", "St",
                    all.Where(p => p.Value.Stumpings > 0)
                        .Select(p => new Entry(p.Key, p.Value.Stumpings, 0, p.Value.Stumpings.ToString(), Standard(p.Key, p.Value))),
                    true, new() { "Mat", "Ct" }),

                Board("mostDismissals", "Most fielding dismissals", "Dis",
                    all.Where(p => p.Value.Dismissals > 0)
                        .Select(p => new Entry(p.Key, p.Value.Dismissals, 0, p.Value.Dismissals.ToString(), Standard(p.Key, p.Value))),
                    true, new() { "Ct", "St", "RO" }),
            };
        }

        public CricketBoard MvpBoard() =>
            Board("mvp", "Most valuable player", "Points",
                _mvp.Select(p =>
                {
                    var played = Math.Max(1, MatchesPlayed(p.Key));
                    return new Entry(p.Key, p.Value.Total, played, p.Value.Total.ToString(), new()
                    {
                        ["Mat"] = MatchesPlayed(p.Key).ToString(),
                        ["Batting"] = p.Value.Batting.ToString(),
                        ["Bowling"] = p.Value.Bowling.ToString(),
                        ["Fielding"] = p.Value.Fielding.ToString(),
                        ["XI"] = p.Value.Playing.ToString(),
                        ["Per match"] = F1(p.Value.Total / (double)played)
                    });
                }),
                true, new() { "Batting", "Bowling", "Fielding", "XI", "Per match", "Mat" },
                take: 25);

        public List<CricketPlayerOfMatch> PlayersOfMatch() =>
            _playersOfMatch.OrderByDescending(p => p.MatchNumber).ToList();

        // --- Records and summary ---------------------------------------

        public CricketTeamRecords Records()
        {
            string Line(CricketInnings i, CricketMatch m) => $"vs {i.BowlingTeamName} · M{m.MatchNumber}";

            var finished = _teamInnings.Where(t => t.Innings.Status == InningsStatus.Completed).ToList();

            var wins = new List<(CricketTeamRecord Record, int Weight)>();
            foreach (var m in _decided.Where(m => m.WinnerTeamId.HasValue && m.ForfeitWinnerTeamId is null
                                                  && m.ResultSummary is not null))
            {
                var winner = m.WinnerTeamId == m.HomeTeamId ? m.HomeTeamName : m.AwayTeamName;
                var loser = m.WinnerTeamId == m.HomeTeamId ? m.AwayTeamName : m.HomeTeamName;
                var runs = Regex.Match(m.ResultSummary!, @"won by (\d+) run");
                var wickets = Regex.Match(m.ResultSummary!, @"won by (\d+) wicket");

                if (runs.Success)
                    wins.Add((new CricketTeamRecord { TeamName = winner, Value = $"{runs.Groups[1].Value} runs", Detail = $"vs {loser} · M{m.MatchNumber}" },
                        int.Parse(runs.Groups[1].Value)));
                else if (wickets.Success)
                    wins.Add((new CricketTeamRecord { TeamName = winner, Value = $"{wickets.Groups[1].Value} wickets", Detail = $"vs {loser} · M{m.MatchNumber}" },
                        // A wicket is worth roughly ten runs, which only matters for ordering the list.
                        int.Parse(wickets.Groups[1].Value) * 10));
            }

            return new CricketTeamRecords
            {
                HighestTotals = finished
                    .OrderByDescending(t => t.Innings.Runs).ThenBy(t => t.Innings.LegalBalls).Take(5)
                    .Select(t => new CricketTeamRecord { TeamName = t.Innings.BattingTeamName, Value = t.Innings.ScoreLine, Detail = Line(t.Innings, t.Match) })
                    .ToList(),
                LowestTotals = finished
                    .Where(t => t.Innings.EndReason is InningsEndReason.AllOut or InningsEndReason.OversComplete)
                    .OrderBy(t => t.Innings.Runs).ThenBy(t => t.Innings.LegalBalls).Take(5)
                    .Select(t => new CricketTeamRecord { TeamName = t.Innings.BattingTeamName, Value = t.Innings.ScoreLine, Detail = Line(t.Innings, t.Match) })
                    .ToList(),
                BiggestWins = wins.OrderByDescending(w => w.Weight).Take(5).Select(w => w.Record).ToList(),
                HighestPartnerships = _partnerships
                    .OrderByDescending(p => p.Runs).ThenBy(p => p.Balls).Take(5)
                    .Select(p => new CricketTeamRecord
                    {
                        TeamName = p.TeamName,
                        Value = $"{p.Runs} ({p.Balls})",
                        Detail = $"{p.Batters} · {Ordinal(p.Wicket)} wicket · vs {p.Opponent} · M{p.MatchNumber}"
                    })
                    .ToList()
            };
        }

        private static string Ordinal(int n) => n switch
        {
            1 => "1st", 2 => "2nd", 3 => "3rd", _ => $"{n}th"
        };

        public CricketStatsSummary Summary(List<CricketMatch> matches, int completed, int inProgress)
        {
            var mainInnings = _teamInnings.Select(t => t.Innings).ToList();
            var topBat = _bat.OrderByDescending(p => p.Value.Runs).FirstOrDefault();
            var topBowl = _bowl.Where(p => p.Value.Wickets > 0)
                .OrderByDescending(p => p.Value.Wickets).ThenBy(p => p.Value.Runs).FirstOrDefault();
            var topMvp = _mvp.OrderByDescending(p => p.Value.Total).FirstOrDefault();
            var highInnings = _innings.OrderByDescending(r => r.Runs).ThenBy(r => r.Balls).FirstOrDefault();
            var bestSpell = _spells.Where(s => s.Wickets > 0)
                .OrderByDescending(s => s.Wickets).ThenBy(s => s.Runs).FirstOrDefault();
            var highTotal = _teamInnings.OrderByDescending(t => t.Innings.Runs).FirstOrDefault();

            return new CricketStatsSummary
            {
                TotalMatches = matches.Count,
                MatchesCompleted = completed,
                MatchesInProgress = inProgress,
                TotalRuns = mainInnings.Sum(i => i.Runs),
                TotalWickets = mainInnings.Sum(i => i.Wickets),
                Extras = mainInnings.Sum(i => i.ExtrasTotal),
                Fours = Fours,
                Sixes = Sixes,
                Fifties = _bat.Values.Sum(b => b.Fifties),
                Hundreds = _bat.Values.Sum(b => b.Hundreds),
                HighestTeamTotal = highTotal.Innings is null ? null
                    : $"{highTotal.Innings.ScoreLine} — {highTotal.Innings.BattingTeamName}",
                HighestScore = highInnings is null ? null
                    : $"{highInnings.Runs}{(highInnings.NotOut ? "*" : "")} — {Name(highInnings.PlayerId)}",
                BestBowling = bestSpell is null ? null : $"{bestSpell.Wickets}/{bestSpell.Runs} — {Name(bestSpell.PlayerId)}",
                TopRunScorer = topBat.Value is null || topBat.Value.Runs == 0 ? null
                    : $"{Name(topBat.Key)} — {topBat.Value.Runs}",
                TopWicketTaker = topBowl.Value is null ? null : $"{Name(topBowl.Key)} — {topBowl.Value.Wickets}",
                Mvp = topMvp.Value is null ? null : $"{Name(topMvp.Key)} — {topMvp.Value.Total} pts"
            };
        }
    }
}
