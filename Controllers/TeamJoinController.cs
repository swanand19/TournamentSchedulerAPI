using Microsoft.AspNetCore.Mvc;
using TournamentScheduler.Api.Auth.Permissions;
using TournamentScheduler.Api.Auth.TournamentAccess;
using TournamentScheduler.Api.Gateway;
using TournamentScheduler.Api.Http;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Services.Access;

namespace TournamentScheduler.Api.Controllers;

/// <summary>
/// Joining a team with the code its organiser shared. Not tournament-scoped: the person isn't in
/// the tournament yet — the code is what finds it.
/// </summary>
[ApiController]
[Route("api/teams/join")]
[DefaultRoles(RoleIds.EndUser)] // every signed-up user; Admin is ticked too
[NotTournamentScoped]
public class TeamJoinController : ApiController
{
    private readonly ITeamJoinService _join;

    public TeamJoinController(ITeamJoinService join)
    {
        _join = join;
    }

    // POST api/teams/join/preview — { code } → the team and the names nobody has claimed
    [HttpPost("preview")]
    [ServiceRequestId("TEAM_JOIN_PREVIEW")]
    public async Task<ActionResult<ApiResponse<JoinTeamPreviewDto>>> Preview([FromBody] JoinTeamPreviewRequest request) =>
        Respond(await _join.PreviewAsync(request));

    // POST api/teams/join — { code, playerId } (playerId null: join as a new player)
    [HttpPost]
    [ServiceRequestId("TEAM_JOIN")]
    public async Task<ActionResult<ApiResponse<JoinTeamResult>>> Join([FromBody] JoinTeamRequest request) =>
        Respond(await _join.JoinAsync(request));
}
