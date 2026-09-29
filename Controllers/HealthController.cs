using Microsoft.AspNetCore.Mvc;
using TournamentScheduler.Api.Http;
using TournamentScheduler.Api.Services;

namespace TournamentScheduler.Api.Controllers;

/// <summary>
/// "Are you there?" for the app's Test connection button, and the one API that will stay outside
/// the encrypted gateway (serviceRequestId HEALTH_CHECK). It also says whether the database answers,
/// which is the first thing to check when the API runs under IIS as its app pool identity.
/// </summary>
[ApiController]
[Route("api/health")]
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
