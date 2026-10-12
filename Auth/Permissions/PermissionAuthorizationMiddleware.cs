using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Options;
using TournamentScheduler.Api.Gateway;
using TournamentScheduler.Api.Http;
using TournamentScheduler.Api.Models.Accounts;

namespace TournamentScheduler.Api.Auth.Permissions;

/// <summary>
/// May this kind of account call this service at all? Runs after the session check, before the
/// action. In order:
/// <list type="number">
/// <item>Anonymous services (sign-in, health) — yes; the session check already let them through.</item>
/// <item>Admin-only (portal) services — no, unless the session is a portal one (code and password).</item>
/// <item>Protected services — yes for every signed-in user (Admin only, for an admin-only one),
/// whatever the table says: nobody can lock people out of signing out.</item>
/// <item>Switched off by an admin, or gone from the code — no, for everyone: 403 "currently unavailable".</item>
/// <item>Otherwise only if the user's role is ticked — and never EndUser for an admin-only service,
/// even if ticked: 403 "you don't have access".</item>
/// </list>
/// Whether the user may touch a particular tournament is the services' own, later check.
/// </summary>
public sealed class PermissionAuthorizationMiddleware(RequestDelegate next, ServiceRegistry registry, PermissionCache cache, IOptions<JsonOptions> json)
{
    public async Task InvokeAsync(HttpContext context, CurrentUser currentUser)
    {
        var endpoint = context.GetEndpoint();
        var id = endpoint?.Metadata.GetMetadata<ServiceRequestIdAttribute>()?.Id;
        var service = id == null || endpoint!.Metadata.GetMetadata<ControllerActionDescriptor>() == null ? null : registry.Find(id);

        if (service == null || service.IsAnonymous || !currentUser.IsSignedIn)
        {
            await next(context);
            return;
        }

        // The portal's services need the portal's sign-in (code and password), not just a code:
        // an admin signed in to the phone app can't use them from there.
        if (service.IsAdminOnly && !currentUser.IsPortal)
        {
            await Refuse(context, StatusCodes.Status403Forbidden, "Sign in to the admin portal to do this.");
            return;
        }

        var roleId = currentUser.RoleId;
        if (service.IsProtected && (!service.IsAdminOnly || roleId == RoleIds.Admin))
        {
            await next(context);
            return;
        }

        var snapshot = await cache.GetAsync(context.RequestAborted);
        if (snapshot == null)
        {
            await Refuse(context, StatusCodes.Status503ServiceUnavailable, "The server couldn't load its permissions. Try again in a minute.");
            return;
        }

        if (!snapshot.Services.TryGetValue(service.Id, out var entry))
        {
            await Refuse(context, StatusCodes.Status403Forbidden, "This feature isn't available yet. Try again in a minute.");
            return;
        }

        if (!service.IsProtected && (entry.IsDisabled || entry.IsObsolete))
        {
            await Refuse(context, StatusCodes.Status403Forbidden, "This feature is currently unavailable.");
            return;
        }

        var allowed = entry.RoleIds.Contains(roleId) && !(service.IsAdminOnly && roleId == RoleIds.EndUser);
        if (!allowed)
        {
            await Refuse(context, StatusCodes.Status403Forbidden, "You don't have access to this.");
            return;
        }

        await next(context);
    }

    private Task Refuse(HttpContext context, int status, string message)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";
        return JsonSerializer.SerializeAsync(context.Response.Body, ApiResponses.Create(status, null, message),
            json.Value.JsonSerializerOptions, context.RequestAborted);
    }
}
