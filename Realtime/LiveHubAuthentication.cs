using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TournamentScheduler.Api.Http;
using TournamentScheduler.Api.Logging;

namespace TournamentScheduler.Api.Realtime;

/// <summary>
/// The live line's door, and the one reviewed way into the API that doesn't go through the gateway
/// (a WebSocket is one long connection, not a request the gateway can unwrap). It lets in only a
/// ticket from <c>LIVE_TICKET</c> — itself a gateway service, signed in — and takes the ticket out
/// of the address before anything further in can log it. The hub isn't an action, so gateway
/// enforcement and the session check pass it by; this is its check.
/// <para>
/// Browsers can't set headers on a WebSocket, so SignalR puts the ticket in the address there; in
/// React Native (the phone app) it sends <c>Authorization: Bearer &lt;ticket&gt;</c> instead. Both are
/// read, and both are removed before the request goes further. Until 2026-10-26 only the address was
/// read, so every phone was refused and fell back to polling.
/// </para>
/// </summary>
public static class LiveHubAuthentication
{
    public const string HolderKey = "live.ticketHolder";
    public const string TicketParameter = "access_token";

    public static IApplicationBuilder UseLiveHubAuthentication(this IApplicationBuilder app) =>
        app.UseWhen(context => context.Request.Path.StartsWithSegments(LiveHub.Path), branch => branch.Use(async (context, next) =>
        {
            var query = context.Request.Query;
            var ticket = query[TicketParameter].ToString();
            context.Request.QueryString = QueryString.Create(query
                .Where(p => !string.Equals(p.Key, TicketParameter, StringComparison.OrdinalIgnoreCase))
                .SelectMany(p => p.Value.Select(v => new KeyValuePair<string, string?>(p.Key, v))));

            var header = context.Request.Headers.Authorization.ToString();
            if (string.IsNullOrEmpty(ticket) && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                ticket = header[7..].Trim();
            context.Request.Headers.Remove("Authorization");

            var holder = context.RequestServices.GetRequiredService<LiveTickets>().Redeem(ticket);
            if (holder == null)
            {
                const int status = StatusCodes.Status401Unauthorized;
                var json = context.RequestServices.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions;
                context.Response.StatusCode = status;
                context.Response.ContentType = "application/json; charset=utf-8";
                await JsonSerializer.SerializeAsync(context.Response.Body,
                    ApiResponses.Create(status, null, "This live updates ticket has expired. Ask for a new one."), json, context.RequestAborted);
                return;
            }

            context.Items[HolderKey] = holder;
            if (RequestLogContext.Of(context) is { } log) log.UserId = holder.UserId;
            await next(context);
        }));
}
