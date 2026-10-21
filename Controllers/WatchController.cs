using Microsoft.AspNetCore.Mvc;
using TournamentScheduler.Api.Auth.Permissions;
using TournamentScheduler.Api.Auth.TournamentAccess;
using TournamentScheduler.Api.Gateway;
using TournamentScheduler.Api.Http;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Models.Watch;
using TournamentScheduler.Api.Services.Access;
using TournamentScheduler.Api.Services.Watch;

namespace TournamentScheduler.Api.Controllers;

/// <summary>
/// Watching a match (Round 2), for both sports: {sport} is "football" or "cricket". Anyone who may
/// view the tournament may watch; the score itself comes from the sport's own match answer.
/// </summary>
[ApiController]
[Route("api/watch/{sport}/{matchId}")]
[DefaultRoles(RoleIds.EndUser)]
public class WatchController : ApiController
{
    private readonly IWatchService _watch;

    public WatchController(IWatchService watch) => _watch = watch;

    // GET api/watch/cricket/5 — the header and both sides: squads before the start, line-ups after
    [HttpGet]
    [ServiceRequestId("WATCH_MATCH")]
    [TournamentAccess(TournamentPermission.View, From = TournamentScope.SportMatch)]
    public async Task<ActionResult<ApiResponse<WatchMatchDto>>> Match(string sport, int matchId) =>
        Respond(await _watch.GetMatchAsync(sport, matchId));

    // GET api/watch/cricket/5/activity?before=&take= — what happened, newest first
    [HttpGet("activity")]
    [ServiceRequestId("WATCH_ACTIVITY")]
    [TournamentAccess(TournamentPermission.View, From = TournamentScope.SportMatch)]
    public async Task<ActionResult<ApiResponse<ActivityPageDto>>> Activity(string sport, int matchId, [FromQuery] int? before, [FromQuery] int? take) =>
        Respond(await _watch.GetActivityAsync(sport, matchId, before, take));
}
