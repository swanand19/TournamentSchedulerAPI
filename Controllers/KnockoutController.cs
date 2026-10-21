using Microsoft.AspNetCore.Mvc;
using TournamentScheduler.Api.Auth.Permissions;
using TournamentScheduler.Api.Auth.TournamentAccess;
using TournamentScheduler.Api.Gateway;
using TournamentScheduler.Api.Http;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Models.Knockouts;
using TournamentScheduler.Api.Services.Knockouts;

namespace TournamentScheduler.Api.Controllers;

/// <summary>
/// A tournament's knockout stage (2026-10-21): everyone who can see the tournament sees it; owners
/// set it up from a preset or one match at a time, before, during or after the group stage. Every
/// change answers with the whole stage.
/// </summary>
[ApiController]
[Route("api/tournaments/{id}/knockout")]
[DefaultRoles(RoleIds.EndUser)]
public class KnockoutController : ApiController
{
    private readonly IKnockoutService _knockouts;

    public KnockoutController(IKnockoutService knockouts) => _knockouts = knockouts;

    // GET api/tournaments/5/knockout — the bracket, with each side's team or where it comes from
    [HttpGet]
    [ServiceRequestId("KNOCKOUT_GET")]
    [TournamentAccess(TournamentPermission.View)]
    public async Task<ActionResult<ApiResponse<KnockoutStageDto>>> Get(int id) =>
        Respond(await _knockouts.GetAsync(id));

    // GET api/tournaments/5/knockout/presets — the formats that fit this tournament's groups, previewed
    [HttpGet("presets")]
    [ServiceRequestId("KNOCKOUT_PRESETS")]
    [TournamentAccess(TournamentPermission.Edit)]
    public async Task<ActionResult<ApiResponse<List<KnockoutPresetDto>>>> Presets(int id) =>
        Respond(await _knockouts.PresetsAsync(id));

    // POST api/tournaments/5/knockout/apply — replaces the knockout stage with a preset's
    [HttpPost("apply")]
    [ServiceRequestId("KNOCKOUT_APPLY")]
    [TournamentAccess(TournamentPermission.Edit)]
    public async Task<ActionResult<ApiResponse<KnockoutStageDto>>> Apply(int id, [FromBody] KnockoutApplyRequest request) =>
        Respond(await _knockouts.ApplyAsync(id, request));

    // DELETE api/tournaments/5/knockout — removes the whole knockout stage (before any of it starts)
    [HttpDelete]
    [ServiceRequestId("KNOCKOUT_CLEAR")]
    [TournamentAccess(TournamentPermission.Edit)]
    public async Task<ActionResult<ApiResponse<KnockoutStageDto>>> Clear(int id) =>
        Respond(await _knockouts.ClearAsync(id));

    // POST api/tournaments/5/knockout/fixtures — one more knockout match
    [HttpPost("fixtures")]
    [ServiceRequestId("KNOCKOUT_FIXTURE_ADD")]
    [TournamentAccess(TournamentPermission.Edit)]
    public async Task<ActionResult<ApiResponse<KnockoutStageDto>>> Add(int id, [FromBody] KnockoutFixtureRequest request) =>
        Respond(await _knockouts.AddAsync(id, request));

    // PUT api/tournaments/5/knockout/fixtures/3 — its round, name and sides (before it starts)
    [HttpPut("fixtures/{fixtureId}")]
    [ServiceRequestId("KNOCKOUT_FIXTURE_UPDATE")]
    [TournamentAccess(TournamentPermission.Edit)]
    public async Task<ActionResult<ApiResponse<KnockoutStageDto>>> Update(int id, int fixtureId, [FromBody] KnockoutFixtureRequest request) =>
        Respond(await _knockouts.UpdateAsync(id, fixtureId, request));

    // DELETE api/tournaments/5/knockout/fixtures/3
    [HttpDelete("fixtures/{fixtureId}")]
    [ServiceRequestId("KNOCKOUT_FIXTURE_DELETE")]
    [TournamentAccess(TournamentPermission.Edit)]
    public async Task<ActionResult<ApiResponse<KnockoutStageDto>>> Delete(int id, int fixtureId) =>
        Respond(await _knockouts.DeleteAsync(id, fixtureId));

    // POST api/tournaments/5/knockout/fixtures/3/team { side, teamId } — the owner's choice for one side
    [HttpPost("fixtures/{fixtureId}/team")]
    [ServiceRequestId("KNOCKOUT_TEAM_SET")]
    [TournamentAccess(TournamentPermission.Edit)]
    public async Task<ActionResult<ApiResponse<KnockoutStageDto>>> SetTeam(int id, int fixtureId, [FromBody] KnockoutTeamRequest request) =>
        Respond(await _knockouts.SetTeamAsync(id, fixtureId, request));
}
