using Microsoft.AspNetCore.Mvc;
using TournamentScheduler.Api.Gateway;
using TournamentScheduler.Api.Http;
using TournamentScheduler.Api.Services;

namespace TournamentScheduler.Api.Controllers;

/// <summary>
/// "Are you there?" for the app's Test connection button. It also says whether the database
/// answers, which is the first thing to check when the API runs under IIS as its app pool identity.
///
/// Callable in the clear as well as through the gateway: the deploy scripts and any uptime monitor
/// call it without the apps' key, and it carries no tournament data. The apps still go through the
/// gateway, so their Test connection proves the encryption works end to end.
/// </summary>
[ApiController]
[Route("api/health")]
[SkipEncryption]
public class HealthController : ApiController
{
    private readonly IHealthService _health;
    private readonly IWebHostEnvironment _env;

    public HealthController(IHealthService health, IWebHostEnvironment env)
    {
        _health = health;
        _env = env;
    }

    // GET api/health — 200 when the API and database both answer, 503 when the database doesn't
    [HttpGet]
    [ServiceRequestId("HEALTH_CHECK")]
    public async Task<ActionResult<ApiResponse<object>>> Get()
    {
        var database = await _health.DatabaseReachableAsync();

        var body = new
        {
            status = database ? "ok" : "degraded",
            database = database ? "ok" : "unreachable",
            environment = _env.EnvironmentName,
            serverTime = DateTime.UtcNow
        };

        return database
            ? Ok(body)
            : StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiResponses.Create(StatusCodes.Status503ServiceUnavailable, body, "The API is running but can't reach its database."));
    }
}
