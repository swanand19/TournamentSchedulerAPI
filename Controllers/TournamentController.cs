using Microsoft.AspNetCore.Mvc;
using TournamentScheduler.Api.Data;
using TournamentScheduler.Api.Models;
using TournamentScheduler.Api.Services;
using Microsoft.EntityFrameworkCore;
using TournamentScheduler.Api.Data;

namespace TournamentScheduler.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TournamentController : ControllerBase
{
    private readonly IScheduleService _scheduleService;
    private readonly TournamentDbContext _db;

    public TournamentController(IScheduleService scheduleService, TournamentDbContext db)
    {
        _scheduleService = scheduleService;
        _db = db;
    }

    [HttpPost("groups/randomize")]
    public ActionResult<object> RandomizeGroups([FromBody] RandomizeGroupsRequest request)
    {
        if (request.GroupCount < 1)
            return BadRequest("Group count must be at least 1.");

        var minTeamsNeeded = Math.Max(2, request.GroupCount * 2);
        if (request.TeamNames == null || request.TeamNames.Count < minTeamsNeeded)
            return BadRequest($"Need at least {minTeamsNeeded} teams for {request.GroupCount} group(s) (min 2 per group).");

        var groups = _scheduleService.RandomizeGroups(request.TeamNames, request.GroupCount);
        return Ok(new { groups });
    }

    [HttpPost("groups/manual")]
    public ActionResult<object> SetManualGroups([FromBody] ManualGroupsRequest request)
    {
        if (request.Groups == null || request.Groups.Count < 1)
            return BadRequest("Provide at least 1 group.");

        if (request.Groups.Any(g => g.Teams.Count == 0))
            return BadRequest("Every group needs at least one team.");

        var allTeams = request.Groups.SelectMany(g => g.Teams).ToList();
        var duplicates = allTeams.GroupBy(t => t).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Any())
            return BadRequest($"Team(s) appear in more than one group: {string.Join(", ", duplicates)}");

        return Ok(new { groups = request.Groups });
    }

    [HttpPost("schedule")]
    public ActionResult<TournamentSchedule> GenerateSchedule([FromBody] GenerateScheduleRequest request)
    {
        if (request.Groups == null || request.Groups.Count < 1)
            return BadRequest("Provide at least 1 group.");

        if (request.Groups.Any(g => g.Teams.Count < 2))
            return BadRequest("Every group needs at least 2 teams.");

        if (request.MatchesPerTeam < 1)
            return BadRequest("Matches per team must be at least 1.");

        var result = _scheduleService.GenerateTournamentSchedule(request.Groups, request.MatchesPerTeam);
        return Ok(result);
    }

    [HttpPost("schedule/approve")]
    public async Task<ActionResult<object>> ApproveSchedule([FromBody] ApproveScheduleWithTournamentRequest request)
    {
        if (request.Schedule?.Groups == null || request.Schedule.Groups.Count == 0)
            return BadRequest("No schedule to approve.");

        if (request.TournamentId <= 0)
            return BadRequest("A tournament must be specified.");

        var tournament = await _db.Tournaments.FindAsync(request.TournamentId);
        if (tournament == null) return NotFound("Tournament not found.");
        if (tournament.IsStarted)
            return BadRequest("This tournament has already started — its schedule is locked and can no longer be changed.");

        var saved = await _scheduleService.ApproveScheduleAsync(request.TournamentId, request.Schedule);
        return Ok(new { savedScheduleId = saved.Id, savedAt = saved.CreatedAt });
    }

    [HttpGet("schedule/{id}")]
    public async Task<ActionResult<SavedSchedule>> GetSavedSchedule(int id, [FromServices] TournamentDbContext db)
    {
        var result = await db.SavedSchedules
            .Include(s => s.Groups)
            .ThenInclude(g => g.Fixtures)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (result == null) return NotFound();
        return Ok(result);
    }
}