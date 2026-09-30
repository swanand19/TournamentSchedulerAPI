using Microsoft.AspNetCore.Mvc;
using TournamentScheduler.Api.Gateway;
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
    [HttpGet("{matchId}")]
    [ServiceRequestId("CRICKET_MATCH_GET")]
    public async Task<ActionResult<ApiResponse<CricketMatchStateDto>>> Get(int matchId)
    {
        var match = await _cricket.GetAsync(matchId);
        if (match == null) return NotFound("Match not found.");
        return Ok(_cricket.BuildState(match));
    }

    // GET api/cricket-matches/5/setup-options — presets and both squads, for the setup screen
    [HttpGet("{matchId}/setup-options")]
    [ServiceRequestId("CRICKET_SETUP_OPTIONS")]
    public async Task<ActionResult<ApiResponse<object>>> GetSetupOptions(int matchId) =>
        Respond(await _queries.GetSetupOptionsAsync(matchId));

    // POST api/cricket-matches/5/setup — rules, toss and both XIs
    [HttpPost("{matchId}/setup")]
    [ServiceRequestId("CRICKET_SETUP")]
    public async Task<ActionResult<ApiResponse<CricketMatchStateDto>>> Setup(int matchId, [FromBody] CricketSetupRequest request) =>
        Respond(await _cricket.SetupAsync(matchId, request));

    // POST api/cricket-matches/5/innings/start
    [HttpPost("{matchId}/innings/start")]
    [ServiceRequestId("CRICKET_INNINGS_START")]
    public async Task<ActionResult<ApiResponse<CricketMatchStateDto>>> StartInnings(int matchId, [FromBody] StartInningsRequest request) =>
        Respond(await _cricket.StartInningsAsync(matchId, request));

    // POST api/cricket-matches/5/balls — one delivery
    [HttpPost("{matchId}/balls")]
    [ServiceRequestId("CRICKET_BALL_RECORD")]
    public async Task<ActionResult<ApiResponse<CricketMatchStateDto>>> RecordBall(int matchId, [FromBody] RecordBallRequest request) =>
        Respond(await _cricket.RecordBallAsync(matchId, request));

    // POST api/cricket-matches/5/balls/undo
    [HttpPost("{matchId}/balls/undo")]
    [ServiceRequestId("CRICKET_BALL_UNDO")]
    public async Task<ActionResult<ApiResponse<CricketMatchStateDto>>> UndoBall(int matchId) =>
        Respond(await _cricket.UndoLastBallAsync(matchId));

    // POST api/cricket-matches/5/batter — the next batter in
    [HttpPost("{matchId}/batter")]
    [ServiceRequestId("CRICKET_BATTER_SET")]
    public async Task<ActionResult<ApiResponse<CricketMatchStateDto>>> SetBatter(int matchId, [FromBody] NewBatterRequest request) =>
        Respond(await _cricket.SetBatterAsync(matchId, request));

    // POST api/cricket-matches/5/bowler — who bowls the next over
    [HttpPost("{matchId}/bowler")]
    [ServiceRequestId("CRICKET_BOWLER_SET")]
    public async Task<ActionResult<ApiResponse<CricketMatchStateDto>>> SetBowler(int matchId, [FromBody] NewBowlerRequest request) =>
        Respond(await _cricket.SetBowlerAsync(matchId, request));

    // POST api/cricket-matches/5/innings/end — a declaration or an abandonment
    [HttpPost("{matchId}/innings/end")]
    [ServiceRequestId("CRICKET_INNINGS_END")]
    public async Task<ActionResult<ApiResponse<CricketMatchStateDto>>> EndInnings(int matchId, [FromBody] EndInningsRequest request) =>
        Respond(await _cricket.EndInningsAsync(matchId, request));

    // POST api/cricket-matches/5/innings/reduce-overs — rain: take overs off the innings in play
    [HttpPost("{matchId}/innings/reduce-overs")]
    [ServiceRequestId("CRICKET_REDUCE_OVERS")]
    public async Task<ActionResult<ApiResponse<CricketMatchStateDto>>> ReduceOvers(int matchId, [FromBody] ReduceOversRequest request) =>
        Respond(await _cricket.ReduceOversAsync(matchId, request));

    // POST api/cricket-matches/5/follow-on
    [HttpPost("{matchId}/follow-on")]
    [ServiceRequestId("CRICKET_FOLLOW_ON")]
    public async Task<ActionResult<ApiResponse<CricketMatchStateDto>>> EnforceFollowOn(int matchId) =>
        Respond(await _cricket.EnforceFollowOnAsync(matchId));

    // POST api/cricket-matches/5/super-over/start
    [HttpPost("{matchId}/super-over/start")]
    [ServiceRequestId("CRICKET_SUPER_OVER_START")]
    public async Task<ActionResult<ApiResponse<CricketMatchStateDto>>> StartSuperOver(int matchId) =>
        Respond(await _cricket.StartSuperOverAsync(matchId));

    // POST api/cricket-matches/5/complete
    [HttpPost("{matchId}/complete")]
    [ServiceRequestId("CRICKET_COMPLETE")]
    public async Task<ActionResult<ApiResponse<CricketMatchStateDto>>> Complete(
        int matchId, [FromBody] CompleteCricketMatchRequest? request = null) =>
        Respond(await _cricket.CompleteAsync(matchId, request ?? new CompleteCricketMatchRequest()));

    // GET api/cricket-matches/5/scorecard
    [HttpGet("{matchId}/scorecard")]
    [ServiceRequestId("CRICKET_SCORECARD")]
    public async Task<ActionResult<ApiResponse<CricketScorecardDto>>> Scorecard(int matchId)
    {
        var card = await _cricket.BuildScorecardAsync(matchId);
        if (card == null) return NotFound("Match not found.");
        return Ok(card);
    }

    // GET api/cricket-matches/5/awards — player of the match and the best of each discipline
    [HttpGet("{matchId}/awards")]
    [ServiceRequestId("CRICKET_AWARDS")]
    public async Task<ActionResult<ApiResponse<CricketMatchAwardsDto>>> Awards(int matchId) =>
        Respond(await _queries.GetAwardsAsync(matchId));

    // GET api/cricket-matches/5/balls — the whole ball-by-ball, oldest first
    [HttpGet("{matchId}/balls")]
    [ServiceRequestId("CRICKET_BALLS")]
    public async Task<ActionResult<ApiResponse<object>>> GetBalls(int matchId) =>
        Respond(await _queries.GetBallsAsync(matchId));

    // GET api/cricket-matches/5/events — the non-ball timeline
    [HttpGet("{matchId}/events")]
    [ServiceRequestId("CRICKET_EVENTS")]
    public async Task<ActionResult<ApiResponse<object>>> GetEvents(int matchId) =>
        Respond(await _queries.GetEventsAsync(matchId));
}
