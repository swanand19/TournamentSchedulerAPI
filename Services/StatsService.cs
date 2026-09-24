using Microsoft.EntityFrameworkCore;
using TournamentScheduler.Api.Data;
using TournamentScheduler.Api.Models;

namespace TournamentScheduler.Api.Services;

public interface IStatsService
{
    Task<TournamentStats?> BuildAsync(int tournamentId);
}

/// <summary>
/// Builds every leaderboard for one tournament from its completed matches.
///
/// Only completed matches count. A match still being played has a moving clock, so minutes played
/// would drift and a shootout could still overturn a standing — counting it would make the boards
/// disagree with each other. The basis is stated on the response so the reader knows.
/// </summary>
public class StatsService : IStatsService
{
    private readonly TournamentDbContext _db;

    public StatsService(TournamentDbContext db)
    {
        _db = db;
    }

    private const int WinPoints = 3;
    private const int DrawPoints = 1;

    public async Task<TournamentStats?> BuildAsync(int tournamentId)
    {
        var tournament = await _db.Tournaments.FirstOrDefaultAsync(t => t.Id == tournamentId);
        if (tournament == null) return null;

        // Teams and players are scoped to the tournament, so a leaderboard can never show a player
        // from another competition.
        var teams = await _db.Teams
            .Where(t => t.TournamentId == tournamentId)
            .Include(t => t.Players)
            .AsNoTracking()
            .ToListAsync();

        var allMatches = await _db.Matches
            .Where(m => m.TournamentId == tournamentId)
            .AsNoTracking()
            .ToListAsync();

        var counted = allMatches.Where(m => m.Status == MatchStatus.Completed).ToList();
        var countedIds = counted.Select(m => m.Id).ToHashSet();

        var squads = await _db.MatchPlayers
            .Where(mp => countedIds.Contains(mp.MatchId))
            .AsNoTracking()
            .ToListAsync();

        var events = await _db.MatchEvents
            .Where(e => countedIds.Contains(e.MatchId))
            .OrderBy(e => e.CreatedAt).ThenBy(e => e.Id)
            .AsNoTracking()
            .ToListAsync();

        var kicks = await _db.PenaltyKicks
            .Where(k => countedIds.Contains(k.MatchId))
            .AsNoTracking()
            .ToListAsync();

        var playerLookup = teams
            .SelectMany(t => t.Players.Select(p => (Player: p, Team: t)))
            .ToDictionary(x => x.Player.Id, x => x);

        var teamNames = teams.ToDictionary(t => t.Id, t => t.Name);

        var stats = new TournamentStats
        {
            TournamentId = tournamentId,
            TournamentName = tournament.Name,
            Basis = counted.Count == 0
                ? "No matches have been completed yet — stats appear as results come in."
                : $"Based on {counted.Count} completed match{(counted.Count == 1 ? "" : "es")}."
        };

        var tallies = BuildPlayerTallies(counted, squads, events, kicks);

        stats.Summary = BuildSummary(allMatches, counted, events, kicks, tallies);
        stats.Standings = BuildStandings(counted, events, teams);
        stats.PlayerBoards = BuildBoards(tallies, playerLookup, teamNames);
        stats.Notes = BuildNotes(counted);

        return stats;
    }

    // -----------------------------------------------------------------
    // Per-player tallies
    // -----------------------------------------------------------------

    private sealed class Tally
    {
        public int PlayerId;
        public int TeamId;
        public int Goals;
        public int Assists;
        public int Appearances;
        public int Starts;
        public int MinutesPlayed;
        public int YellowCards;
        public int StraightReds;
        public int SecondYellowReds;
        public int ShootoutTaken;
        public int ShootoutScored;
        public int TotalReds => StraightReds + SecondYellowReds;
        public int DisciplinePoints => YellowCards + TotalReds * 3;
    }

    private static Dictionary<int, Tally> BuildPlayerTallies(
        List<Match> matches, List<MatchPlayer> squads, List<MatchEvent> events, List<PenaltyKick> kicks)
    {
        var tallies = new Dictionary<int, Tally>();

        Tally For(int playerId, int teamId)
        {
            if (!tallies.TryGetValue(playerId, out var t))
                tallies[playerId] = t = new Tally { PlayerId = playerId, TeamId = teamId };
            return t;
        }

        var squadsByMatch = squads.GroupBy(s => s.MatchId).ToDictionary(g => g.Key, g => g.ToList());
        var eventsByMatch = events.GroupBy(e => e.MatchId).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var match in matches)
        {
            var squad = squadsByMatch.TryGetValue(match.Id, out var s) ? s : new List<MatchPlayer>();
            var timeline = eventsByMatch.TryGetValue(match.Id, out var e) ? e : new List<MatchEvent>();

            // Squads are validated on setup, but legacy rows could hold a player twice. Collapse
            // rather than throw: a duplicate is not worth turning a stats page into a 500.
            squad = squad.GroupBy(x => x.PlayerId).Select(g => g.First()).ToList();
            var teamOf = squad.ToDictionary(x => x.PlayerId, x => x.TeamId);
            var finalMinute = FinalMinuteOf(match, timeline);

            // Who came on as a substitute — the incoming player rides on RelatedPlayerId.
            var cameOn = timeline
                .Where(x => x.EventType == MatchEventType.SubstitutionIn && x.RelatedPlayerId.HasValue)
                .Select(x => x.RelatedPlayerId!.Value)
                .ToHashSet();

            foreach (var member in squad)
            {
                var appeared = member.StartedMatch || cameOn.Contains(member.PlayerId);
                if (!appeared) continue;

                var tally = For(member.PlayerId, member.TeamId);
                tally.Appearances++;
                if (member.StartedMatch) tally.Starts++;
                tally.MinutesPlayed += MinutesOnPitch(member, timeline, finalMinute);
            }

            foreach (var evt in timeline)
            {
                switch (evt.EventType)
                {
                    // Only goals scored in play. Shootout kicks are PenaltyKick events and never
                    // touch this branch, so a shootout can't inflate the scoring charts.
                    case MatchEventType.Goal when evt.PlayerId.HasValue:
                        For(evt.PlayerId.Value, evt.TeamId ?? TeamOf(teamOf, evt.PlayerId.Value)).Goals++;
                        if (evt.RelatedPlayerId.HasValue)
                            For(evt.RelatedPlayerId.Value, evt.TeamId ?? TeamOf(teamOf, evt.RelatedPlayerId.Value)).Assists++;
                        break;

                    case MatchEventType.YellowCard when evt.PlayerId.HasValue:
                        For(evt.PlayerId.Value, evt.TeamId ?? TeamOf(teamOf, evt.PlayerId.Value)).YellowCards++;
                        break;

                    case MatchEventType.RedCard when evt.PlayerId.HasValue:
                        For(evt.PlayerId.Value, evt.TeamId ?? TeamOf(teamOf, evt.PlayerId.Value)).StraightReds++;
                        break;
                }
            }

            // A second booking is a sending-off in its own right: two yellows and one red, the way
            // official competition stats record it.
            foreach (var group in timeline
                         .Where(x => x.EventType == MatchEventType.YellowCard && x.PlayerId.HasValue)
                         .GroupBy(x => x.PlayerId!.Value)
                         .Where(g => g.Count() >= 2))
            {
                For(group.Key, TeamOf(teamOf, group.Key)).SecondYellowReds++;
            }
        }

        foreach (var kick in kicks.Where(k => k.PlayerId.HasValue))
        {
            var tally = For(kick.PlayerId!.Value, kick.TeamId);
            tally.ShootoutTaken++;
            if (kick.Scored) tally.ShootoutScored++;
        }

        return tallies;
    }

    private static int TeamOf(Dictionary<int, int> teamOf, int playerId) =>
        teamOf.TryGetValue(playerId, out var teamId) ? teamId : 0;

    /// <summary>Minute the match finished — the whistle, else the closing event, else the periods played.</summary>
    private static int FinalMinuteOf(Match match, List<MatchEvent> timeline)
    {
        if (match.FinalWhistleMinute is int whistle && whistle > 0) return whistle;

        var closing = timeline.LastOrDefault(e =>
            e.EventType == MatchEventType.MatchCompleted || e.EventType == MatchEventType.MatchAbandoned);
        if (closing != null) return EffectiveMinute(closing);

        var regulation = (match.MinutesPerHalf ?? 0) * 2;
        if (match.CurrentHalf > 2) regulation += (match.ExtraTimeMinutesPerHalf ?? 0) * (match.CurrentHalf - 2);
        return regulation;
    }

    private static int EffectiveMinute(MatchEvent evt) => evt.MinuteOfMatch + (evt.StoppageMinute ?? 0);

    /// <summary>
    /// Walks the timeline accumulating the spells a player spent on the pitch. Handles rolling
    /// substitutions, where the same player can come on and off more than once.
    /// </summary>
    private static int MinutesOnPitch(MatchPlayer member, List<MatchEvent> timeline, int finalMinute)
    {
        int? onSince = member.StartedMatch ? 0 : null;
        var minutes = 0;
        var yellows = 0;

        foreach (var evt in timeline)
        {
            var minute = Math.Min(EffectiveMinute(evt), finalMinute);

            if (evt.EventType == MatchEventType.SubstitutionIn)
            {
                if (evt.RelatedPlayerId == member.PlayerId && onSince == null)
                    onSince = minute;                              // came on
                else if (evt.PlayerId == member.PlayerId && onSince != null)
                {
                    minutes += Math.Max(0, minute - onSince.Value); // went off
                    onSince = null;
                }
                continue;
            }

            if (evt.PlayerId != member.PlayerId) continue;

            var sentOff = evt.EventType == MatchEventType.RedCard
                || (evt.EventType == MatchEventType.YellowCard && ++yellows >= 2);

            if (sentOff && onSince != null)
            {
                minutes += Math.Max(0, minute - onSince.Value);
                onSince = null;
            }
        }

        if (onSince != null) minutes += Math.Max(0, finalMinute - onSince.Value);
        return minutes;
    }

    // -----------------------------------------------------------------
    // Boards
    // -----------------------------------------------------------------

    private static List<StatBoard> BuildBoards(
        Dictionary<int, Tally> tallies,
        Dictionary<int, (Player Player, Team Team)> playerLookup,
        Dictionary<int, string> teamNames)
    {
        var boards = new List<StatBoard>();

        StatBoard Board(string key, string title, string valueLabel, Func<Tally, int> value,
            string? note = null, bool showPerMatch = false,
            Func<Tally, Dictionary<string, int>>? detail = null,
            List<string>? detailColumns = null,
            Func<Tally, bool>? include = null)
        {
            var rows = tallies.Values
                .Where(t => value(t) > 0 && (include == null || include(t)))
                .Where(t => playerLookup.ContainsKey(t.PlayerId))   // tournament players only
                .Select(t =>
                {
                    var (player, team) = playerLookup[t.PlayerId];
                    return new PlayerStatRow
                    {
                        PlayerId = t.PlayerId,
                        PlayerName = player.Name,
                        JerseyNumber = player.JerseyNumber,
                        Position = player.Position,
                        TeamId = team.Id,
                        TeamName = teamNames.TryGetValue(t.TeamId, out var n) ? n : team.Name,
                        Value = value(t),
                        Appearances = t.Appearances,
                        MinutesPlayed = t.MinutesPlayed,
                        PerMatch = showPerMatch && t.Appearances > 0
                            ? Math.Round((double)value(t) / t.Appearances, 2)
                            : null,
                        Detail = detail?.Invoke(t) ?? new Dictionary<string, int>()
                    };
                })
                // Most of the metric first; fewer appearances is the better return, then by name so
                // the order never shuffles between requests.
                .OrderByDescending(r => r.Value)
                .ThenBy(r => r.Appearances)
                .ThenBy(r => r.PlayerName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            AssignRanks(rows);

            var board = new StatBoard
            {
                Key = key,
                Title = title,
                ValueLabel = valueLabel,
                Note = note,
                ShowPerMatch = showPerMatch,
                DetailColumns = detailColumns ?? new List<string>(),
                Rows = rows
            };
            boards.Add(board);
            return board;
        }

        Board("goals", "Top goalscorers", "Goals", t => t.Goals, showPerMatch: true,
            note: "Goals scored in play, including penalties awarded during the match. Goals in a penalty shootout are not counted.");

        Board("assists", "Top assists", "Assists", t => t.Assists, showPerMatch: true,
            note: "Recorded when the scorer's goal is logged with an assisting team-mate.");

        Board("goalContributions", "Goal contributions", "G+A", t => t.Goals + t.Assists, showPerMatch: true,
            note: "Goals and assists combined.",
            detail: t => new Dictionary<string, int> { ["Goals"] = t.Goals, ["Assists"] = t.Assists },
            detailColumns: new List<string> { "Goals", "Assists" });

        Board("appearances", "Most appearances", "Apps", t => t.Appearances,
            note: "A player counts as having appeared if they started or came on as a substitute.",
            detail: t => new Dictionary<string, int> { ["Starts"] = t.Starts, ["Off bench"] = t.Appearances - t.Starts },
            detailColumns: new List<string> { "Starts", "Off bench" });

        Board("minutes", "Most minutes played", "Minutes", t => t.MinutesPlayed,
            note: "Counted from kick-off or the minute a substitute came on, to the final whistle, a substitution, or a sending-off.",
            detail: t => new Dictionary<string, int> { ["Starts"] = t.Starts },
            detailColumns: new List<string> { "Starts" });

        Board("yellowCards", "Most yellow cards", "Yellows", t => t.YellowCards,
            note: "Every booking counts, including the second one that leads to a sending-off.");

        Board("redCards", "Most red cards", "Reds", t => t.TotalReds,
            note: "Straight red cards and second-yellow sendings-off, listed separately.",
            detail: t => new Dictionary<string, int> { ["Straight"] = t.StraightReds, ["2nd yellow"] = t.SecondYellowReds },
            detailColumns: new List<string> { "Straight", "2nd yellow" });

        Board("discipline", "Worst disciplinary record", "Points", t => t.DisciplinePoints,
            note: "One point per yellow card, three per red — the usual way competitions rank discipline.",
            detail: t => new Dictionary<string, int> { ["Yellows"] = t.YellowCards, ["Reds"] = t.TotalReds },
            detailColumns: new List<string> { "Yellows", "Reds" });

        Board("shootouts", "Penalty shootout takers", "Scored", t => t.ShootoutScored,
            note: "Shootout kicks only. These deliberately do not count towards the goalscoring charts.",
            detail: t => new Dictionary<string, int>
            {
                ["Taken"] = t.ShootoutTaken,
                ["Missed"] = t.ShootoutTaken - t.ShootoutScored,
                ["Success %"] = t.ShootoutTaken > 0 ? (int)Math.Round(100.0 * t.ShootoutScored / t.ShootoutTaken) : 0
            },
            detailColumns: new List<string> { "Taken", "Missed", "Success %" });

        return boards;
    }

    private static void AssignRanks(List<PlayerStatRow> rows)
    {
        // Equal values share a rank, and the next rank skips accordingly (1, 2, 2, 4).
        for (var i = 0; i < rows.Count; i++)
            rows[i].Rank = i > 0 && rows[i].Value == rows[i - 1].Value ? rows[i - 1].Rank : i + 1;
    }

    // -----------------------------------------------------------------
    // Standings
    // -----------------------------------------------------------------

    private static List<TeamStandingRow> BuildStandings(List<Match> matches, List<MatchEvent> events, List<Team> teams)
    {
        var rows = teams.ToDictionary(t => t.Id, t => new TeamStandingRow
        {
            TeamId = t.Id,
            TeamName = t.Name
        });

        var cardsByMatch = events
            .Where(e => e.EventType is MatchEventType.YellowCard or MatchEventType.RedCard)
            .ToList();

        foreach (var match in matches)
        {
            if (match.HomeTeamId is not int homeId || match.AwayTeamId is not int awayId) continue;
            if (!rows.TryGetValue(homeId, out var home) || !rows.TryGetValue(awayId, out var away)) continue;

            home.GroupName = match.GroupName;
            away.GroupName = match.GroupName;

            home.Played++;
            away.Played++;
            home.GoalsFor += match.HomeScore;
            home.GoalsAgainst += match.AwayScore;
            away.GoalsFor += match.AwayScore;
            away.GoalsAgainst += match.HomeScore;

            if (match.AwayScore == 0) home.CleanSheets++;
            if (match.HomeScore == 0) away.CleanSheets++;

            // A team that wins by forfeit takes the three points however the score stood.
            var winnerId = match.ForfeitWinnerTeamId ?? match.PenaltyWinnerTeamId;
            if (match.PenaltyWinnerTeamId is int penWinner)
            {
                if (penWinner == homeId) { home.ShootoutsWon++; away.ShootoutsLost++; }
                else { away.ShootoutsWon++; home.ShootoutsLost++; }
            }

            if (winnerId == null && match.HomeScore != match.AwayScore)
                winnerId = match.HomeScore > match.AwayScore ? homeId : awayId;

            if (winnerId is int decided)
            {
                var winner = decided == homeId ? home : away;
                var loser = decided == homeId ? away : home;
                winner.Won++;
                winner.Points += WinPoints;
                loser.Lost++;
            }
            else
            {
                home.Drawn++;
                away.Drawn++;
                home.Points += DrawPoints;
                away.Points += DrawPoints;
            }

            foreach (var card in cardsByMatch.Where(c => c.MatchId == match.Id))
            {
                if (!card.TeamId.HasValue || !rows.TryGetValue(card.TeamId.Value, out var row)) continue;
                if (card.EventType == MatchEventType.YellowCard) row.YellowCards++;
                else row.RedCards++;
            }

            foreach (var group in events
                         .Where(e => e.MatchId == match.Id && e.EventType == MatchEventType.YellowCard && e.PlayerId.HasValue)
                         .GroupBy(e => e.PlayerId!.Value)
                         .Where(g => g.Count() >= 2))
            {
                var teamId = group.First().TeamId;
                if (teamId.HasValue && rows.TryGetValue(teamId.Value, out var row)) row.RedCards++;
            }
        }

        var ordered = rows.Values
            .OrderByDescending(r => r.Points)
            .ThenByDescending(r => r.GoalDifference)
            .ThenByDescending(r => r.GoalsFor)
            .ThenBy(r => r.TeamName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (var i = 0; i < ordered.Count; i++) ordered[i].Rank = i + 1;
        return ordered;
    }

    // -----------------------------------------------------------------
    // Summary
    // -----------------------------------------------------------------

    private static TournamentSummary BuildSummary(
        List<Match> allMatches, List<Match> counted, List<MatchEvent> events,
        List<PenaltyKick> kicks, Dictionary<int, Tally> tallies)
    {
        var goals = events.Count(e => e.EventType == MatchEventType.Goal);
        var secondYellows = events
            .Where(e => e.EventType == MatchEventType.YellowCard && e.PlayerId.HasValue)
            .GroupBy(e => new { e.MatchId, PlayerId = e.PlayerId!.Value })
            .Count(g => g.Count() >= 2);

        var summary = new TournamentSummary
        {
            TotalMatches = allMatches.Count,
            MatchesCompleted = counted.Count,
            MatchesInProgress = allMatches.Count(m => m.Status is MatchStatus.InProgress or MatchStatus.Paused or MatchStatus.PenaltyShootout),
            MatchesNotStarted = allMatches.Count(m => m.Status == MatchStatus.NotStarted),
            TotalGoals = goals,
            GoalsPerMatch = counted.Count > 0 ? Math.Round((double)goals / counted.Count, 2) : 0,
            TotalAssists = events.Count(e => e.EventType == MatchEventType.Goal && e.RelatedPlayerId.HasValue),
            TotalYellowCards = events.Count(e => e.EventType == MatchEventType.YellowCard),
            TotalRedCards = events.Count(e => e.EventType == MatchEventType.RedCard) + secondYellows,
            TotalSubstitutions = events.Count(e => e.EventType == MatchEventType.SubstitutionIn),
            MatchesDrawn = counted.Count(m => m.HomeScore == m.AwayScore && m.PenaltyWinnerTeamId == null && m.ForfeitWinnerTeamId == null),
            CleanSheets = counted.Count(m => m.HomeScore == 0) + counted.Count(m => m.AwayScore == 0),
            ShootoutsPlayed = kicks.Select(k => k.MatchId).Distinct().Count(),
            MatchesAbandoned = counted.Count(m => m.ForfeitWinnerTeamId != null),
            PlayersUsed = tallies.Values.Count(t => t.Appearances > 0),
            TeamsInvolved = counted.SelectMany(m => new[] { m.HomeTeamId, m.AwayTeamId })
                .Where(id => id.HasValue).Select(id => id!.Value).Distinct().Count()
        };

        var highest = counted.OrderByDescending(m => m.HomeScore + m.AwayScore).FirstOrDefault();
        if (highest != null && highest.HomeScore + highest.AwayScore > 0)
            summary.HighestScoringMatch = Describe(highest);

        var biggest = counted.OrderByDescending(m => Math.Abs(m.HomeScore - m.AwayScore)).FirstOrDefault();
        if (biggest != null && biggest.HomeScore != biggest.AwayScore)
            summary.BiggestWin = Describe(biggest);

        return summary;
    }

    private static string Describe(Match m) =>
        $"{m.HomeTeamName} {m.HomeScore}-{m.AwayScore} {m.AwayTeamName}";

    private static List<string> BuildNotes(List<Match> counted)
    {
        var notes = new List<string>
        {
            "Only completed matches are counted, so every board stays consistent while a match is still being played.",
            "Penalty shootout goals never count towards the goalscoring charts — they have their own board.",
            "A win is worth 3 points and a draw 1. A match settled by a shootout counts as a win for the team that won it."
        };

        if (counted.Any(m => m.ForfeitWinnerTeamId != null))
            notes.Add("An abandoned match is awarded to the team still able to field a side, with the score as it stood when it was called off.");

        return notes;
    }
}
