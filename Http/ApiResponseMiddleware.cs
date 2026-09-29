using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace TournamentScheduler.Api.Http;

/// <summary>
/// The envelope for answers that never reach a controller, so even these read like every other
/// response: an unhandled exception (500) and a request no route matches (404, 405, …).
/// </summary>
public static class ApiResponseMiddleware
{
    public static WebApplication UseApiResponses(this WebApplication app)
    {
        // A crash: log it, answer 500 in the envelope. The exception's own text is only shown while
        // developing — in production it could reveal internals, so users get a plain message.
        app.UseExceptionHandler(handler =>
        {
            // Handling clears the response, CORS headers included; put them back so the website can read the message.
            handler.UseCors("AllowFrontend");
            handler.Run(async context =>
            {
                var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
                var message = app.Environment.IsDevelopment() && error != null ? error.Message : null;
                await WriteAsync(context, StatusCodes.Status500InternalServerError, message);
            });
        });

        // An error status with no body yet (no route matched, wrong method): give it one.
        app.UseStatusCodePages(async statusContext =>
            await WriteAsync(statusContext.HttpContext, statusContext.HttpContext.Response.StatusCode, null));

        return app;
    }

    private static Task WriteAsync(HttpContext context, int statusCode, string? message)
    {
        var json = context.RequestServices.GetRequiredService<IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>().Value.JsonSerializerOptions;
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        return JsonSerializer.SerializeAsync(context.Response.Body, ApiResponses.Create(statusCode, null, message), json);
    }
}
