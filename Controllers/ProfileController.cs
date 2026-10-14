using Microsoft.AspNetCore.Mvc;
using TournamentScheduler.Api.Auth.Permissions;
using TournamentScheduler.Api.Auth.TournamentAccess;
using TournamentScheduler.Api.Gateway;
using TournamentScheduler.Api.Http;
using TournamentScheduler.Api.Models;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Models.Profiles;
using TournamentScheduler.Api.Services.Profiles;

namespace TournamentScheduler.Api.Controllers;

/// <summary>
/// Profiles and careers. With no ids it's you; a <c>playerId</c> (a squad place, which is what the
/// apps have when someone taps a name) or a <c>userId</c> opens someone else's.
/// </summary>
[ApiController]
[Route("api/profile")]
[DefaultRoles(RoleIds.EndUser)]
[NotTournamentScoped] // a career spans tournaments
public class ProfileController : ApiController
{
    private readonly IProfileService _profiles;

    public ProfileController(IProfileService profiles) => _profiles = profiles;

    // GET api/profile?userId=&playerId=
    [HttpGet]
    [ServiceRequestId("PROFILE_GET")]
    public async Task<ActionResult<ApiResponse<ProfileDto>>> Get([FromQuery] int? userId, [FromQuery] int? playerId) =>
        Respond(await _profiles.GetProfileAsync(userId, playerId));

    // GET api/profile/career?sport=Cricket&userId=&playerId=
    [HttpGet("career")]
    [ServiceRequestId("PROFILE_CAREER")]
    public async Task<ActionResult<ApiResponse<CareerDto>>> Career([FromQuery] Sport sport, [FromQuery] int? userId, [FromQuery] int? playerId) =>
        Respond(await _profiles.GetCareerAsync(sport, userId, playerId));
}
