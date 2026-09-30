namespace TournamentScheduler.Api.Logging;

/// <summary>
/// What every log row written during one request needs to know about it. Created first thing for
/// every request (<see cref="RequestCorrelationMiddleware"/>); the gateway fills in what its header
/// says, and the activity log adds the action and the plain request body — which is how an error
/// logged deep in a service still lands in ErrorLogs with the request that caused it.
/// </summary>
public sealed class RequestLogContext
{
    /// <summary>The request's id in every table. The app's requestUUID for gateway calls; generated for the rest.</summary>
    public Guid RequestUUID { get; set; }

    public bool ViaGateway { get; set; }
    public string? ServiceRequestId { get; set; }
    public string? JourneyId { get; set; }
    public string? SessionId { get; set; }
    public string? DeviceId { get; set; }
    public string? Channel { get; set; }

    /// <summary>The action's own URL (after the gateway's rewrite).</summary>
    public string? Url { get; set; }

    /// <summary>The plain, masked request body the action received.</summary>
    public string? RequestJson { get; set; }

    public static RequestLogContext? Of(HttpContext context) => context.Features.Get<RequestLogContext>();
}

/// <summary>
/// Gives every request an id before anything else runs, so even a crash in the pipeline's first
/// steps can be tied to a request, and echoes it to the caller as <c>X-Request-Id</c>. A caller's
/// own <c>X-Request-Id</c> is kept when it is a UUID; the gateway replaces it with the app's
/// requestUUID once it has read the envelope.
/// </summary>
public sealed class RequestCorrelationMiddleware(RequestDelegate next, ILogger<RequestCorrelationMiddleware> logger)
{
    public const string Header = "X-Request-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var log = new RequestLogContext
        {
            RequestUUID = Guid.TryParse(context.Request.Headers[Header], out var given) ? given : Guid.NewGuid()
        };
        context.Features.Set(log);
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[Header] = log.RequestUUID.ToString();
            return Task.CompletedTask;
        });

        using (logger.BeginScope(new Dictionary<string, object> { ["RequestUUID"] = log.RequestUUID }))
            await next(context);
    }
}
