using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace TournamentScheduler.Api.Http;

/// <summary>
/// Wraps every controller response in <see cref="ApiResponse{T}"/>, so no endpoint can forget to —
/// or answer in a different shape. It runs on every result, including ones produced before the
/// action (model validation) and ASP.NET's own error bodies, and keeps the real HTTP status.
///
/// How each kind of result becomes an envelope:
///  - a value (Ok, Created, …)          -> data = the value, message "Success." / "Created."
///  - a failure with text (BadRequest("…")) -> message = the text, data = null
///  - ASP.NET problem details (NotFound())  -> message = its detail or a readable default
///  - validation problems (bad JSON, types) -> message = the first problem, data = every problem by field
///  - a bare status (Ok(), NoContent())     -> data = null; a 204 becomes a 200 so there is a body to read
/// </summary>
public sealed class ApiResponseFilter : IAlwaysRunResultFilter, IOrderedFilter
{
    /// <summary>After ASP.NET's own client-error filter (-2000), which turns NotFound() into problem details.</summary>
    public int Order => 1000;

    public void OnResultExecuting(ResultExecutingContext context)
    {
        switch (context.Result)
        {
            case ObjectResult { Value: IApiResponse }:
                return;

            case ObjectResult result:
            {
                var status = result.StatusCode ?? StatusCodes.Status200OK;
                result.Value = Wrap(status, result.Value);
                result.DeclaredType = result.Value.GetType();
                result.StatusCode = status;
                result.ContentTypes.Clear(); // a string body was bound for text/plain; the envelope is JSON
                return;
            }

            case StatusCodeResult result:
            {
                var status = result.StatusCode == StatusCodes.Status204NoContent ? StatusCodes.Status200OK : result.StatusCode;
                context.Result = new ObjectResult(ApiResponses.Create(status, null, null)) { StatusCode = status };
                return;
            }
        }
    }

    public void OnResultExecuted(ResultExecutedContext context) { }

    private static IApiResponse Wrap(int status, object? value)
    {
        if (status < 400)
            return ApiResponses.Create(status, value, null);

        switch (value)
        {
            case string message:
                return ApiResponses.Create(status, null, message);
            case ValidationProblemDetails validation:
            {
                var errors = Readable(validation.Errors);
                return ApiResponses.Create(status, errors, errors.Values.SelectMany(v => v).FirstOrDefault());
            }
            case ProblemDetails problem:
                return ApiResponses.Create(status, null, problem.Detail);
            default:
                // A failure that carries a body of its own (the health check's detail) keeps it as data.
                return ApiResponses.Create(status, value, null);
        }
    }

    /// <summary>
    /// The binder's own wording names .NET types and byte positions ("could not be converted to
    /// TournamentScheduler.Api.Models.Sport … BytePositionInLine: 30") — unhelpful to a person and
    /// more than a caller needs to know about the server. Each field gets a plain sentence instead,
    /// and the knock-on "request field is required" is dropped when a real cause is listed.
    /// </summary>
    private static Dictionary<string, string[]> Readable(IDictionary<string, string[]> errors)
    {
        var causes = errors.Where(e => !string.Equals(e.Key, "request", StringComparison.OrdinalIgnoreCase)).ToList();
        if (causes.Count == 0) causes = errors.ToList();

        return causes.ToDictionary(
            e => e.Key.StartsWith("$.") ? e.Key[2..] : e.Key,
            e => e.Key switch
            {
                "$" => ["The request body isn't valid JSON."],
                _ when e.Key.StartsWith("$.") => [$"'{e.Key[2..]}' has a value of the wrong type or format."],
                "request" => ["The request body is missing."],
                _ => e.Value
            });
    }
}
