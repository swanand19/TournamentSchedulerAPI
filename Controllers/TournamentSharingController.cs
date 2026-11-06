using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TournamentScheduler.Api.Auth.Permissions;
using TournamentScheduler.Api.Auth.TournamentAccess;
using TournamentScheduler.Api.Gateway;
using TournamentScheduler.Api.Http;
using TournamentScheduler.Api.Models;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Models.Public;
using TournamentScheduler.Api.Services.Public;

namespace TournamentScheduler.Api.Controllers;

/// <summary>
/// Public live scores (6E), the signed-in side: owners switch a tournament public or not; anyone
/// signed in finds public ones by name or code and follows them, and stops.
/// </summary>
[ApiController]
[DefaultRoles(RoleIds.EndUser)]
public class TournamentSharingController : ApiController
{
    private readonly ITournamentSharingService _sharing;

    public TournamentSharingController(ITournamentSharingService sharing) => _sharing = sharing;

    // POST api/tournaments/5/public — { isPublic }: "anyone with the link can watch"; answers the tournament with its link
    [HttpPost("api/tournaments/{id}/public")]
    [ServiceRequestId("TOURNAMENT_PUBLIC_SET")]
    [TournamentAccess(TournamentPermission.ManageMembers)]
    public async Task<ActionResult<ApiResponse<Tournament>>> SetPublic(int id, [FromBody] SetPublicRequest request) =>
        Respond(await _sharing.SetPublicAsync(id, request.IsPublic));

    // GET api/follows/search?q= — public tournaments by name (3+ letters) or code; limited per address like the public pages
    [HttpGet("api/follows/search")]
    [ServiceRequestId("FOLLOW_SEARCH")]
    [NotTournamentScoped]
    [EnableRateLimiting(PublicRateLimit.Policy)]
    public async Task<ActionResult<ApiResponse<List<PublicSearchResultDto>>>> Search([FromQuery] string? q) =>
        Respond(await _sharing.SearchAsync(q));

    // POST api/follows — { code } (or the whole link): follow a public tournament
    [HttpPost("api/follows")]
    [ServiceRequestId("FOLLOW_ADD")]
    [NotTournamentScoped]
    public async Task<ActionResult<ApiResponse<FollowedDto>>> Follow([FromBody] FollowRequest request) =>
        Respond(await _sharing.FollowAsync(request.Code));

    // DELETE api/follows/5 — stop following (works after it stops being public too)
    [HttpDelete("api/follows/{tournamentId}")]
    [ServiceRequestId("FOLLOW_REMOVE")]
    [NotTournamentScoped]
    public async Task<ActionResult<ApiResponse<object>>> Unfollow(int tournamentId) =>
        Respond(await _sharing.UnfollowAsync(tournamentId));
}
