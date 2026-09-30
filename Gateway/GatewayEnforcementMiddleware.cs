using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Options;
using TournamentScheduler.Api.Http;

namespace TournamentScheduler.Api.Gateway;

/// <summary>
/// Runs after routing, so it knows which action a request is for. When encryption is enforced, an
/// action called on its own URL — not through the gateway — is refused unless it skips encryption.
/// Without this the gateway would be optional, and a client could go on sending tournament data in
/// the clear.
/// </summary>
public sealed class GatewayEnforcementMiddleware(RequestDelegate next, IOptions<GatewayOptions> options, IOptions<JsonOptions> json)
{
    private readonly GatewayOptions _options = options.Value;
    private readonly HashSet<string> _plainIds = options.Value.PlainServiceIds.ToHashSet(StringComparer.Ordinal);

    public async Task InvokeAsync(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        if (!_options.EnforceEncryption
            || context.Features.Get<GatewayRequestFeature>() != null
            || endpoint?.Metadata.GetMetadata<ControllerActionDescriptor>() == null
            || AllowsPlain(endpoint))
        {
            await next(context);
            return;
        }

        const int status = StatusCodes.Status403Forbidden;
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(context.Response.Body,
            ApiResponses.Create(status, null, "This endpoint must be called through the secure gateway."),
            json.Value.JsonSerializerOptions, context.RequestAborted);
    }

    private bool AllowsPlain(Endpoint endpoint) =>
        endpoint.Metadata.GetMetadata<SkipEncryptionAttribute>() != null
        || endpoint.Metadata.GetMetadata<ServiceRequestIdAttribute>() is { } id && _plainIds.Contains(id.Id);
}
