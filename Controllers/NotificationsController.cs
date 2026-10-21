using Microsoft.AspNetCore.Mvc;
using TournamentScheduler.Api.Auth.Permissions;
using TournamentScheduler.Api.Auth.TournamentAccess;
using TournamentScheduler.Api.Gateway;
using TournamentScheduler.Api.Http;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Models.Notifications;
using TournamentScheduler.Api.Services.Notifications;

namespace TournamentScheduler.Api.Controllers;

/// <summary>Your inbox (phase 6D). Always the signed-in person's own; a notification is {notificationId}.</summary>
[ApiController]
[Route("api/notifications")]
[DefaultRoles(RoleIds.EndUser)]
[NotTournamentScoped] // an inbox spans tournaments
public class NotificationsController : ApiController
{
    private readonly INotificationService _notifications;

    public NotificationsController(INotificationService notifications) => _notifications = notifications;

    // GET api/notifications?before=123&take=30
    [HttpGet]
    [ServiceRequestId("NOTIFICATION_LIST")]
    public async Task<ActionResult<ApiResponse<NotificationPageDto>>> List([FromQuery] long? before, [FromQuery] int? take) =>
        Respond(await _notifications.ListAsync(before, take));

    // GET api/notifications/unread — the bell's count, asked for while Home is on screen
    [HttpGet("unread")]
    [ServiceRequestId("NOTIFICATION_UNREAD")]
    public async Task<ActionResult<ApiResponse<UnreadCountDto>>> Unread() =>
        Respond(await _notifications.UnreadCountAsync());

    // POST api/notifications/123/read
    [HttpPost("{notificationId}/read")]
    [ServiceRequestId("NOTIFICATION_READ")]
    public async Task<ActionResult<ApiResponse<UnreadCountDto>>> Read(long notificationId) =>
        Respond(await _notifications.MarkReadAsync(notificationId));

    // POST api/notifications/read-all
    [HttpPost("read-all")]
    [ServiceRequestId("NOTIFICATION_READ_ALL")]
    public async Task<ActionResult<ApiResponse<UnreadCountDto>>> ReadAll() =>
        Respond(await _notifications.MarkAllReadAsync());
}
