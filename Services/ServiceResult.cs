namespace TournamentScheduler.Api.Services;

public enum ServiceOutcome
{
    Ok,
    NotFound,
    Invalid,
    Conflict,
    Forbidden,
    TooManyRequests,
    Unavailable
}

/// <summary>
/// What a service action produced: the value, or why there isn't one. Controllers turn it into the
/// HTTP answer (see <c>ApiController.Respond</c>), so the rules live in services and the status
/// codes and messages the apps rely on stay exactly as they were.
/// </summary>
public sealed class ServiceResult<T>
{
    public ServiceOutcome Outcome { get; }
    public T? Value { get; }

    /// <summary>The message shown to the user. A NotFound with no message answers with the standard empty 404.</summary>
    public string? Error { get; }

    private ServiceResult(ServiceOutcome outcome, T? value, string? error)
    {
        Outcome = outcome;
        Value = value;
        Error = error;
    }

    public bool IsOk => Outcome == ServiceOutcome.Ok;

    public static ServiceResult<T> Ok(T value) => new(ServiceOutcome.Ok, value, null);

    public static implicit operator ServiceResult<T>(ServiceError error) => new(error.Outcome, default, error.Message);
}

/// <summary>A failed outcome not yet tied to a result type, so one <c>return NotFound(...)</c> fits any action.</summary>
public readonly record struct ServiceError(ServiceOutcome Outcome, string? Message);

/// <summary>
/// The vocabulary every service answers in. The names deliberately match ASP.NET's (NotFound,
/// BadRequest, Conflict, Ok) so logic moved out of a controller reads exactly as it did there.
/// </summary>
public abstract class ServiceBase
{
    protected static ServiceError NotFound(string? message = null) => new(ServiceOutcome.NotFound, message);

    protected static ServiceError BadRequest(string message) => new(ServiceOutcome.Invalid, message);

    protected static ServiceError Conflict(string message) => new(ServiceOutcome.Conflict, message);

    /// <summary>403: the caller is known but may not do this.</summary>
    protected static ServiceError Forbidden(string message) => new(ServiceOutcome.Forbidden, message);

    /// <summary>429: a limit was reached; the message says how long to wait.</summary>
    protected static ServiceError TooManyRequests(string message) => new(ServiceOutcome.TooManyRequests, message);

    /// <summary>503: something the service depends on (email, say) isn't working right now.</summary>
    protected static ServiceError Unavailable(string message) => new(ServiceOutcome.Unavailable, message);

    /// <summary>For actions answering with an ad-hoc JSON shape (an anonymous object).</summary>
    protected static ServiceResult<object> Ok(object value) => ServiceResult<object>.Ok(value);

    protected static ServiceResult<T> Success<T>(T value) => ServiceResult<T>.Ok(value);
}
