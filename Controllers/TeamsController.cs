using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TournamentScheduler.Api.Data;
using TournamentScheduler.Api.Models;

namespace TournamentScheduler.Api.Controllers;

[ApiController]
[Route("api/tournaments/{tournamentId}/teams")]
public class TeamsController : ControllerBase
{
    private readonly TournamentDbContext _db;

    public TeamsController(TournamentDbContext db)
    {
        _db = db;
    }

    private async Task<bool> TournamentExists(int tournamentId) =>
        await _db.Tournaments.AnyAsync(t => t.Id == tournamentId);

    // GET api/tournaments/5/teams
    [HttpGet]
    public async Task<ActionResult<List<Team>>> GetTeams(int tournamentId)
    {
        if (!await TournamentExists(tournamentId)) return NotFound("Tournament not found.");

        var teams = await _db.Teams
            .Where(t => t.TournamentId == tournamentId)
            .Include(t => t.Players)
            .OrderBy(t => t.Name)
            .ToListAsync();
        return Ok(teams);
    }

    // GET api/tournaments/5/teams/9
    [HttpGet("{id}")]
    public async Task<ActionResult<Team>> GetTeam(int tournamentId, int id)
    {
        var team = await _db.Teams
            .Include(t => t.Players)
            .FirstOrDefaultAsync(t => t.Id == id && t.TournamentId == tournamentId);
        if (team == null) return NotFound();
        return Ok(team);
    }

    // POST api/tournaments/5/teams
    [HttpPost]
    public async Task<ActionResult<Team>> CreateTeam(int tournamentId, [FromBody] CreateTeamRequest request)
    {
        if (!await TournamentExists(tournamentId)) return NotFound("Tournament not found.");

        var name = request.Name?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest("Team name is required.");

        var exists = await _db.Teams.AnyAsync(t => t.Name == name && t.TournamentId == tournamentId);
        if (exists)
            return Conflict($"A team named '{name}' already exists in this tournament.");

        var team = new Team { Name = name, TournamentId = tournamentId };
        _db.Teams.Add(team);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            return Conflict($"A team named '{name}' already exists in this tournament.");
        }
        return CreatedAtAction(nameof(GetTeam), new { tournamentId, id = team.Id }, team);
    }

    // PUT api/tournaments/5/teams/9
    [HttpPut("{id}")]
    public async Task<ActionResult<Team>> UpdateTeam(int tournamentId, int id, [FromBody] UpdateTeamRequest request)
    {
        var team = await _db.Teams.FirstOrDefaultAsync(t => t.Id == id && t.TournamentId == tournamentId);
        if (team == null) return NotFound();

        var name = request.Name?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest("Team name is required.");

        var duplicate = await _db.Teams.AnyAsync(t => t.Name == name && t.TournamentId == tournamentId && t.Id != id);
        if (duplicate)
            return Conflict($"A team named '{name}' already exists in this tournament.");

        team.Name = name;
        await _db.SaveChangesAsync();
        return Ok(team);
    }

    // DELETE api/tournaments/5/teams/9
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteTeam(int tournamentId, int id)
    {
        var team = await _db.Teams.FirstOrDefaultAsync(t => t.Id == id && t.TournamentId == tournamentId);
        if (team == null) return NotFound();

        _db.Teams.Remove(team); // cascades to players
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // POST api/tournaments/5/teams/9/players
    [HttpPost("{teamId}/players")]
    public async Task<ActionResult<Player>> AddPlayer(int tournamentId, int teamId, [FromBody] CreatePlayerRequest request)
    {
        var team = await _db.Teams.FirstOrDefaultAsync(t => t.Id == teamId && t.TournamentId == tournamentId);
        if (team == null) return NotFound("Team not found.");

        var name = request.Name?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest("Player name is required.");

        if (request.JerseyNumber.HasValue)
        {
            var jerseyTaken = await _db.Players.AnyAsync(p =>
                p.TeamId == teamId && p.JerseyNumber == request.JerseyNumber);
            if (jerseyTaken)
                return Conflict($"Jersey number {request.JerseyNumber} is already taken on this team.");
        }

        var player = new Player
        {
            Name = name,
            Position = request.Position,
            JerseyNumber = request.JerseyNumber,
            TeamId = teamId
        };
        _db.Players.Add(player);
        await _db.SaveChangesAsync();
        return Ok(player);
    }

    // PUT api/tournaments/5/teams/9/players/3
    [HttpPut("{teamId}/players/{playerId}")]
    public async Task<ActionResult<Player>> UpdatePlayer(int tournamentId, int teamId, int playerId, [FromBody] UpdatePlayerRequest request)
    {
        var team = await _db.Teams.FirstOrDefaultAsync(t => t.Id == teamId && t.TournamentId == tournamentId);
        if (team == null) return NotFound("Team not found.");

        var player = await _db.Players.FirstOrDefaultAsync(p => p.Id == playerId && p.TeamId == teamId);
        if (player == null) return NotFound();

        var name = request.Name?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest("Player name is required.");

        if (request.JerseyNumber.HasValue)
        {
            var jerseyTaken = await _db.Players.AnyAsync(p =>
                p.TeamId == teamId && p.JerseyNumber == request.JerseyNumber && p.Id != playerId);
            if (jerseyTaken)
                return Conflict($"Jersey number {request.JerseyNumber} is already taken on this team.");
        }

        player.Name = name;
        player.Position = request.Position;
        player.JerseyNumber = request.JerseyNumber;
        await _db.SaveChangesAsync();
        return Ok(player);
    }

    // DELETE api/tournaments/5/teams/9/players/3
    [HttpDelete("{teamId}/players/{playerId}")]
    public async Task<IActionResult> DeletePlayer(int tournamentId, int teamId, int playerId)
    {
        var team = await _db.Teams.FirstOrDefaultAsync(t => t.Id == teamId && t.TournamentId == tournamentId);
        if (team == null) return NotFound("Team not found.");

        var player = await _db.Players.FirstOrDefaultAsync(p => p.Id == playerId && p.TeamId == teamId);
        if (player == null) return NotFound();

        _db.Players.Remove(player);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        return ex.InnerException is Microsoft.Data.SqlClient.SqlException sqlEx
            && (sqlEx.Number == 2601 || sqlEx.Number == 2627);
    }
}