using Microsoft.AspNetCore.Mvc;
using TournamentScheduler.Api.Auth.Permissions;
using TournamentScheduler.Api.Auth.TournamentAccess;
using TournamentScheduler.Api.Gateway;
using TournamentScheduler.Api.Http;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Services.Access;

namespace TournamentScheduler.Api.Controllers;

// A tournament's owners and scorers, and invites for people without an account yet. Route
// parameters: the tournament is {id}, a member {userId}, an invite {inviteId}.
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
    public async Task<ActionResult<ApiResponse<TournamentPeopleDto>>> List(int id) =>
        Respond(await _members.ListAsync());

    // POST api/tournaments/5/members — { contact (email or mobile number), role: "Owner" | "Scorer" }; changes the
    // role of someone already in, and invites someone with no account yet
    [HttpPost("members")]
    [ServiceRequestId("TOURNAMENT_MEMBER_ADD")]
    [TournamentAccess(TournamentPermission.ManageMembers)]
    public async Task<ActionResult<ApiResponse<TournamentPeopleDto>>> Add(int id, [FromBody] AddTournamentMemberRequest request) =>
        Respond(await _members.AddAsync(request));

    // DELETE api/tournaments/5/members/42
    [HttpDelete("members/{userId}")]
    [ServiceRequestId("TOURNAMENT_MEMBER_REMOVE")]
    [TournamentAccess(TournamentPermission.ManageMembers)]
    public async Task<ActionResult<ApiResponse<TournamentPeopleDto>>> Remove(int id, int userId) =>
        Respond(await _members.RemoveAsync(userId));

    // POST api/tournaments/5/invites/7/resend — sends it again and renews it for another 30 days
    [HttpPost("invites/{inviteId}/resend")]
    [ServiceRequestId("TOURNAMENT_INVITE_RESEND")]
    [TournamentAccess(TournamentPermission.ManageMembers)]
    public async Task<ActionResult<ApiResponse<TournamentPeopleDto>>> ResendInvite(int id, int inviteId) =>
        Respond(await _members.ResendInviteAsync(inviteId));

    // DELETE api/tournaments/5/invites/7 — signing up with it no longer adds them
    [HttpDelete("invites/{inviteId}")]
    [ServiceRequestId("TOURNAMENT_INVITE_CANCEL")]
    [TournamentAccess(TournamentPermission.ManageMembers)]
    public async Task<ActionResult<ApiResponse<TournamentPeopleDto>>> CancelInvite(int id, int inviteId) =>
        Respond(await _members.CancelInviteAsync(inviteId));

    // POST api/tournaments/5/leave — anyone but the creator
    [HttpPost("leave")]
    [ServiceRequestId("TOURNAMENT_LEAVE")]
    [TournamentAccess(TournamentPermission.View)]
    public async Task<ActionResult<ApiResponse<object>>> Leave(int id) =>
        Respond(await _members.LeaveAsync(), _ => Ok());
}
