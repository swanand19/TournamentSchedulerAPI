using Microsoft.AspNetCore.Mvc;
using TournamentScheduler.Api.Http;
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
public class CricketMatchesController : ApiController
{
    private readonly ICricketScoringService _cricket;
    private readonly ICricketMatchQueryService _queries;

    public CricketMatchesController(ICricketScoringService cricket, ICricketMatchQueryService queries)
    {
        _cricket = cricket;
        _queries = queries;
    }

    /// <summary>Turns an engine result into the right HTTP answer, or the state on success.</summary>
    private ActionResult Respond(CricketResult<CricketMatch> result)
    {
        if (result.NotFound) return NotFound(result.Error);
        if (result.Error != null) return BadRequest(result.Error);
        return Ok(_cricket.BuildState(result.Value!));
    }

    // GET api/cricket-matches/5
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<CricketMatchStateDto>>> Get(int id)
    {
        var match = await _cricket.GetAsync(id);
        if (match == null) return NotFound("Match not found.");
        return Ok(_cricket.BuildState(match));
    }

    // GET api/cricket-matches/5/setup-options — presets and both squads, for the setup screen
    [HttpGet("{id}/setup-options")]
    public async Task<ActionResult<ApiResponse<object>>> GetSetupOptions(int id) =>
        Respond(await _queries.GetSetupOptionsAsync(id));

    // POST api/cricket-matches/5/setup — rules, toss and both XIs
    [HttpPost("{id}/setup")]
    public async Task<ActionResult<ApiResponse<CricketMatchStateDto>>> Setup(int id, [FromBody] CricketSetupRequest request) =>
        Respond(await _cricket.SetupAsync(id, request));

    // POST api/cricket-matches/5/innings/start
    [HttpPost("{id}/innings/start")]
    public async Task<ActionResult<ApiResponse<CricketMatchStateDto>>> StartInnings(int id, [FromBody] StartInningsRequest request) =>
        Respond(await _cricket.StartInningsAsync(id, request));

    // POST api/cricket-matches/5/balls — one delivery
    [HttpPost("{id}/balls")]
    public async Task<ActionResult<ApiResponse<CricketMatchStateDto>>> RecordBall(int id, [FromBody] RecordBallRequest request) =>
        Respond(await _cricket.RecordBallAsync(id, request));

    // POST api/cricket-matches/5/balls/undo
    [HttpPost("{id}/balls/undo")]
    public async Task<ActionResult<ApiResponse<CricketMatchStateDto>>> UndoBall(int id) =>
        Respond(await _cricket.UndoLastBallAsync(id));

    // POST api/cricket-matches/5/batter — the next batter in
    [HttpPost("{id}/batter")]
    public async Task<ActionResult<ApiResponse<CricketMatchStateDto>>> SetBatter(int id, [FromBody] NewBatterRequest request) =>
        Respond(await _cricket.SetBatterAsync(id, request));

    // POST api/cricket-matches/5/bowler — who bowls the next over
    [HttpPost("{id}/bowler")]
    public async Task<ActionResult<ApiResponse<CricketMatchStateDto>>> SetBowler(int id, [FromBody] NewBowlerRequest request) =>
        Respond(await _cricket.SetBowlerAsync(id, request));

    // POST api/cricket-matches/5/innings/end — a declaration or an abandonment
    [HttpPost("{id}/innings/end")]
    public async Task<ActionResult<ApiResponse<CricketMatchStateDto>>> EndInnings(int id, [FromBody] EndInningsRequest request) =>
        Respond(await _cricket.EndInningsAsync(id, request));

    // POST api/cricket-matches/5/innings/reduce-overs — rain: take overs off the innings in play
    [HttpPost("{id}/innings/reduce-overs")]
    public async Task<ActionResult<ApiResponse<CricketMatchStateDto>>> ReduceOvers(int id, [FromBody] ReduceOversRequest request) =>
        Respond(await _cricket.ReduceOversAsync(id, request));

    // POST api/cricket-matches/5/follow-on
    [HttpPost("{id}/follow-on")]
    public async Task<ActionResult<ApiResponse<CricketMatchStateDto>>> EnforceFollowOn(int id) =>
        Respond(await _cricket.EnforceFollowOnAsync(id));

    // POST api/cricket-matches/5/super-over/start
    [HttpPost("{id}/super-over/start")]
    public async Task<ActionResult<ApiResponse<CricketMatchStateDto>>> StartSuperOver(int id) =>
        Respond(await _cricket.StartSuperOverAsync(id));

    // POST api/cricket-matches/5/complete
    [HttpPost("{id}/complete")]
    public async Task<ActionResult<ApiResponse<CricketMatchStateDto>>> Complete(
        int id, [FromBody] CompleteCricketMatchRequest? request = null) =>
        Respond(await _cricket.CompleteAsync(id, request ?? new CompleteCricketMatchRequest()));

    // GET api/cricket-matches/5/scorecard
    [HttpGet("{id}/scorecard")]
    public async Task<ActionResult<ApiResponse<CricketScorecardDto>>> Scorecard(int id)
    {
        var card = await _cricket.BuildScorecardAsync(id);
        if (card == null) return NotFound("Match not found.");
        return Ok(card);
    }

    // GET api/cricket-matches/5/awards — player of the match and the best of each discipline
    [HttpGet("{id}/awards")]
    public async Task<ActionResult<ApiResponse<CricketMatchAwardsDto>>> Awards(int id) =>
        Respond(await _queries.GetAwardsAsync(id));

    // GET api/cricket-matches/5/balls — the whole ball-by-ball, oldest first
    [HttpGet("{id}/balls")]
    public async Task<ActionResult<ApiResponse<object>>> GetBalls(int id) =>
        Respond(await _queries.GetBallsAsync(id));

    // GET api/cricket-matches/5/events — the non-ball timeline
    [HttpGet("{id}/events")]
    public async Task<ActionResult<ApiResponse<object>>> GetEvents(int id) =>
        Respond(await _queries.GetEventsAsync(id));
}
