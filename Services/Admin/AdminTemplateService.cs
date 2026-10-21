using TournamentScheduler.Api.Auth;
using TournamentScheduler.Api.Data;
using TournamentScheduler.Api.Data.DataServices;
using TournamentScheduler.Api.Messaging;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Models.Messaging;

namespace TournamentScheduler.Api.Services.Admin;

public interface IAdminTemplateService
{
    Task<ServiceResult<List<AdminTemplateDto>>> ListAsync();
    Task<ServiceResult<AdminTemplateDto>> SaveAsync(NotificationTemplateCode code, SaveTemplateRequest request);
    Task<ServiceResult<AdminTemplateDto>> ResetAsync(NotificationTemplateCode code);
}

/// <summary>
/// The admin portal's message templates (phase 6D): every message's wording per channel, which
/// placeholders it may use, and back to the built-in wording on reset. A save is checked against the
/// template's rules (<see cref="NotificationTemplateCatalog.Problem"/>) and applies at once.
/// </summary>
public class AdminTemplateService(
    IDataService<NotificationTemplate> templates,
    IDataService<User> users,
    IUnitOfWork unitOfWork,
    NotificationTemplateStore store,
    ICurrentUser currentUser,
    TimeProvider clock) : ServiceBase, IAdminTemplateService
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<ServiceResult<List<AdminTemplateDto>>> ListAsync()
    {
        await store.GetAsync(NotificationTemplateCode.SignInCode); // adds any template the table doesn't have yet
        var rows = (await templates.ListAsync(tracking: false)).ToDictionary(t => t.Code);
        var names = await NamesOf(rows.Values.Select(r => r.LmdBy));
        return Success(NotificationTemplateCatalog.All
            .Select(d => ToDto(d, rows.GetValueOrDefault(d.Code), names))
            .ToList());
    }

    public async Task<ServiceResult<AdminTemplateDto>> SaveAsync(NotificationTemplateCode code, SaveTemplateRequest request)
    {
        if (!Enum.IsDefined(code)) return NotFound("There's no such message template.");
        var definition = NotificationTemplateCatalog.Get(code);

        // Texts on channels this template can't use are dropped, so they can't hide a broken placeholder.
        if (definition.Family == TemplateFamily.Direct)
        {
            request.NotificationTitle = request.NotificationBody = request.PushTitle = request.PushBody = null;
        }
        else
        {
            request.SmsBody = request.SmsDltTemplateId = null;
        }
        if (NotificationTemplateCatalog.Problem(code, request) is { } problem) return BadRequest(problem);

        var row = await templates.FirstOrDefaultAsync(t => t.Code == code);
        var now = Now;
        if (row == null)
        {
            row = NotificationTemplateCatalog.DefaultRow(code);
            row.Created(now, currentUser.UserId);
            templates.Add(row);
        }
        row.IsEmail = request.IsEmail;
        row.EmailSubject = Clean(request.EmailSubject);
        row.EmailBody = Clean(request.EmailBody);
        row.IsSms = request.IsSms;
        row.SmsBody = Clean(request.SmsBody);
        row.SmsDltTemplateId = Clean(request.SmsDltTemplateId);
        row.IsNotification = request.IsNotification;
        row.NotificationTitle = Clean(request.NotificationTitle);
        row.NotificationBody = Clean(request.NotificationBody);
        row.IsPush = request.IsPush;
        row.PushTitle = Clean(request.PushTitle);
        row.PushBody = Clean(request.PushBody);
        row.IsDisabled = false;
        row.Modified(now, currentUser.UserId);

        await unitOfWork.SaveChangesAsync();
        store.Invalidate();
        return Success(ToDto(definition, row, await NamesOf([row.LmdBy])));
    }

    public async Task<ServiceResult<AdminTemplateDto>> ResetAsync(NotificationTemplateCode code)
    {
        if (!Enum.IsDefined(code)) return NotFound("There's no such message template.");
        var definition = NotificationTemplateCatalog.Get(code);
        var now = Now;
        var row = await templates.FirstOrDefaultAsync(t => t.Code == code);
        if (row == null)
        {
            row = NotificationTemplateCatalog.DefaultRow(code);
            row.Created(now, currentUser.UserId);
            templates.Add(row);
        }
        else
        {
            // The DLT id is about registration, not wording: a reset keeps it.
            var dlt = row.SmsDltTemplateId;
            NotificationTemplateCatalog.CopyTexts(definition.Default, row);
            row.SmsDltTemplateId = dlt;
            row.IsDisabled = false;
        }
        row.Modified(now, currentUser.UserId);

        await unitOfWork.SaveChangesAsync();
        store.Invalidate();
        return Success(ToDto(definition, row, await NamesOf([row.LmdBy])));
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Replace("\r\n", "\n").Trim();

    private async Task<Dictionary<int, string>> NamesOf(IEnumerable<int> ids)
    {
        var wanted = ids.Distinct().ToList();
        return (await users.QueryAsync(q => q.Where(u => wanted.Contains(u.UserId)).Select(u => new { u.UserId, u.Name }), tracking: false))
            .ToDictionary(u => u.UserId, u => u.Name);
    }

    private static AdminTemplateDto ToDto(TemplateDefinition d, NotificationTemplate? row, Dictionary<int, string> names)
    {
        var t = row ?? d.Default;
        var direct = d.Family == TemplateFamily.Direct;
        const string ByContact = "Goes by email or text, whichever the person gave — so it can't be switched off.";
        var customised = row != null && !NotificationTemplateCatalog.IsDefault(row);
        return new AdminTemplateDto
        {
            Code = d.Code,
            Name = d.Name,
            Group = d.Group,
            Description = d.Description,
            Channels =
            [
                new() { Channel = TemplateChannel.Email, Supported = true, Locked = direct, Note = direct ? ByContact : null },
                new() { Channel = TemplateChannel.Sms, Supported = direct, Locked = direct,
                        Note = direct ? ByContact + " Texts go to the log until an SMS provider is set up." : "Notifications don't go by text." },
                new() { Channel = TemplateChannel.Notification, Supported = !direct, Note = direct ? "Codes and invites aren't inbox messages." : null },
                new() { Channel = TemplateChannel.Push, Supported = !direct,
                        Note = direct ? "Codes and invites aren't push messages." : "Push isn't switched on yet (it needs the app's development build). The words are kept for when it is; empty means the same as the inbox." }
            ],
            Placeholders = d.Placeholders.Select(p => new TemplatePlaceholderDto { Name = p.Name, Description = p.Description, Sample = p.Sample, Required = p.Required }).ToList(),
            IsCustomised = customised,
            ChangedBy = customised && row!.LmdBy != SystemUser.Id ? names.GetValueOrDefault(row.LmdBy) : null,
            ChangedAt = customised ? row!.Lmd : null,
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
}
