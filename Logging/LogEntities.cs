namespace TournamentScheduler.Api.Logging;

// The four tables of the logs database (TournamentSchedulerLogs). Names follow the team's other
// projects. Every row carries the request's RequestUUID, so one id shows a request's whole story:
// what arrived over the wire, what the action saw and answered, and anything that went wrong.
// All times are UTC. Bodies are masked (credential fields) and capped before they get here.

/// <summary>Every call to the secure gateway, exactly as it crossed the wire — encrypted.</summary>
public class MBMiddlewareLog
{
    public long MBMiddlewareLogId { get; set; }

    /// <summary>From the request header when it parses; generated when the envelope was unreadable.</summary>
    public Guid RequestUUID { get; set; }

    public string? ServiceRequestId { get; set; }
    public string? JourneyId { get; set; }
    public string? SessionId { get; set; }
    public string? DeviceId { get; set; }
    public string? Channel { get; set; }
    public string? AppVersion { get; set; }
    public string? KeyId { get; set; }
    public string? ClientIp { get; set; }

    /// <summary>The envelope as received: clear header plus the JWE.</summary>
    public string? EncryptedRequestJson { get; set; }

    /// <summary>The envelope as sent. Null for reads unless read bodies are switched on.</summary>
    public string? EncryptedResponseJson { get; set; }

    public int HttpStatusCode { get; set; }

    /// <summary>Why the gateway refused the request itself (bad envelope, tampered, replay, clock…); null when it reached the action.</summary>
    public string? RejectedReason { get; set; }

    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int DurationMs { get; set; }
}

/// <summary>Every call that reached the API's pipeline, in plain text — through the gateway or not.</summary>
public class MBActivityLog
{
    public long MBActivityLogId { get; set; }
    public Guid RequestUUID { get; set; }
    public string? SessionId { get; set; }
    public string? JourneyId { get; set; }
    public string? DeviceId { get; set; }
    public string? Channel { get; set; }
    public string? ServiceRequestId { get; set; }
    public string? ControllerName { get; set; }
    public string? ActionName { get; set; }
    public string HttpMethod { get; set; } = string.Empty;

    /// <summary>The action's own URL, route values and query included — e.g. /api/cricket-matches/42/balls.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>The body the action received.</summary>
    public string? RequestJson { get; set; }

    /// <summary>The { status, data } envelope it answered with. Null for reads unless read bodies are switched on.</summary>
    public string? ResponseJson { get; set; }

    public int HttpStatusCode { get; set; }
    public bool? IsSuccess { get; set; }
    public string? StatusMessage { get; set; }
    public bool ViaGateway { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int DurationMs { get; set; }
}

/// <summary>Anything logged at Error or above, anywhere in the API — with the request it happened in.</summary>
public class ErrorLog
{
    public long ErrorLogId { get; set; }

    /// <summary>When it happened (the team's "created date" convention).</summary>
    public DateTime Crd { get; set; }

    /// <summary>Null for errors outside any request (start-up, background work).</summary>
    public Guid? RequestUUID { get; set; }

    public string? ServiceRequestId { get; set; }
    public string? Url { get; set; }

    /// <summary>The plain request body the action received, when the error happened inside a request.</summary>
    public string? RequestJson { get; set; }

    public string? ExceptionType { get; set; }

    /// <summary>The exception's message, or the log message when there was no exception.</summary>
    public string? Message { get; set; }

    /// <summary>What the component that logged it said, e.g. "Gateway call to X failed.".</summary>
    public string? LogMessage { get; set; }

    public string? StackTrace { get; set; }

    /// <summary>Every inner exception, outermost first, with their stack traces.</summary>
    public string? InnerException { get; set; }

    /// <summary>The component that logged it (the logger category).</summary>
    public string? Source { get; set; }

    public string Severity { get; set; } = string.Empty;
}

/// <summary>
/// Calls the API makes to third-party services. Nothing writes to it yet — it is here so the first
/// integration logs the same way from day one. See Logging/README.md.
/// </summary>
public class RemoteLog
{
    public long RemoteLogId { get; set; }

    /// <summary>The API request during which the third party was called.</summary>
    public Guid? RequestUUID { get; set; }

    public string? ProviderName { get; set; }
    public string? HttpMethod { get; set; }
    public string? Url { get; set; }

    /// <summary>Masked: Authorization and API-key headers never stored in the clear.</summary>
    public string? RequestHeaders { get; set; }

    public string? RequestJson { get; set; }
    public string? EncryptedRequestJson { get; set; }
    public string? ResponseHeaders { get; set; }
    public string? ResponseJson { get; set; }
    public string? EncryptedResponseJson { get; set; }
    public int? HttpStatusCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int DurationMs { get; set; }
}
