using Microsoft.AspNetCore.Mvc;
using TournamentScheduler.Api.Gateway;
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
    [HttpGet("{matchId}")]
    [ServiceRequestId("MATCH_GET")]
    public async Task<ActionResult<ApiResponse<object>>> GetMatch(int matchId) =>
        Respond(await _football.GetMatch(matchId));

    // POST api/matches/5/setup — configures the match and immediately starts it
    [HttpPost("{matchId}/setup")]
    [ServiceRequestId("MATCH_SETUP")]
    public async Task<ActionResult<ApiResponse<object>>> SetupAndStart(int matchId, [FromBody] StartMatchRequest request) =>
        Respond(await _football.SetupAndStart(matchId, request));

    // POST api/matches/5/events — register a goal, card, or substitution
    [HttpPost("{matchId}/events")]
    [ServiceRequestId("MATCH_EVENT_RECORD")]
    public async Task<ActionResult<ApiResponse<object>>> RecordEvent(int matchId, [FromBody] RecordEventRequest request) =>
        Respond(await _football.RecordEvent(matchId, request));

    // GET api/matches/5/events — full timeline, oldest first
    [HttpGet("{matchId}/events")]
    [ServiceRequestId("MATCH_EVENTS")]
    public async Task<ActionResult<ApiResponse<object>>> GetEvents(int matchId) =>
        Respond(await _football.GetEvents(matchId));

    // POST api/matches/5/clock/pause — stop play mid-period (injury, incident). The period is NOT over.
    [HttpPost("{matchId}/clock/pause")]
    [ServiceRequestId("MATCH_CLOCK_PAUSE")]
    public async Task<ActionResult<ApiResponse<object>>> PauseClock(int matchId) =>
        Respond(await _football.PauseClock(matchId));

    // POST api/matches/5/clock/resume — restart play after a mid-period stoppage.
    [HttpPost("{matchId}/clock/resume")]
    [ServiceRequestId("MATCH_CLOCK_RESUME")]
    public async Task<ActionResult<ApiResponse<object>>> ResumeClock(int matchId) =>
        Respond(await _football.ResumeClock(matchId));

    // POST api/matches/5/clock/add-time
    [HttpPost("{matchId}/clock/add-time")]
    [ServiceRequestId("MATCH_CLOCK_ADD_TIME")]
    public async Task<ActionResult<ApiResponse<object>>> AddTime(int matchId, [FromBody] AddTimeRequest request) =>
        Respond(await _football.AddTime(matchId, request));

    // POST api/matches/5/half/end — the referee's whistle for the end of the current period.
    [HttpPost("{matchId}/half/end")]
    [ServiceRequestId("MATCH_PERIOD_END")]
    public async Task<ActionResult<ApiResponse<object>>> EndPeriod(int matchId) =>
        Respond(await _football.EndPeriod(matchId));

    // POST api/matches/5/half/next — kick off the next period. Requires the current one to be ended.
    [HttpPost("{matchId}/half/next")]
    [ServiceRequestId("MATCH_PERIOD_NEXT")]
    public async Task<ActionResult<ApiResponse<object>>> NextHalf(int matchId) =>
        Respond(await _football.NextHalf(matchId));

    // POST api/matches/5/complete
    [HttpPost("{matchId}/complete")]
    [ServiceRequestId("MATCH_COMPLETE")]
    public async Task<ActionResult<ApiResponse<object>>> CompleteMatch(int matchId, [FromBody] CompleteMatchRequest? request = null) =>
        Respond(await _football.CompleteMatch(matchId, request));

    // POST api/matches/5/penalties/start
    [HttpPost("{matchId}/penalties/start")]
    [ServiceRequestId("MATCH_PENALTIES_START")]
    public async Task<ActionResult<ApiResponse<object>>> StartPenalties(int matchId, [FromBody] StartPenaltiesRequest request) =>
        Respond(await _football.StartPenalties(matchId, request));

    // GET api/matches/5/penalties
    [HttpGet("{matchId}/penalties")]
    [ServiceRequestId("MATCH_PENALTIES_GET")]
    public async Task<ActionResult<ApiResponse<object>>> GetPenalties(int matchId) =>
        Respond(await _football.GetPenalties(matchId));

    // POST api/matches/5/penalties/kick
    [HttpPost("{matchId}/penalties/kick")]
    [ServiceRequestId("MATCH_PENALTY_KICK")]
    public async Task<ActionResult<ApiResponse<object>>> RecordPenaltyKick(int matchId, [FromBody] RecordPenaltyKickRequest request) =>
        Respond(await _football.RecordPenaltyKick(matchId, request));

    // POST api/matches/5/penalties/end — manual override, declare a winner directly
    [HttpPost("{matchId}/penalties/end")]
    [ServiceRequestId("MATCH_PENALTIES_END")]
    public async Task<ActionResult<ApiResponse<object>>> EndPenaltiesManually(int matchId, [FromBody] EndPenaltiesRequest request) =>
        Respond(await _football.EndPenaltiesManually(matchId, request));
}
