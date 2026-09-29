using Microsoft.AspNetCore.Mvc;
using TournamentScheduler.Api.Http;
using TournamentScheduler.Api.Models;
using TournamentScheduler.Api.Services;

namespace TournamentScheduler.Api.Controllers;

[ApiController]
[Route("api/tournaments/{tournamentId}/teams")]
public class TeamsController : ApiController
{
    private readonly ITeamService _teams;

    public TeamsController(ITeamService teams)
    {
        _teams = teams;
    }

    // GET api/tournaments/5/teams
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<Team>>>> GetTeams(int tournamentId) =>
        Respond(await _teams.GetTeamsAsync(tournamentId));

    // GET api/tournaments/5/teams/9
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<Team>>> GetTeam(int tournamentId, int id) =>
        Respond(await _teams.GetTeamAsync(tournamentId, id));

    // POST api/tournaments/5/teams
    [HttpPost]
    public async Task<ActionResult<ApiResponse<Team>>> CreateTeam(int tournamentId, [FromBody] CreateTeamRequest request) =>
        Respond(await _teams.CreateTeamAsync(tournamentId, request),
            team => CreatedAtAction(nameof(GetTeam), new { tournamentId, id = team.Id }, team));

    // PUT api/tournaments/5/teams/9
    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<Team>>> UpdateTeam(int tournamentId, int id, [FromBody] UpdateTeamRequest request) =>
        Respond(await _teams.UpdateTeamAsync(tournamentId, id, request));

    // DELETE api/tournaments/5/teams/9
    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResponse<object>>> DeleteTeam(int tournamentId, int id) =>
        Respond(await _teams.DeleteTeamAsync(tournamentId, id), _ => Ok());

    // POST api/tournaments/5/teams/9/players
    [HttpPost("{teamId}/players")]
    public async Task<ActionResult<ApiResponse<Player>>> AddPlayer(int tournamentId, int teamId, [FromBody] CreatePlayerRequest request) =>
        Respond(await _teams.AddPlayerAsync(tournamentId, teamId, request));

    // PUT api/tournaments/5/teams/9/players/3
    [HttpPut("{teamId}/players/{playerId}")]
    public async Task<ActionResult<ApiResponse<Player>>> UpdatePlayer(int tournamentId, int teamId, int playerId, [FromBody] UpdatePlayerRequest request) =>
        Respond(await _teams.UpdatePlayerAsync(tournamentId, teamId, playerId, request));

    // DELETE api/tournaments/5/teams/9/players/3
    [HttpDelete("{teamId}/players/{playerId}")]
    public async Task<ActionResult<ApiResponse<object>>> DeletePlayer(int tournamentId, int teamId, int playerId) =>
        Respond(await _teams.DeletePlayerAsync(tournamentId, teamId, playerId), _ => Ok());
}
