using TournamentScheduler.Api.Data.DataServices;
using TournamentScheduler.Api.Data.Queries;
using TournamentScheduler.Api.Models;
using TournamentScheduler.Api.Models.Cricket;
using TournamentScheduler.Api.Models.Watch;

namespace TournamentScheduler.Api.Services.Watch;

public interface IWatchService
{
    Task<ServiceResult<WatchMatchDto>> GetMatchAsync(string sport, int matchId);
    Task<ServiceResult<ActivityPageDto>> GetActivityAsync(string sport, int matchId, int? before, int? take);
}

/// <summary>
/// The match for people watching rather than scoring (Round 2): its sides — the squads before it
/// starts, the line-ups after — and its activity in plain words, a page at a time, newest first.
/// The score itself still comes from each sport's own match answer, which the scorer's screen uses.
/// </summary>
public class WatchService : ServiceBase, IWatchService
{
    public const int DefaultPage = 40;
    public const int MaxPage = 100;

    private readonly IDataService<Match> _matches;
    private readonly IDataService<MatchEvent> _matchEvents;
    private readonly IDataService<CricketMatch> _cricketMatches;
    private readonly IDataService<CricketMatchEvent> _cricketEvents;
    private readonly IDataService<Team> _teams;
    private readonly IDataService<Tournament> _tournaments;

    public WatchService(IDataService<Match> matches, IDataService<MatchEvent> matchEvents,
        IDataService<CricketMatch> cricketMatches, IDataService<CricketMatchEvent> cricketEvents,
        IDataService<Team> teams, IDataService<Tournament> tournaments)
    {
        _matches = matches;
        _matchEvents = matchEvents;
        _cricketMatches = cricketMatches;
        _cricketEvents = cricketEvents;
        _teams = teams;
        _tournaments = tournaments;
    }

    private static Sport? ParseSport(string sport) =>
        string.Equals(sport, "football", StringComparison.OrdinalIgnoreCase) ? Sport.Football
        : string.Equals(sport, "cricket", StringComparison.OrdinalIgnoreCase) ? Sport.Cricket
        : null;

    public async Task<ServiceResult<ActivityPageDto>> GetActivityAsync(string sport, int matchId, int? before, int? take)
    {
        List<ActivityItemDto> all;
        switch (ParseSport(sport))
        {
            case Sport.Football:
                var match = await _matches.FirstOrDefaultAsync(m => m.Id == matchId, MatchQueries.ForMatchScreen, tracking: false);
                if (match == null) return NotFound("Match not found.");
                var events = await _matchEvents.ListAsync(e => e.MatchId == matchId, tracking: false);
                all = FootballActivity.Build(match, events, Names(match));
                break;

            case Sport.Cricket:
                var cricket = await _cricketMatches.FirstOrDefaultAsync(m => m.Id == matchId, CricketMatchQueries.Full, tracking: false);
                if (cricket == null) return NotFound("Match not found.");
                var log = await _cricketEvents.ListAsync(e => e.CricketMatchId == matchId, tracking: false);
                all = CricketActivity.Build(cricket, log);
                break;

            default:
                return BadRequest("Sport must be football or cricket.");
        }

        var size = Math.Clamp(take ?? DefaultPage, 1, MaxPage);
        var older = all.Where(i => before is not int b || i.Id < b).OrderByDescending(i => i.Id).ToList();
        return Success(new ActivityPageDto { Items = older.Take(size).ToList(), HasMore = older.Count > size });
    }

    public async Task<ServiceResult<WatchMatchDto>> GetMatchAsync(string sport, int matchId) =>
        ParseSport(sport) switch
        {
            Sport.Football => await FootballAsync(matchId),
            Sport.Cricket => await CricketAsync(matchId),
            _ => BadRequest("Sport must be football or cricket.")
        };

    private static Dictionary<int, string> Names(Match match) =>
        new[] { match.HomeTeam, match.AwayTeam }
            .SelectMany(t => t?.Players ?? [])
            .GroupBy(p => p.Id)
            .ToDictionary(g => g.Key, g => g.First().Name);

    private async Task<string> TournamentNameAsync(int tournamentId) =>
        await _tournaments.QueryFirstOrDefaultAsync(q => q.Where(t => t.Id == tournamentId).Select(t => t.Name), tracking: false) ?? "";

    // --- Football ----------------------------------------------------------

    private async Task<ServiceResult<WatchMatchDto>> FootballAsync(int matchId)
    {
        var match = await _matches.FirstOrDefaultAsync(m => m.Id == matchId, MatchQueries.ForMatchScreen, tracking: false);
        if (match == null) return NotFound("Match not found.");
        var events = await _matchEvents.ListAsync(e => e.MatchId == matchId, tracking: false);

        var phase = match.Status switch
        {
            MatchStatus.NotStarted => WatchPhase.Preview,
            MatchStatus.Completed => WatchPhase.Finished,
            _ => WatchPhase.Live
        };

        return Success(new WatchMatchDto
        {
            MatchId = match.Id,
            Sport = Sport.Football,
            TournamentId = match.TournamentId,
            TournamentName = await TournamentNameAsync(match.TournamentId),
            GroupName = match.GroupName,
            RoundLabel = match.RoundLabel,
            MatchNumber = match.MatchNumber,
            Phase = phase,
            FormatLine = match.MinutesPerHalf is int minutes
                ? $"2 halves of {minutes} minutes" + (match.MaxPlayersPerSide is int side ? $" · {side} a side" : "")
                : null,
            ResultLine = phase == WatchPhase.Finished ? FootballResult(match) : null,
            Home = FootballSide(match, match.HomeTeam, match.HomeTeamId, match.HomeTeamName, events),
            Away = FootballSide(match, match.AwayTeam, match.AwayTeamId, match.AwayTeamName, events)
        });
    }

    private static string FootballResult(Match match)
    {
        string Name(int id) => id == match.HomeTeamId ? match.HomeTeamName : match.AwayTeamName;
        var score = $"{match.HomeScore}–{match.AwayScore}";
        if (match.ForfeitWinnerTeamId is int forfeit) return $"{Name(forfeit)} awarded the match";
        if (match.PenaltyWinnerTeamId is int pens) return $"{Name(pens)} win on penalties ({score} after full time)";
        if (match.HomeScore == match.AwayScore) return $"Drawn {score}";
        var (winner, high, low) = match.HomeScore > match.AwayScore
            ? (match.HomeTeamName, match.HomeScore, match.AwayScore)
            : (match.AwayTeamName, match.AwayScore, match.HomeScore);
        return $"{winner} win {high}–{low}";
    }

    private static WatchSideDto FootballSide(Match match, Team? team, int? teamId, string name, List<MatchEvent> events)
    {
        var side = new WatchSideDto { TeamId = teamId, Name = name };
        var players = (team?.Players ?? []).ToDictionary(p => p.Id);

        WatchPlayerDto Row(Player p) => new()
        {
            PlayerId = p.Id,
            Name = p.Name,
            JerseyNumber = p.JerseyNumber,
            Role = string.IsNullOrWhiteSpace(p.Position) ? null : p.Position
        };

        static IEnumerable<WatchPlayerDto> Ordered(IEnumerable<WatchPlayerDto> rows) =>
            rows.OrderBy(r => r.JerseyNumber ?? int.MaxValue).ThenBy(r => r.Name);

        if (match.Status == MatchStatus.NotStarted)
        {
            side.Heading = "Squad";
            side.Players = Ordered(players.Values.Select(Row)).ToList();
            return side;
        }

        side.Heading = "Starting line-up";
        var squad = match.MatchPlayers.Where(mp => mp.TeamId == teamId && players.ContainsKey(mp.PlayerId)).ToList();
        var marks = Marks(events.Where(e => e.TeamId == teamId));

        WatchPlayerDto Marked(MatchPlayer mp)
        {
            var row = Row(players[mp.PlayerId]);
            if (marks.TryGetValue(mp.PlayerId, out var list)) row.Marks = list;
            return row;
        }

        side.Players = Ordered(squad.Where(mp => mp.StartedMatch).Select(Marked)).ToList();
        side.Bench = Ordered(squad.Where(mp => !mp.StartedMatch && mp.SquadStatus != SquadStatus.Unavailable).Select(Marked)).ToList();
        return side;
    }

    /// <summary>Goals, cards and substitutions per player, in match order.</summary>
    private static Dictionary<int, List<WatchMarkDto>> Marks(IEnumerable<MatchEvent> events)
    {
        var marks = new Dictionary<int, List<WatchMarkDto>>();
        void Add(int? playerId, WatchMarkKind kind, MatchEvent e)
        {
            if (playerId is not int id) return;
            if (!marks.TryGetValue(id, out var list)) marks[id] = list = new();
            list.Add(new WatchMarkDto
            {
                Kind = kind,
                Minute = e.StoppageMinute is int s && s > 0 ? $"{e.MinuteOfMatch}+{s}'" : $"{e.MinuteOfMatch}'"
            });
        }

        var yellows = new Dictionary<int, int>();
        foreach (var e in events.OrderBy(e => e.Id))
        {
            switch (e.EventType)
            {
                case MatchEventType.Goal: Add(e.PlayerId, WatchMarkKind.Goal, e); break;
                case MatchEventType.YellowCard:
                    var second = e.PlayerId is int p && (yellows[p] = yellows.GetValueOrDefault(p) + 1) >= 2;
                    Add(e.PlayerId, second ? WatchMarkKind.RedCard : WatchMarkKind.YellowCard, e);
                    break;
                case MatchEventType.RedCard: Add(e.PlayerId, WatchMarkKind.RedCard, e); break;
                case MatchEventType.SubstitutionIn:
                    Add(e.PlayerId, WatchMarkKind.SubbedOff, e);
                    Add(e.RelatedPlayerId, WatchMarkKind.SubbedOn, e);
                    break;
            }
        }
        return marks;
    }

    // --- Cricket -----------------------------------------------------------

    private async Task<ServiceResult<WatchMatchDto>> CricketAsync(int matchId)
    {
        var match = await _cricketMatches.FirstOrDefaultAsync(m => m.Id == matchId, CricketMatchQueries.WithSquad, tracking: false);
        if (match == null) return NotFound("Match not found.");

        var teamIds = new[] { match.HomeTeamId, match.AwayTeamId }.OfType<int>().ToList();
        var teams = await _teams.ListAsync(t => teamIds.Contains(t.Id), TeamQueries.WithPlayersAndProfiles, tracking: false);
        var toss = match.IsSetUp
            ? await _cricketEvents.QueryFirstOrDefaultAsync(q => q
                .Where(e => e.CricketMatchId == matchId && e.EventType == CricketMatchEventType.TossCompleted)
                .OrderByDescending(e => e.Id).Select(e => e.Description), tracking: false)
            : null;

        return Success(new WatchMatchDto
        {
            MatchId = match.Id,
            Sport = Sport.Cricket,
            TournamentId = match.TournamentId,
            TournamentName = await TournamentNameAsync(match.TournamentId),
            GroupName = match.GroupName,
            RoundLabel = match.RoundLabel,
            MatchNumber = match.MatchNumber,
            Phase = match.Status == CricketMatchStatus.NotStarted ? WatchPhase.Preview
                : match.IsOver ? WatchPhase.Finished
                : WatchPhase.Live,
            FormatLine = match.IsSetUp ? CricketFormatLine(match.Rules) : null,
            TossLine = toss,
            ResultLine = match.IsOver ? match.ResultSummary : null,
            Home = CricketSide(match, teams.FirstOrDefault(t => t.Id == match.HomeTeamId), match.HomeTeamId, match.HomeTeamName),
            Away = CricketSide(match, teams.FirstOrDefault(t => t.Id == match.AwayTeamId), match.AwayTeamId, match.AwayTeamName)
        });
    }

    private static string CricketFormatLine(CricketMatchRules rules)
    {
        var innings = rules.InningsPerSide == 1 ? "" : $"{rules.InningsPerSide} innings each · ";
        var length = rules.OversPerInnings is int overs
            ? rules.InningsPerSide == 1 ? $"{overs} overs a side" : $"{overs} overs an innings"
            : "No overs limit";
        return $"{innings}{length} · {rules.PlayersPerSide} a side";
    }

    private static WatchSideDto CricketSide(CricketMatch match, Team? team, int? teamId, string name)
    {
        var side = new WatchSideDto { TeamId = teamId, Name = name };
        var players = (team?.Players ?? []).ToDictionary(p => p.Id);

        WatchPlayerDto Row(Player p) => new()
        {
            PlayerId = p.Id,
            Name = p.Name,
            JerseyNumber = p.JerseyNumber,
            Role = p.Cricket?.RoleLabel
        };

        if (!match.IsSetUp)
        {
            side.Heading = "Squad";
            side.Players = players.Values.Select(Row).OrderBy(r => r.JerseyNumber ?? int.MaxValue).ThenBy(r => r.Name).ToList();
            return side;
        }

        side.Heading = "Playing XI";
        var squad = match.Squad.Where(s => s.TeamId == teamId).ToList();

        WatchPlayerDto Picked(CricketMatchPlayer s)
        {
            var row = players.TryGetValue(s.PlayerId, out var p) ? Row(p) : new WatchPlayerDto { PlayerId = s.PlayerId };
            row.Name = s.PlayerName;
            row.IsCaptain = s.IsCaptain;
            row.IsWicketKeeper = s.IsWicketKeeper;
            row.BattingOrder = s.BattingOrder;
            return row;
        }

        side.Players = squad.Where(s => s.SquadStatus == CricketSquadStatus.Playing)
            .OrderBy(s => s.BattingOrder ?? int.MaxValue).ThenBy(s => s.PlayerName).Select(Picked).ToList();
        side.Bench = squad.Where(s => s.SquadStatus == CricketSquadStatus.Bench).OrderBy(s => s.PlayerName).Select(Picked).ToList();
        return side;
    }
}
