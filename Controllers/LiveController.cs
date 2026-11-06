using Microsoft.AspNetCore.Mvc;
using TournamentScheduler.Api.Auth.Permissions;
using TournamentScheduler.Api.Auth.TournamentAccess;
using TournamentScheduler.Api.Gateway;
using TournamentScheduler.Api.Http;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Models.Live;
using TournamentScheduler.Api.Services.Access;
using TournamentScheduler.Api.Services.Live;

namespace TournamentScheduler.Api.Controllers;

/// <summary>
/// Live updates: a ticket that opens the live line (<c>/hubs/live</c>), and what the line hears about.
/// {connectionId} is the line's id, which the hub says when it opens.
/// </summary>
[ApiController]
[Route("api/live")]
[DefaultRoles(RoleIds.EndUser)]
public class LiveController : ApiController
{
    private readonly ILiveService _live;

    public LiveController(ILiveService live) => _live = live;

    // POST api/live/ticket — one minute, once: opens the line as you
    [HttpPost("ticket")]
    [ServiceRequestId("LIVE_TICKET")]
    [NotTournamentScoped]
    public ActionResult<ApiResponse<LiveTicketDto>> Ticket() => Respond(_live.Ticket());

    // POST api/live/{connectionId}/tournaments/5 — hear when anything in the tournament changes
    [HttpPost("{connectionId}/tournaments/{id}")]
    [ServiceRequestId("LIVE_WATCH_TOURNAMENT")]
    [TournamentAccess(TournamentPermission.View)]
    public async Task<ActionResult<ApiResponse<object>>> WatchTournament(string connectionId, int id) =>
        Respond(await _live.WatchTournamentAsync(connectionId));

    // POST api/live/{connectionId}/matches/cricket/12 — hear when this match changes
    [HttpPost("{connectionId}/matches/{sport}/{matchId}")]
    [ServiceRequestId("LIVE_WATCH_MATCH")]
    [TournamentAccess(TournamentPermission.View, From = TournamentScope.SportMatch)]
    public async Task<ActionResult<ApiResponse<object>>> WatchMatch(string connectionId, string sport, int matchId) =>
        Respond(await _live.WatchMatchAsync(connectionId));

    // POST api/live/{connectionId}/unwatch — { tournamentId } or { sport, matchId }
    [HttpPost("{connectionId}/unwatch")]
    [ServiceRequestId("LIVE_UNWATCH")]
    [NotTournamentScoped]
    public async Task<ActionResult<ApiResponse<object>>> Unwatch(string connectionId, [FromBody] LiveUnwatchRequest request) =>
        Respond(await _live.UnwatchAsync(connectionId, request));
}
