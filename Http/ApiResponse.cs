namespace TournamentScheduler.Api.Http;

/// <summary>
/// The one shape every response has, success or failure:
/// <code>
/// { "status": { "isSuccess": true, "message": "Success.", "statusCode": 200 }, "data": { ... } }
/// { "status": { "isSuccess": false, "message": "Team name is required.", "statusCode": 400 }, "data": null }
/// </code>
/// The HTTP status code is always the real one too — a 404 is a 404, never a 200 carrying
/// isSuccess: false — so caching, monitoring and retries keep working; statusCode repeats it for
/// convenience. Clients read <c>data</c> on success and show <c>status.message</c> on failure.
///
/// Controllers don't build this themselves: <see cref="ApiResponseFilter"/> wraps whatever an
/// action returns, and the middleware in <see cref="ApiResponseMiddleware"/> covers the answers that
/// never reach a controller (an unknown URL, a crash).
/// </summary>
public sealed class ApiResponse<T> : IApiResponse
{
    public ApiStatus Status { get; init; } = new();

    /// <summary>What the endpoint answered with. Null on failure, except where the failure carries detail (validation errors).</summary>
    public T? Data { get; init; }
}

public sealed class ApiStatus
{
    public bool IsSuccess { get; init; }

    /// <summary>For people: "Success.", or on failure the reason to show, e.g. "Tournament not found.".</summary>
    public string? Message { get; init; }

    /// <summary>The HTTP status code of the response.</summary>
    public int StatusCode { get; init; }
}

/// <summary>Marks a value as already wrapped, so it is never wrapped twice.</summary>
public interface IApiResponse;

public static class ApiResponses
{
    public static ApiResponse<object> Create(int statusCode, object? data, string? message) => new()
    {
        Status = new ApiStatus
        {
            IsSuccess = statusCode < 400,
            Message = message ?? DefaultMessage(statusCode),
            StatusCode = statusCode
        },
        Data = data
    };

    /// <summary>What a response says when its action gave no message of its own.</summary>
    public static string DefaultMessage(int statusCode) => statusCode switch
    {
        201 => "Created.",
        < 300 => "Success.",
        400 => "The request is not valid.",
        401 => "Sign in to do that.",
        403 => "You don't have permission to do that.",
        404 => "The requested item was not found.",
        405 => "That action isn't allowed here.",
        409 => "The request conflicts with the current state. Reload and try again.",
        415 => "The request body must be JSON.",
        503 => "The service is unavailable right now. Try again shortly.",
        >= 500 => "Something went wrong on the server. Try again shortly.",
        _ => $"The request could not be completed ({statusCode})."
    };
}
