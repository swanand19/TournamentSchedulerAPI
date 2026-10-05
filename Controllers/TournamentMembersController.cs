using Microsoft.AspNetCore.Mvc;
using TournamentScheduler.Api.Auth.Permissions;
using TournamentScheduler.Api.Auth.TournamentAccess;
using TournamentScheduler.Api.Gateway;
using TournamentScheduler.Api.Http;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Services.Access;

namespace TournamentScheduler.Api.Controllers;

// A tournament's owners and scorers. Route parameters: the tournament is {id}, a member {userId}.
[ApiController]
[Route("api/tournaments/{id}")]
[DefaultRoles(RoleIds.EndUser)] // every signed-up user; Admin is ticked too
public class TournamentMembersController : ApiController
{
    private readonly ITournamentMemberService _members;

    public TournamentMembersController(ITournamentMemberService members)
    {
        _members = members;
    }

    // GET api/tournaments/5/members
    [HttpGet("members")]
    [ServiceRequestId("TOURNAMENT_MEMBER_LIST")]
    [TournamentAccess(TournamentPermission.View)]
    public async Task<ActionResult<ApiResponse<List<TournamentMemberDto>>>> List(int id) =>
        Respond(await _members.ListAsync());

    // POST api/tournaments/5/members — { email, role: "Owner" | "Scorer" }; changes the role of someone already in
    [HttpPost("members")]
    [ServiceRequestId("TOURNAMENT_MEMBER_ADD")]
    [TournamentAccess(TournamentPermission.ManageMembers)]
    public async Task<ActionResult<ApiResponse<List<TournamentMemberDto>>>> Add(int id, [FromBody] AddTournamentMemberRequest request) =>
        Respond(await _members.AddAsync(request));

    // DELETE api/tournaments/5/members/42
    [HttpDelete("members/{userId}")]
    [ServiceRequestId("TOURNAMENT_MEMBER_REMOVE")]
    [TournamentAccess(TournamentPermission.ManageMembers)]
    public async Task<ActionResult<ApiResponse<List<TournamentMemberDto>>>> Remove(int id, int userId) =>
        Respond(await _members.RemoveAsync(userId));

    // POST api/tournaments/5/leave — anyone but the creator
    [HttpPost("leave")]
    [ServiceRequestId("TOURNAMENT_LEAVE")]
    [TournamentAccess(TournamentPermission.View)]
    public async Task<ActionResult<ApiResponse<object>>> Leave(int id) =>
        Respond(await _members.LeaveAsync(), _ => Ok());
}
