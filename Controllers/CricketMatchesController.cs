using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TournamentScheduler.Api.Data;
using TournamentScheduler.Api.Models;
using TournamentScheduler.Api.Models.Cricket;
using TournamentScheduler.Api.Services.Cricket;

namespace TournamentScheduler.Api.Controllers;

/// <summary>
/// The scoring endpoints. Deliberately thin: every action validates nothing itself, hands the
/// request to <see cref="ICricketScoringService"/>, and answers with the same match state whatever
/// it did — so the console can redraw from any response without a second round trip.
/// </summary>
[ApiController]
[Route("api/cricket-matches")]
public class CricketMatchesController : ControllerBase
{
    private readonly TournamentDbContext _db;
    private readonly ICricketScoringService _cricket;

    public CricketMatchesController(TournamentDbContext db, ICricketScoringService cricket)
    {
        _db = db;
        _cricket = cricket;
    }

    /// <summary>Turns an engine result into the right HTTP answer, or the state on success.</summary>
    private ActionResult<CricketMatchStateDto> Respond(CricketResult<CricketMatch> result)
    {
        if (result.NotFound) return NotFound(result.Error);
        if (result.Error != null) return BadRequest(result.Error);
        return Ok(_cricket.BuildState(result.Value!));
    }

    // GET api/cricket-matches/5
    [HttpGet("{id}")]
    public async Task<ActionResult<CricketMatchStateDto>> Get(int id)
    {
        var match = await _cricket.GetAsync(id);
        if (match == null) return NotFound("Match not found.");
        return Ok(_cricket.BuildState(match));
    }

    // GET api/cricket-matches/5/setup-options — presets and both squads, for the setup screen
    [HttpGet("{id}/setup-options")]
    public async Task<ActionResult<object>> GetSetupOptions(int id)
    {
        var match = await _db.CricketMatches.FirstOrDefaultAsync(m => m.Id == id);
        if (match == null) return NotFound("Match not found.");

        var teamIds = new[] { match.HomeTeamId, match.AwayTeamId }.Where(t => t.HasValue).Select(t => t!.Value).ToList();

        var teams = await _db.Teams
            .Where(t => teamIds.Contains(t.Id))
            .Include(t => t.Players).ThenInclude(p => p.Cricket)
            .ToListAsync();

        var lastSquads = new Dictionary<int, object?>();
        foreach (var team in teams)
            lastSquads[team.Id] = await LastSquadAsync(match, team);

        // A tournament is played to one format, so the last match set up is a better starting point
        // than a generic T20 — otherwise a 6-a-side league re-types "6" before every fixture.
        // No tracking: the rules are an owned type, and EF will only project one on its own when it
        // is not being tracked — which a read-only lookup never needs anyway.
        var lastFormat = await _db.CricketMatches
            .AsNoTracking()
            .Where(m => m.TournamentId == match.TournamentId && m.Id != id && m.Squad.Any())
            .OrderByDescending(m => m.StartedAt.HasValue)
            .ThenByDescending(m => m.StartedAt)
            .ThenByDescending(m => m.Id)
            .Select(m => new { m.MatchNumber, m.Rules })
            .FirstOrDefaultAsync();

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
        var previous = await _db.CricketMatches
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
            })
            .FirstOrDefaultAsync();

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

    // POST api/cricket-matches/5/setup — rules, toss and both XIs
    [HttpPost("{id}/setup")]
    public async Task<ActionResult<CricketMatchStateDto>> Setup(int id, [FromBody] CricketSetupRequest request) =>
        Respond(await _cricket.SetupAsync(id, request));

    // POST api/cricket-matches/5/innings/start
    [HttpPost("{id}/innings/start")]
    public async Task<ActionResult<CricketMatchStateDto>> StartInnings(int id, [FromBody] StartInningsRequest request) =>
        Respond(await _cricket.StartInningsAsync(id, request));

    // POST api/cricket-matches/5/balls — one delivery
    [HttpPost("{id}/balls")]
    public async Task<ActionResult<CricketMatchStateDto>> RecordBall(int id, [FromBody] RecordBallRequest request) =>
        Respond(await _cricket.RecordBallAsync(id, request));

    // POST api/cricket-matches/5/balls/undo
    [HttpPost("{id}/balls/undo")]
    public async Task<ActionResult<CricketMatchStateDto>> UndoBall(int id) =>
        Respond(await _cricket.UndoLastBallAsync(id));

    // POST api/cricket-matches/5/batter — the next batter in
    [HttpPost("{id}/batter")]
    public async Task<ActionResult<CricketMatchStateDto>> SetBatter(int id, [FromBody] NewBatterRequest request) =>
        Respond(await _cricket.SetBatterAsync(id, request));

    // POST api/cricket-matches/5/bowler — who bowls the next over
    [HttpPost("{id}/bowler")]
    public async Task<ActionResult<CricketMatchStateDto>> SetBowler(int id, [FromBody] NewBowlerRequest request) =>
        Respond(await _cricket.SetBowlerAsync(id, request));

    // POST api/cricket-matches/5/innings/end — a declaration or an abandonment
    [HttpPost("{id}/innings/end")]
    public async Task<ActionResult<CricketMatchStateDto>> EndInnings(int id, [FromBody] EndInningsRequest request) =>
        Respond(await _cricket.EndInningsAsync(id, request));

    // POST api/cricket-matches/5/follow-on
    [HttpPost("{id}/follow-on")]
    public async Task<ActionResult<CricketMatchStateDto>> EnforceFollowOn(int id) =>
        Respond(await _cricket.EnforceFollowOnAsync(id));

    // POST api/cricket-matches/5/super-over/start
    [HttpPost("{id}/super-over/start")]
    public async Task<ActionResult<CricketMatchStateDto>> StartSuperOver(int id) =>
        Respond(await _cricket.StartSuperOverAsync(id));

    // POST api/cricket-matches/5/complete
    [HttpPost("{id}/complete")]
    public async Task<ActionResult<CricketMatchStateDto>> Complete(
        int id, [FromBody] CompleteCricketMatchRequest? request = null) =>
        Respond(await _cricket.CompleteAsync(id, request ?? new CompleteCricketMatchRequest()));

    // GET api/cricket-matches/5/scorecard
    [HttpGet("{id}/scorecard")]
    public async Task<ActionResult<CricketScorecardDto>> Scorecard(int id)
    {
        var card = await _cricket.BuildScorecardAsync(id);
        if (card == null) return NotFound("Match not found.");
        return Ok(card);
    }

    // GET api/cricket-matches/5/balls — the whole ball-by-ball, oldest first
    [HttpGet("{id}/balls")]
    public async Task<ActionResult<object>> GetBalls(int id)
    {
        var match = await _cricket.GetAsync(id);
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

    // GET api/cricket-matches/5/events — the non-ball timeline
    [HttpGet("{id}/events")]
    public async Task<ActionResult<object>> GetEvents(int id)
    {
        var exists = await _db.CricketMatches.AnyAsync(m => m.Id == id);
        if (!exists) return NotFound("Match not found.");

        var events = await _db.CricketMatchEvents
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
            })
            .ToListAsync();

        return Ok(events);
    }
}
