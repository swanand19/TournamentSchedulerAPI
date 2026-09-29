using Microsoft.AspNetCore.Mvc;
using TournamentScheduler.Api.Services;

namespace TournamentScheduler.Api.Controllers;

/// <summary>
/// Base for every controller. A controller's only job is HTTP: bind the request, call one service
/// method, and turn its <see cref="ServiceResult{T}"/> into the response.
///
/// Actions return their data as-is; <see cref="Http.ApiResponseFilter"/> then wraps every response
/// in the { status, data } envelope. That is why actions declare <c>ActionResult&lt;ApiResponse&lt;T&gt;&gt;</c>:
/// it is what the client actually receives, and what Swagger documents.
/// </summary>
public abstract class ApiController : ControllerBase
{
    /// <summary>
    /// 200 with the value, or the matching error. <paramref name="onSuccess"/> replaces the 200 when
    /// an action answers differently on success (201 Created).
    /// </summary>
    protected ActionResult Respond<T>(ServiceResult<T> result, Func<T, ActionResult>? onSuccess = null) =>
        result.Outcome switch
        {
            ServiceOutcome.Ok => onSuccess != null ? onSuccess(result.Value!) : Ok(result.Value),
            ServiceOutcome.NotFound => result.Error == null ? NotFound() : NotFound(result.Error),
            ServiceOutcome.Conflict => Conflict(result.Error),
            _ => BadRequest(result.Error)
        };
}
