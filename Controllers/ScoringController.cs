using Microsoft.AspNetCore.Mvc;
using TournamentScheduler.Api.Auth.Permissions;
using TournamentScheduler.Api.Auth.TournamentAccess;
using TournamentScheduler.Api.Gateway;
using TournamentScheduler.Api.Http;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Services.Access;

namespace TournamentScheduler.Api.Controllers;

/// <summary>
/// Handing a match's scoring between people — one set of services for both sports. Route
/// parameters: {sport} is "football" or "cricket", {matchId} the match. Each answers with the
/// match's <c>scoring</c> block.
/// </summary>
[ApiController]
[Route("api/scoring/{sport}/{matchId}")]
[DefaultRoles(RoleIds.EndUser)] // every signed-up user; Admin is ticked too
public class ScoringController : ApiController
{
    private readonly IScoringLockService _lock;

    public ScoringController(IScoringLockService scoringLock)
    {
        _lock = scoringLock;
    }

    // POST api/scoring/cricket/5/request — ask the active scorer to hand over (yours at once if nobody is scoring, or they've gone quiet)
    [HttpPost("request")]
    [ServiceRequestId("SCORING_REQUEST")]
    [TournamentAccess(TournamentPermission.Score, From = TournamentScope.SportMatch, IgnoresScorerLock = true)]
    public async Task<ActionResult<ApiResponse<ScoringDto>>> RequestScoring(string sport, int matchId) =>
        Respond(await _lock.RequestAsync());

    // POST api/scoring/cricket/5/respond — { allow: true | false }, the active scorer's answer
    [HttpPost("respond")]
    [ServiceRequestId("SCORING_RESPOND")]
    [TournamentAccess(TournamentPermission.Score, From = TournamentScope.SportMatch, IgnoresScorerLock = true)]
    public async Task<ActionResult<ApiResponse<ScoringDto>>> RespondToRequest(string sport, int matchId, [FromBody] RespondToScoringRequest request) =>
        Respond(await _lock.RespondAsync(request));

    // POST api/scoring/cricket/5/take-over — owners only, without asking
    [HttpPost("take-over")]
    [ServiceRequestId("SCORING_TAKE_OVER")]
    [TournamentAccess(TournamentPermission.Score, From = TournamentScope.SportMatch, IgnoresScorerLock = true)]
    public async Task<ActionResult<ApiResponse<ScoringDto>>> TakeOverScoring(string sport, int matchId) =>
        Respond(await _lock.TakeOverAsync());
}
