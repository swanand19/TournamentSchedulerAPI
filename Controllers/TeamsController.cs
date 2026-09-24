using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TournamentScheduler.Api.Data;
using TournamentScheduler.Api.Models;
using TournamentScheduler.Api.Models.Cricket;

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
            .Include(t => t.Players).ThenInclude(p => p.Cricket)
            .OrderBy(t => t.Name)
            .ToListAsync();
        return Ok(teams);
    }

    // GET api/tournaments/5/teams/9
    [HttpGet("{id}")]
    public async Task<ActionResult<Team>> GetTeam(int tournamentId, int id)
    {
        var team = await _db.Teams
            .Include(t => t.Players).ThenInclude(p => p.Cricket)
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

        if (request.SetCaptain)
        {
            if (request.DefaultCaptainPlayerId.HasValue)
            {
                var onThisTeam = await _db.Players.AnyAsync(p =>
                    p.Id == request.DefaultCaptainPlayerId && p.TeamId == id);
                if (!onThisTeam)
                    return BadRequest("The captain must be a player on this team.");
            }
            team.DefaultCaptainPlayerId = request.DefaultCaptainPlayerId;
        }

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

        if (ValidateCricket(request.Cricket) is { } cricketError)
            return BadRequest(cricketError);

        var player = new Player
        {
            Name = name,
            Position = request.Position,
            JerseyNumber = request.JerseyNumber,
            TeamId = teamId
        };
        if (request.Cricket != null)
            player.Cricket = NewProfile(request.Cricket);

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

        var player = await _db.Players
            .Include(p => p.Cricket)
            .FirstOrDefaultAsync(p => p.Id == playerId && p.TeamId == teamId);
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

        if (ValidateCricket(request.Cricket) is { } cricketError)
            return BadRequest(cricketError);

        player.Name = name;
        player.Position = request.Position;
        player.JerseyNumber = request.JerseyNumber;

        // Null means "not sent", not "clear it" — a football client never sends this block, and a
        // rename must not silently discard a cricketer's styles.
        if (request.Cricket != null)
        {
            if (player.Cricket == null)
                player.Cricket = NewProfile(request.Cricket);
            else
                Apply(request.Cricket, player.Cricket);
        }

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

    /// <summary>
    /// An arm without a type (or the reverse) is half a bowling style, and would render as
    /// nothing at all. Reject it here rather than storing a profile that cannot be described.
    /// </summary>
    private static string? ValidateCricket(CricketProfileInput? input)
    {
        if (input == null) return null;

        if (input.BowlingArm.HasValue != input.BowlingType.HasValue)
            return "A bowling style needs both an arm and a type, or neither.";

        if (input.BattingOrderPreference is < 1 or > 11)
            return "Batting order preference must be between 1 and 11.";

        return null;
    }

    private static PlayerCricketProfile NewProfile(CricketProfileInput input)
    {
        var profile = new PlayerCricketProfile();
        Apply(input, profile);
        return profile;
    }

    private static void Apply(CricketProfileInput input, PlayerCricketProfile profile)
    {
        profile.PrimaryRole = input.PrimaryRole;
        profile.BattingStyle = input.BattingStyle;
        profile.BattingOrderPreference = input.BattingOrderPreference;

        // A pure batter keeps no bowling style: the form hides those fields, so anything still
        // sitting in the payload is stale rather than intended.
        var bowls = CricketRoles.Bowls(input.PrimaryRole);
        profile.BowlingArm = bowls ? input.BowlingArm : null;
        profile.BowlingType = bowls ? input.BowlingType : null;
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        return ex.InnerException is Microsoft.Data.SqlClient.SqlException sqlEx
            && (sqlEx.Number == 2601 || sqlEx.Number == 2627);
    }
}