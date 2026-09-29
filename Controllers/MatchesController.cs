using Microsoft.AspNetCore.Mvc;
using TournamentScheduler.Api.Http;
using TournamentScheduler.Api.Models;
using TournamentScheduler.Api.Services.Football;

namespace TournamentScheduler.Api.Controllers;

[ApiController]
[Route("api/matches")]
public class MatchesController : ApiController
{
    private readonly IFootballMatchService _football;

    public MatchesController(IFootballMatchService football)
    {
        _football = football;
    }

    // GET api/matches/5
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<object>>> GetMatch(int id) =>
        Respond(await _football.GetMatch(id));

    // POST api/matches/5/setup — configures the match and immediately starts it
    [HttpPost("{id}/setup")]
    public async Task<ActionResult<ApiResponse<object>>> SetupAndStart(int id, [FromBody] StartMatchRequest request) =>
        Respond(await _football.SetupAndStart(id, request));

    // POST api/matches/5/events — register a goal, card, or substitution
    [HttpPost("{id}/events")]
    public async Task<ActionResult<ApiResponse<object>>> RecordEvent(int id, [FromBody] RecordEventRequest request) =>
        Respond(await _football.RecordEvent(id, request));

    // GET api/matches/5/events — full timeline, oldest first
    [HttpGet("{id}/events")]
    public async Task<ActionResult<ApiResponse<object>>> GetEvents(int id) =>
        Respond(await _football.GetEvents(id));

    // POST api/matches/5/clock/pause — stop play mid-period (injury, incident). The period is NOT over.
    [HttpPost("{id}/clock/pause")]
    public async Task<ActionResult<ApiResponse<object>>> PauseClock(int id) =>
        Respond(await _football.PauseClock(id));

    // POST api/matches/5/clock/resume — restart play after a mid-period stoppage.
    [HttpPost("{id}/clock/resume")]
    public async Task<ActionResult<ApiResponse<object>>> ResumeClock(int id) =>
        Respond(await _football.ResumeClock(id));

    // POST api/matches/5/clock/add-time
    [HttpPost("{id}/clock/add-time")]
    public async Task<ActionResult<ApiResponse<object>>> AddTime(int id, [FromBody] AddTimeRequest request) =>
        Respond(await _football.AddTime(id, request));

    // POST api/matches/5/half/end — the referee's whistle for the end of the current period.
    [HttpPost("{id}/half/end")]
    public async Task<ActionResult<ApiResponse<object>>> EndPeriod(int id) =>
        Respond(await _football.EndPeriod(id));

    // POST api/matches/5/half/next — kick off the next period. Requires the current one to be ended.
    [HttpPost("{id}/half/next")]
    public async Task<ActionResult<ApiResponse<object>>> NextHalf(int id) =>
        Respond(await _football.NextHalf(id));

    // POST api/matches/5/complete
    [HttpPost("{id}/complete")]
    public async Task<ActionResult<ApiResponse<object>>> CompleteMatch(int id, [FromBody] CompleteMatchRequest? request = null) =>
        Respond(await _football.CompleteMatch(id, request));

    // POST api/matches/5/penalties/start
    [HttpPost("{id}/penalties/start")]
    public async Task<ActionResult<ApiResponse<object>>> StartPenalties(int id, [FromBody] StartPenaltiesRequest request) =>
        Respond(await _football.StartPenalties(id, request));

    // GET api/matches/5/penalties
    [HttpGet("{id}/penalties")]
    public async Task<ActionResult<ApiResponse<object>>> GetPenalties(int id) =>
        Respond(await _football.GetPenalties(id));

    // POST api/matches/5/penalties/kick
    [HttpPost("{id}/penalties/kick")]
    public async Task<ActionResult<ApiResponse<object>>> RecordPenaltyKick(int id, [FromBody] RecordPenaltyKickRequest request) =>
        Respond(await _football.RecordPenaltyKick(id, request));

    // POST api/matches/5/penalties/end — manual override, declare a winner directly
    [HttpPost("{id}/penalties/end")]
    public async Task<ActionResult<ApiResponse<object>>> EndPenaltiesManually(int id, [FromBody] EndPenaltiesRequest request) =>
        Respond(await _football.EndPenaltiesManually(id, request));
}
