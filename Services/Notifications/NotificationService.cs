using TournamentScheduler.Api.Auth;
using TournamentScheduler.Api.Data;
using TournamentScheduler.Api.Data.DataServices;
using TournamentScheduler.Api.Messaging;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Models.Notifications;

namespace TournamentScheduler.Api.Services.Notifications;

public interface INotificationService
{
    Task<ServiceResult<NotificationPageDto>> ListAsync(long? before, int? take);
    Task<ServiceResult<UnreadCountDto>> UnreadCountAsync();
    Task<ServiceResult<UnreadCountDto>> MarkReadAsync(long notificationId);
    Task<ServiceResult<UnreadCountDto>> MarkAllReadAsync();

    /// <summary>Deletes notifications older than <see cref="NotificationService.KeepDays"/>. Returns how many.</summary>
    Task<int> PurgeOldAsync();

    /// <summary>Sends the notification emails waiting to go (the email job, every 30 seconds). Returns how many went.</summary>
    Task<int> SendPendingEmailsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Your inbox (phase 6D): newest first, a page at a time, and how many are unread — which the bell
/// on Home asks for while it's on screen. Only ever your own notifications.
/// </summary>
public class NotificationService(
    IDataService<Notification> notifications,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IMessageSender messages,
    TimeProvider clock,
    ILogger<NotificationService> logger) : ServiceBase, INotificationService
{
    public const int PageSize = 30;
    public const int MaxPageSize = 100;

    /// <summary>Notifications are news, not a record: after this long they're deleted, read or not.</summary>
    public const int KeepDays = 90;

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<ServiceResult<NotificationPageDto>> ListAsync(long? before, int? take)
    {
        var me = currentUser.UserId;
        var size = Math.Clamp(take ?? PageSize, 1, MaxPageSize);
        var rows = await notifications.QueryAsync(q => q
            .Where(n => n.UserId == me && n.InInbox && (before == null || n.NotificationId < before))
            .OrderByDescending(n => n.NotificationId)
            .Take(size + 1)
            .Select(n => new NotificationDto
            {
                NotificationId = n.NotificationId,
                Kind = n.Kind,
                Title = n.Title,
                Body = n.Body,
                TournamentId = n.TournamentId,
                Sport = n.Sport,
                MatchId = n.MatchId,
                CreatedAt = n.CreatedAt,
                IsRead = n.ReadAt != null
            }), tracking: false);

        return Success(new NotificationPageDto
        {
            Items = rows.Take(size).ToList(),
            HasMore = rows.Count > size,
            UnreadCount = await Unread(me)
        });
    }

    public async Task<ServiceResult<UnreadCountDto>> UnreadCountAsync() =>
        Success(new UnreadCountDto { UnreadCount = await Unread(currentUser.UserId) });

    public async Task<ServiceResult<UnreadCountDto>> MarkReadAsync(long notificationId)
    {
        var me = currentUser.UserId;
        var notification = await notifications.FirstOrDefaultAsync(n => n.NotificationId == notificationId && n.UserId == me && n.InInbox);
        if (notification == null) return NotFound("That notification isn't there any more.");

        if (notification.ReadAt == null)
        {
            notification.ReadAt = Now;
            await unitOfWork.SaveChangesAsync();
        }
        return Success(new UnreadCountDto { UnreadCount = await Unread(me) });
    }

    public async Task<ServiceResult<UnreadCountDto>> MarkAllReadAsync()
    {
        var me = currentUser.UserId;
        var unread = await notifications.ListAsync(n => n.UserId == me && n.InInbox && n.ReadAt == null);
        if (unread.Count > 0)
        {
            var now = Now;
            foreach (var n in unread) n.ReadAt = now;
            await unitOfWork.SaveChangesAsync();
        }
        return Success(new UnreadCountDto { UnreadCount = 0 });
    }

    public async Task<int> PurgeOldAsync()
    {
        var cutOff = Now.AddDays(-KeepDays);
        var old = await notifications.ListAsync(n => n.CreatedAt < cutOff);
        if (old.Count == 0) return 0;
        notifications.RemoveRange(old);
        await unitOfWork.SaveChangesAsync();
        return old.Count;
    }

    /// <summary>A notification email is tried this many times, then given up on (the inbox still has it).</summary>
    public const int MaxEmailAttempts = 3;

    /// <summary>One older than this isn't news any more: it's not sent at all.</summary>
    public static readonly TimeSpan EmailStaleAfter = TimeSpan.FromDays(1);

    public async Task<int> SendPendingEmailsAsync(CancellationToken cancellationToken = default)
    {
        var since = Now - EmailStaleAfter;
        var waiting = await notifications.ListAsync(n => n.EmailTo != null && n.EmailSentAt == null
                                                         && n.EmailAttempts < MaxEmailAttempts && n.CreatedAt > since,
            q => q.OrderBy(n => n.NotificationId).Take(50));
        var sent = 0;
        foreach (var n in waiting)
        {
            n.EmailAttempts++;
            try
            {
                await messages.SendAsync(new OutgoingMessage(OtpChannel.Email, n.EmailTo!, n.EmailSubject ?? n.Title, n.EmailBody ?? n.Body, n.Kind.ToString()), cancellationToken);
                n.EmailSentAt = Now;
                sent++;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogError(e, "Couldn't email notification {NotificationId} (try {Attempt} of {Max}).", n.NotificationId, n.EmailAttempts, MaxEmailAttempts);
            }
        }
        if (waiting.Count > 0) await unitOfWork.SaveChangesAsync();
        return sent;
    }

    private Task<int> Unread(int userId) => notifications.CountAsync(n => n.UserId == userId && n.InInbox && n.ReadAt == null);
}

/// <summary>Sends notification emails a few seconds after they're made, so a request never waits on SMTP.</summary>
public sealed class NotificationEmailJob(IServiceScopeFactory scopes, ILogger<NotificationEmailJob> logger) : BackgroundService
{
    private static readonly TimeSpan Every = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<INotificationService>().SendPendingEmailsAsync(stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogError(e, "Couldn't send the waiting notification emails; trying again shortly.");
            }

            try
            {
                await Task.Delay(Every, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
