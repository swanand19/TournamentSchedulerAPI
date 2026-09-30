using Microsoft.AspNetCore.Mvc;
using TournamentScheduler.Api.Gateway;
using TournamentScheduler.Api.Http;
using TournamentScheduler.Api.Models;
using TournamentScheduler.Api.Services;

namespace TournamentScheduler.Api.Controllers;

// Route parameter names are part of the gateway contract (the apps fill them by name):
// the tournament is {id}, then {teamId} and {playerId}.
[ApiController]
[Route("api/tournaments/{id}/teams")]
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
    public async Task<ActionResult<ApiResponse<List<Team>>>> GetTeams(int id) =>
        Respond(await _teams.GetTeamsAsync(id));

    // GET api/tournaments/5/teams/9
    [HttpGet("{teamId}")]
    [ServiceRequestId("TEAM_GET")]
    public async Task<ActionResult<ApiResponse<Team>>> GetTeam(int id, int teamId) =>
        Respond(await _teams.GetTeamAsync(id, teamId));

    // POST api/tournaments/5/teams
    [HttpPost]
    [ServiceRequestId("TEAM_CREATE")]
    public async Task<ActionResult<ApiResponse<Team>>> CreateTeam(int id, [FromBody] CreateTeamRequest request) =>
        Respond(await _teams.CreateTeamAsync(id, request),
            team => CreatedAtAction(nameof(GetTeam), new { id, teamId = team.Id }, team));

    // PUT api/tournaments/5/teams/9
    [HttpPut("{teamId}")]
    [ServiceRequestId("TEAM_UPDATE")]
    public async Task<ActionResult<ApiResponse<Team>>> UpdateTeam(int id, int teamId, [FromBody] UpdateTeamRequest request) =>
        Respond(await _teams.UpdateTeamAsync(id, teamId, request));

    // DELETE api/tournaments/5/teams/9
    [HttpDelete("{teamId}")]
    [ServiceRequestId("TEAM_DELETE")]
    public async Task<ActionResult<ApiResponse<object>>> DeleteTeam(int id, int teamId) =>
        Respond(await _teams.DeleteTeamAsync(id, teamId), _ => Ok());

    // POST api/tournaments/5/teams/9/players
    [HttpPost("{teamId}/players")]
    [ServiceRequestId("PLAYER_CREATE")]
    public async Task<ActionResult<ApiResponse<Player>>> AddPlayer(int id, int teamId, [FromBody] CreatePlayerRequest request) =>
        Respond(await _teams.AddPlayerAsync(id, teamId, request));

    // PUT api/tournaments/5/teams/9/players/3
    [HttpPut("{teamId}/players/{playerId}")]
    [ServiceRequestId("PLAYER_UPDATE")]
    public async Task<ActionResult<ApiResponse<Player>>> UpdatePlayer(int id, int teamId, int playerId, [FromBody] UpdatePlayerRequest request) =>
        Respond(await _teams.UpdatePlayerAsync(id, teamId, playerId, request));

    // DELETE api/tournaments/5/teams/9/players/3
    [HttpDelete("{teamId}/players/{playerId}")]
    [ServiceRequestId("PLAYER_DELETE")]
    public async Task<ActionResult<ApiResponse<object>>> DeletePlayer(int id, int teamId, int playerId) =>
        Respond(await _teams.DeletePlayerAsync(id, teamId, playerId), _ => Ok());
}
