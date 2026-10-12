using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Options;
using TournamentScheduler.Api.Gateway;
using TournamentScheduler.Api.Http;
using TournamentScheduler.Api.Logging;
using TournamentScheduler.Api.Services.Accounts;

namespace TournamentScheduler.Api.Auth;

/// <summary>
/// Who is calling, and whether they may call at all. Runs after routing (it needs to know the
/// action) and after gateway enforcement (a direct call that isn't allowed hears that first).
///
/// The token comes from the sealed gateway payload (<c>session</c>) for app requests. A direct call —
/// Swagger or a script while encryption isn't enforced — may send <c>Authorization: Bearer &lt;token&gt;</c>
/// instead; a gateway request never reads that header.
///
/// A valid session fills <see cref="CurrentUser"/> and the request's log context (so every log row
/// carries the user). Any action without <see cref="AllowAnonymousServiceAttribute"/> then needs one,
/// or answers 401 — which tells the apps to go back to their sign-in screen.
/// </summary>
public sealed class SessionAuthenticationMiddleware(RequestDelegate next, IOptions<JsonOptions> json)
{
    public async Task InvokeAsync(HttpContext context, ISessionService sessions, CurrentUser currentUser)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<ControllerActionDescriptor>() == null)
        {
            await next(context);
            return;
        }

        var token = TokenOf(context);
        SessionCheck? check = null;
        if (token != null)
        {
            check = await sessions.CheckAsync(token);
            if (check.IsSignedIn)
            {
                currentUser.SignIn(check.UserId, check.RoleId, check.Name, check.SessionId, check.IsPortal);
                if (RequestLogContext.Of(context) is { } log)
                {
                    log.UserId = check.UserId;
                    log.SessionId = check.SessionId.ToString();
                }
            }
        }

        if (currentUser.IsSignedIn || endpoint.Metadata.GetMetadata<AllowAnonymousServiceAttribute>() != null)
        {
            await next(context);
            return;
        }

        var message = check?.Outcome switch
        {
            null => "Sign in to continue.",
            SessionCheckOutcome.Blocked => "This account has been blocked.",
            SessionCheckOutcome.Expired => "You've been signed out because this device wasn't used for a while. Sign in again.",
            _ => "You've been signed out. Sign in again."
        };

        const int status = StatusCodes.Status401Unauthorized;
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(context.Response.Body, ApiResponses.Create(status, null, message),
            json.Value.JsonSerializerOptions, context.RequestAborted);
    }

    private static string? TokenOf(HttpContext context)
    {
        if (context.Features.Get<GatewayRequestFeature>() is { } gateway)
            return string.IsNullOrWhiteSpace(gateway.SessionToken) ? null : gateway.SessionToken;

        var header = context.Request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) && header.Length > 7 ? header[7..].Trim() : null;
    }
}
