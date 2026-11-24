using Microsoft.AspNetCore.Mvc;
using TournamentScheduler.Api.Auth.TournamentAccess;
using TournamentScheduler.Api.Auth.Permissions;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Gateway;
using TournamentScheduler.Api.Http;
using TournamentScheduler.Api.Models;
using TournamentScheduler.Api.Services;

namespace TournamentScheduler.Api.Controllers;

// Route parameter names are part of the gateway contract (the apps fill them by name):
// the tournament is {id}, then {teamId} and {playerId}.
// A squad can be changed by the tournament's owners and by that team's admins (ManageTeam, 2026-11-20); adding
// or deleting teams, and naming admins, stay with the owners.
[ApiController]
[Route("api/tournaments/{id}/teams")]
[DefaultRoles(RoleIds.EndUser)] // every signed-up user; Admin is ticked too
public class TeamsController : ApiController
{
    private readonly ITeamService _teams;

    public TeamsController(ITeamService teams)
    {
        _teams = teams;
    }

    // GET api/tournaments/5/teams
    [HttpGet]
    [ServiceRequestId("TEAM_LIST")]
    [TournamentAccess(TournamentPermission.View)]
    public async Task<ActionResult<ApiResponse<List<Team>>>> GetTeams(int id) =>
        Respond(await _teams.GetTeamsAsync(id));

    // GET api/tournaments/5/teams/9
    [HttpGet("{teamId}")]
    [ServiceRequestId("TEAM_GET")]
    [TournamentAccess(TournamentPermission.View)]
    public async Task<ActionResult<ApiResponse<Team>>> GetTeam(int id, int teamId) =>
        Respond(await _teams.GetTeamAsync(id, teamId));

    // POST api/tournaments/5/teams
    [HttpPost]
    [ServiceRequestId("TEAM_CREATE")]
    [TournamentAccess(TournamentPermission.Edit)]
    public async Task<ActionResult<ApiResponse<Team>>> CreateTeam(int id, [FromBody] CreateTeamRequest request) =>
        Respond(await _teams.CreateTeamAsync(id, request),
            team => CreatedAtAction(nameof(GetTeam), new { id, teamId = team.Id }, team));

    // PUT api/tournaments/5/teams/9
    [HttpPut("{teamId}")]
    [ServiceRequestId("TEAM_UPDATE")]
    [TournamentAccess(TournamentPermission.ManageTeam)]
    public async Task<ActionResult<ApiResponse<Team>>> UpdateTeam(int id, int teamId, [FromBody] UpdateTeamRequest request) =>
        Respond(await _teams.UpdateTeamAsync(id, teamId, request));

    // DELETE api/tournaments/5/teams/9
    [HttpDelete("{teamId}")]
    [ServiceRequestId("TEAM_DELETE")]
    [TournamentAccess(TournamentPermission.Edit)]
    public async Task<ActionResult<ApiResponse<object>>> DeleteTeam(int id, int teamId) =>
        Respond(await _teams.DeleteTeamAsync(id, teamId), _ => Ok());

    // POST api/tournaments/5/teams/9/join-code/reset — the old code stops working
    [HttpPost("{teamId}/join-code/reset")]
    [ServiceRequestId("TEAM_JOIN_CODE_RESET")]
    [TournamentAccess(TournamentPermission.ManageTeam)]
    public async Task<ActionResult<ApiResponse<Team>>> ResetJoinCode(int id, int teamId) =>
        Respond(await _teams.ResetJoinCodeAsync(id, teamId));

    // POST api/tournaments/5/teams/9/players/lookup — who an email or number belongs to, before adding them
    [HttpPost("{teamId}/players/lookup")]
    [ServiceRequestId("PLAYER_CONTACT_LOOKUP")]
    [TournamentAccess(TournamentPermission.ManageTeam)]
    public async Task<ActionResult<ApiResponse<PlayerLookupDto>>> LookupPlayer(int id, int teamId, [FromBody] PlayerLookupRequest request) =>
        Respond(await _teams.LookupPlayerAsync(id, teamId, request));

    // POST api/tournaments/5/teams/9/players
    [HttpPost("{teamId}/players")]
    [ServiceRequestId("PLAYER_CREATE")]
    [TournamentAccess(TournamentPermission.ManageTeam)]
    public async Task<ActionResult<ApiResponse<Player>>> AddPlayer(int id, int teamId, [FromBody] CreatePlayerRequest request) =>
        Respond(await _teams.AddPlayerAsync(id, teamId, request));

    // PUT api/tournaments/5/teams/9/players/3
    [HttpPut("{teamId}/players/{playerId}")]
    [ServiceRequestId("PLAYER_UPDATE")]
    [TournamentAccess(TournamentPermission.ManageTeam)]
    public async Task<ActionResult<ApiResponse<Player>>> UpdatePlayer(int id, int teamId, int playerId, [FromBody] UpdatePlayerRequest request) =>
        Respond(await _teams.UpdatePlayerAsync(id, teamId, playerId, request));

    // POST api/tournaments/5/teams/9/players/3/unlink — an owner, or the player themselves ("Leave team")
    [HttpPost("{teamId}/players/{playerId}/unlink")]
    [ServiceRequestId("PLAYER_UNLINK")]
    [TournamentAccess(TournamentPermission.View)]
    public async Task<ActionResult<ApiResponse<Player>>> UnlinkPlayer(int id, int teamId, int playerId) =>
        Respond(await _teams.UnlinkPlayerAsync(id, teamId, playerId));

    // DELETE api/tournaments/5/teams/9/players/3
    [HttpDelete("{teamId}/players/{playerId}")]
    [ServiceRequestId("PLAYER_DELETE")]
    [TournamentAccess(TournamentPermission.ManageTeam)]
    public async Task<ActionResult<ApiResponse<object>>> DeletePlayer(int id, int teamId, int playerId) =>
        Respond(await _teams.DeletePlayerAsync(id, teamId, playerId), _ => Ok());

    // POST api/tournaments/5/teams/9/players/3/restore — back into the squad after being left out
    [HttpPost("{teamId}/players/{playerId}/restore")]
    [ServiceRequestId("PLAYER_RESTORE")]
    [TournamentAccess(TournamentPermission.ManageTeam)]
    public async Task<ActionResult<ApiResponse<Team>>> RestorePlayer(int id, int teamId, int playerId) =>
        Respond(await _teams.RestorePlayerAsync(id, teamId, playerId));

    // POST api/tournaments/5/teams/9/admins — an owner names someone to run this team
    [HttpPost("{teamId}/admins")]
    [ServiceRequestId("TEAM_ADMIN_ADD")]
    [TournamentAccess(TournamentPermission.Edit)]
    public async Task<ActionResult<ApiResponse<Team>>> AddAdmin(int id, int teamId, [FromBody] AddTeamAdminRequest request) =>
        Respond(await _teams.AddAdminAsync(id, teamId, request));

    // DELETE api/tournaments/5/teams/9/admins/12 — an owner removes them, or the admin steps down (checked in the service)
    [HttpDelete("{teamId}/admins/{userId}")]
    [ServiceRequestId("TEAM_ADMIN_REMOVE")]
    [TournamentAccess(TournamentPermission.View)]
    public async Task<ActionResult<ApiResponse<Team>>> RemoveAdmin(int id, int teamId, int userId) =>
        Respond(await _teams.RemoveAdminAsync(id, teamId, userId));
}
