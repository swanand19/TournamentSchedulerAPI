using TournamentScheduler.Api.Data.DataServices;
using TournamentScheduler.Api.Data.Queries;
using TournamentScheduler.Api.Models;
using TournamentScheduler.Api.Models.Cricket;

namespace TournamentScheduler.Api.Services.Cricket;

public interface ICricketMatchQueryService
{
    Task<ServiceResult<object>> GetSetupOptionsAsync(int id);
    Task<ServiceResult<CricketMatchAwardsDto>> GetAwardsAsync(int id);
    Task<ServiceResult<object>> GetBallsAsync(int id);
    Task<ServiceResult<object>> GetEventsAsync(int id);
}

/// <summary>
/// The read-only views of a cricket match that are not the scoring state itself: what the setup
/// screen offers, the honours, the ball-by-ball and the timeline. Scoring lives in
/// <see cref="CricketScoringService"/>.
/// </summary>
public class CricketMatchQueryService : ServiceBase, ICricketMatchQueryService
{
    private readonly IDataService<CricketMatch> _matches;
    private readonly IDataService<CricketMatchEvent> _events;
    private readonly IDataService<Team> _teams;
    private readonly IDataService<PlayerCricketProfile> _profiles;

    public CricketMatchQueryService(
        IDataService<CricketMatch> matches,
        IDataService<CricketMatchEvent> events,
        IDataService<Team> teams,
        IDataService<PlayerCricketProfile> profiles)
    {
        _matches = matches;
        _events = events;
        _teams = teams;
        _profiles = profiles;
    }

    /// <summary>Presets and both squads, for the setup screen.</summary>
    public async Task<ServiceResult<object>> GetSetupOptionsAsync(int id)
    {
        var match = await _matches.FirstOrDefaultAsync(m => m.Id == id);
        if (match == null) return NotFound("Match not found.");

        var teamIds = new[] { match.HomeTeamId, match.AwayTeamId }.Where(t => t.HasValue).Select(t => t!.Value).ToList();

        var teams = await _teams.ListAsync(t => teamIds.Contains(t.Id), TeamQueries.WithPlayersAndProfiles);

        var lastSquads = new Dictionary<int, object?>();
        foreach (var team in teams)
            lastSquads[team.Id] = await LastSquadAsync(match, team);

        // A tournament is played to one format, so the last match set up is a better starting point
        // than a generic T20 — otherwise a 6-a-side league re-types "6" before every fixture.
        // No tracking: the rules are an owned type, and EF will only project one on its own when it
        // is not being tracked — which a read-only lookup never needs anyway.
        var lastFormat = await _matches.QueryFirstOrDefaultAsync(q => q
            .Where(m => m.TournamentId == match.TournamentId && m.Id != id && m.Squad.Any())
            .OrderByDescending(m => m.StartedAt.HasValue)
            .ThenByDescending(m => m.StartedAt)
            .ThenByDescending(m => m.Id)
            .Select(m => new { m.MatchNumber, m.Rules }), tracking: false);

        return Ok(new
        {
            match.Id,
            match.HomeTeamId,
            match.AwayTeamId,
            match.HomeTeamName,
            match.AwayTeamName,
            Presets = CricketRulePresets.All.Keys.Select(name => new
            {
                Name = name,
                Rules = CricketRulePresets.Create(name)
            }),
            LastFormat = lastFormat,
            Teams = teams.Select(t => new
            {
                t.Id,
                t.Name,
                t.DefaultCaptainPlayerId,
                Players = t.Players.Select(p => new
                {
                    p.Id,
                    p.Name,
                    p.JerseyNumber,
                    Cricket = p.Cricket == null ? null : new
                    {
                        Role = p.Cricket.PrimaryRole.ToString(),
                        RoleLabel = p.Cricket.RoleLabel,
                        BattingStyle = p.Cricket.BattingStyle?.ToString(),
                        BowlingStyleLabel = p.Cricket.BowlingStyleLabel,
                        p.Cricket.BattingOrderPreference
                    }
                }).OrderBy(p => p.Name),
                LastSquad = lastSquads[t.Id]
            })
        });
    }

    /// <summary>
    /// The XI this team fielded in its most recent other match of the tournament, so the setup
    /// screen can offer "same as last time" — most sides change one or two players, not eleven.
    /// Started matches win over ones only set up; players since removed from the team are dropped.
    /// </summary>
    private async Task<object?> LastSquadAsync(CricketMatch current, Team team)
    {
        var previous = await _matches.QueryFirstOrDefaultAsync(q => q
            .Where(m => m.TournamentId == current.TournamentId
                        && m.Id != current.Id
                        && (m.HomeTeamId == team.Id || m.AwayTeamId == team.Id)
                        && m.Squad.Any(p => p.TeamId == team.Id))
            .OrderByDescending(m => m.StartedAt.HasValue)
            .ThenByDescending(m => m.StartedAt)
            .ThenByDescending(m => m.Id)
            .Select(m => new
            {
                m.Id,
                m.MatchNumber,
                Opponent = m.HomeTeamId == team.Id ? m.AwayTeamName : m.HomeTeamName,
                Players = m.Squad
                    .Where(p => p.TeamId == team.Id)
                    .Select(p => new { p.PlayerId, p.SquadStatus, p.IsCaptain, p.IsWicketKeeper, p.BattingOrder })
                    .ToList()
            }));

        if (previous == null) return null;

        var stillHere = team.Players.Select(p => p.Id).ToHashSet();

        return new
        {
            MatchId = previous.Id,
            previous.MatchNumber,
            previous.Opponent,
            Players = previous.Players
                .Where(p => stillHere.Contains(p.PlayerId))
                .Select(p => new
                {
                    p.PlayerId,
                    Playing = p.SquadStatus == CricketSquadStatus.Playing,
                    p.IsCaptain,
                    p.IsWicketKeeper,
                    p.BattingOrder
                })
        };
    }

    /// <summary>Player of the match and the best of each discipline.</summary>
    public async Task<ServiceResult<CricketMatchAwardsDto>> GetAwardsAsync(int id)
    {
        var match = await _matches.FirstOrDefaultAsync(m => m.Id == id, CricketMatchQueries.Full);
        if (match == null) return NotFound("Match not found.");

        var playerIds = match.Squad.Select(p => p.PlayerId).ToList();
        var roles = (await _profiles.ListAsync(p => playerIds.Contains(p.PlayerId)))
            .ToDictionary(p => p.PlayerId, p => p.PrimaryRole);

        return Success(CricketMatchAwards.Build(match, roles));
    }

    /// <summary>The whole ball-by-ball, oldest first.</summary>
    public async Task<ServiceResult<object>> GetBallsAsync(int id)
    {
        var match = await _matches.FirstOrDefaultAsync(m => m.Id == id, CricketMatchQueries.Full);
        if (match == null) return NotFound("Match not found.");

        var timeline = match.Innings
            .OrderBy(i => i.InningsNumber)
            .Select(i => new
            {
                i.InningsNumber,
                i.BattingTeamName,
                Balls = i.Balls.OrderBy(b => b.SequenceNumber).Select(b => new
                {
                    b.SequenceNumber,
                    b.OverNumber,
                    b.BallInOver,
                    b.BowlerId,
                    b.StrikerId,
                    b.NonStrikerId,
                    b.RunsOffBat,
                    b.IsWide,
                    b.IsNoBall,
                    b.WideExtraRuns,
                    b.Byes,
                    b.LegByes,
                    b.PenaltyRuns,
                    b.IsFreeHit,
                    b.TotalRuns,
                    WicketType = b.WicketType?.ToString(),
                    b.DismissedPlayerId,
                    b.FielderId
                })
            });

        return Ok(timeline);
    }

    /// <summary>The non-ball timeline: toss, batters in, bowlers on, innings ends, the result.</summary>
    public async Task<ServiceResult<object>> GetEventsAsync(int id)
    {
        if (!await _matches.AnyAsync(m => m.Id == id)) return NotFound("Match not found.");

        var events = await _events.QueryAsync(q => q
            .Where(e => e.CricketMatchId == id)
            .OrderBy(e => e.Id)
            .Select(e => new
            {
                e.Id,
                EventType = e.EventType.ToString(),
                e.InningsNumber,
                e.TeamId,
                e.PlayerId,
                e.Description,
                e.CreatedAt
            }));

        return Ok(events);
    }
}
