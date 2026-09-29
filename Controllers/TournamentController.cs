using Microsoft.AspNetCore.Mvc;
using TournamentScheduler.Api.Http;
using TournamentScheduler.Api.Models;
using TournamentScheduler.Api.Services;

namespace TournamentScheduler.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TournamentController : ApiController
{
    private readonly IScheduleService _scheduleService;

    public TournamentController(IScheduleService scheduleService)
    {
        _scheduleService = scheduleService;
    }

    [HttpPost("groups/randomize")]
    public ActionResult<ApiResponse<object>> RandomizeGroups([FromBody] RandomizeGroupsRequest request) =>
        Respond(_scheduleService.DrawGroups(request));

    [HttpPost("groups/manual")]
    public ActionResult<ApiResponse<object>> SetManualGroups([FromBody] ManualGroupsRequest request) =>
        Respond(_scheduleService.CheckManualGroups(request));

    [HttpPost("schedule")]
    public ActionResult<ApiResponse<TournamentSchedule>> GenerateSchedule([FromBody] GenerateScheduleRequest request) =>
        Respond(_scheduleService.GenerateSchedule(request));

    [HttpPost("schedule/approve")]
    public async Task<ActionResult<ApiResponse<object>>> ApproveSchedule([FromBody] ApproveScheduleWithTournamentRequest request) =>
        Respond(await _scheduleService.ApproveAsync(request));

    [HttpGet("schedule/{id}")]
    public async Task<ActionResult<ApiResponse<SavedSchedule>>> GetSavedSchedule(int id) =>
        Respond(await _scheduleService.GetSavedScheduleAsync(id));
}
