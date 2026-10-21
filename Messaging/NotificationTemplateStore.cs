using TournamentScheduler.Api.Data;
using TournamentScheduler.Api.Data.DataServices;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Models.Messaging;

namespace TournamentScheduler.Api.Messaging;

/// <summary>A template's wording on one channel, with the placeholders filled in.</summary>
public sealed record RenderedText(string Title, string Body);

/// <summary>
/// The templates, held in memory so a message costs no database trip (phase 6D). Loaded on first
/// use and every minute after, so an edit made in SQL applies within a minute; a save in the portal
/// calls <see cref="Invalidate"/> and applies at once. A load also adds a row, with the built-in
/// wording, for any template the table doesn't have yet — a new template appears in the portal on
/// the first message after a deploy.
///
/// A row that breaks the rules (edited by hand in SQL — say a code email without <c>{{Code}}</c>)
/// is skipped with a warning and the built-in wording used instead: a broken template must never
/// stop people signing in. If the database can't be read, the last copy (or the built-in wording)
/// is used.
/// </summary>
public sealed class NotificationTemplateStore(IServiceScopeFactory scopes, TimeProvider clock, ILogger<NotificationTemplateStore> logger)
{
    private static readonly TimeSpan Refresh = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromSeconds(10);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private Dictionary<NotificationTemplateCode, NotificationTemplate>? _templates;
    private DateTimeOffset _nextLoad = DateTimeOffset.MinValue;

    /// <summary>The template in use for <paramref name="code"/>.</summary>
    public async Task<NotificationTemplate> GetAsync(NotificationTemplateCode code, CancellationToken cancellationToken = default)
    {
        var templates = await LoadAsync(cancellationToken);
        return templates != null && templates.TryGetValue(code, out var row) ? row : NotificationTemplateCatalog.Get(code).Default;
    }

    public void Invalidate() => _nextLoad = DateTimeOffset.MinValue;

    private async Task<Dictionary<NotificationTemplateCode, NotificationTemplate>?> LoadAsync(CancellationToken cancellationToken)
    {
        if (_templates != null && clock.GetUtcNow() < _nextLoad) return _templates;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_templates != null && clock.GetUtcNow() < _nextLoad) return _templates;

            using var scope = scopes.CreateScope();
            var data = scope.ServiceProvider.GetRequiredService<IDataService<NotificationTemplate>>();
            var rows = await data.ListAsync(tracking: false);

            var missing = NotificationTemplateCatalog.All.Select(d => d.Code).Except(rows.Select(r => r.Code)).ToList();
            if (missing.Count > 0)
            {
                var now = clock.GetUtcNow().UtcDateTime;
                foreach (var code in missing)
                {
                    var row = NotificationTemplateCatalog.DefaultRow(code);
                    row.Created(now, SystemUser.Id);
                    data.Add(row);
                    rows.Add(row);
                }
                try
                {
                    await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
                }
                catch (DuplicateEntryException)
                {
                    // Another request added them a moment ago; their wording is the same.
                }
            }

            var usable = new Dictionary<NotificationTemplateCode, NotificationTemplate>();
            foreach (var row in rows.Where(r => !r.IsDisabled && Enum.IsDefined(r.Code)))
            {
                if (NotificationTemplateCatalog.Problem(row.Code, AsRequest(row)) is { } problem)
                {
                    logger.LogWarning("The {Code} template in the database is skipped ({Problem}); the built-in wording is used.", row.Code, problem);
                    continue;
                }
                usable[row.Code] = row;
            }
            _templates = usable;
            _nextLoad = clock.GetUtcNow() + Refresh;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "Couldn't load the message templates; {Using}.", _templates == null ? "the built-in wording is used" : "the previous ones stay in use");
            _nextLoad = clock.GetUtcNow() + RetryAfterFailure;
        }
        finally
        {
            _gate.Release();
        }
        return _templates;
    }

    public static SaveTemplateRequest AsRequest(NotificationTemplate t) => new()
    {
        IsEmail = t.IsEmail,
        EmailSubject = t.EmailSubject,
        EmailBody = t.EmailBody,
        IsSms = t.IsSms,
        SmsBody = t.SmsBody,
        SmsDltTemplateId = t.SmsDltTemplateId,
        IsNotification = t.IsNotification,
        NotificationTitle = t.NotificationTitle,
        NotificationBody = t.NotificationBody,
        IsPush = t.IsPush,
        PushTitle = t.PushTitle,
        PushBody = t.PushBody
    };
}

/// <summary>Filling a template in, per channel.</summary>
public static class TemplateText
{
    public static RenderedText Email(NotificationTemplate t, IReadOnlyDictionary<string, string> values) =>
        new(NotificationTemplateCatalog.Render(t.EmailSubject, values), NotificationTemplateCatalog.Render(t.EmailBody, values));

    public static string Sms(NotificationTemplate t, IReadOnlyDictionary<string, string> values) =>
        NotificationTemplateCatalog.Render(t.SmsBody, values);

    public static RenderedText Inbox(NotificationTemplate t, IReadOnlyDictionary<string, string> values) =>
        new(NotificationTemplateCatalog.Render(t.NotificationTitle, values), NotificationTemplateCatalog.Render(t.NotificationBody, values));
}
