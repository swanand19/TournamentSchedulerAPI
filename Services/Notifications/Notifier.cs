using TournamentScheduler.Api.Auth;
using TournamentScheduler.Api.Data;
using TournamentScheduler.Api.Data.DataServices;
using TournamentScheduler.Api.Messaging;
using TournamentScheduler.Api.Models;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Models.Cricket;
using TournamentScheduler.Api.Models.Messaging;
using TournamentScheduler.Api.Models.Notifications;
using TournamentScheduler.Api.Services.Access;

namespace TournamentScheduler.Api.Services.Notifications;

/// <summary>Where tapping a notification goes: a tournament, or one of its matches.</summary>
public readonly record struct NotificationLink(int TournamentId, Sport? Sport = null, int? MatchId = null)
{
    public static NotificationLink To(IScoredMatch match) =>
        new(match.TournamentId, match is CricketMatch ? Models.Sport.Cricket : Models.Sport.Football, match.Id);
}

public interface INotifier
{
    /// <summary>
    /// Tells someone, in the words of the <paramref name="code"/> template with <paramref name="values"/>
    /// filled in: a line in their inbox and/or an email waiting to go, as the template says. Stages
    /// it; the caller saves with its own change, so news of something that didn't happen is never
    /// kept. The System account gets nothing.
    /// </summary>
    Task NotifyAsync(int userId, NotificationTemplateCode code, IReadOnlyDictionary<string, string> values,
        NotificationLink? link = null, string? dedupeKey = null);

    /// <summary>The tournament's owners who can still read their inbox: the creator and every owner, not blocked or deleted.</summary>
    Task<List<int>> OwnersOfAsync(int tournamentId);

    Task<string> TournamentNameAsync(int tournamentId);

    /// <summary>How many of the tournament's matches there are and how many are over (finished or abandoned).</summary>
    Task<(int Total, int Over)> PlayedAsync(int tournamentId, Sport sport);

    /// <summary>
    /// After a scoring action: if it finished the tournament's last match, asks the owners to mark it
    /// complete — once per tournament. Saves its own change.
    /// </summary>
    Task AfterScoringAsync();

    /// <summary>Whether news with this dedupe key has gone out (to anyone).</summary>
    Task<bool> AlreadySentAsync(string dedupeKey);
}

/// <summary>
/// Makes notifications (phase 6D) for the services where things happen — scoring handovers, people
/// added, invites taken up, squad places linked, the last match played. The inbox itself is
/// <see cref="NotificationService"/>; the end-date reminders come from the hourly job.
/// </summary>
public class Notifier(
    IDataService<Notification> notifications,
    IDataService<Tournament> tournaments,
    IDataService<TournamentMember> members,
    IDataService<User> users,
    IDataService<Match> matches,
    IDataService<CricketMatch> cricketMatches,
    IUnitOfWork unitOfWork,
    ITournamentContext context,
    ICurrentUser currentUser,
    NotificationTemplateStore templates,
    TimeProvider clock) : ServiceBase, INotifier
{
    public async Task NotifyAsync(int userId, NotificationTemplateCode code, IReadOnlyDictionary<string, string> values,
        NotificationLink? link = null, string? dedupeKey = null)
    {
        if (userId == SystemUser.Id) return;
        var template = await templates.GetAsync(code);
        if (!template.IsNotification && !template.IsEmail) return; // switched off in the portal

        // The inbox wording is kept even for an email-only row: it's what the logs and the dedupe see.
        var inbox = TemplateText.Inbox(template.IsNotification ? template : NotificationTemplateCatalog.Get(code).Default, values);
        var row = new Notification
        {
            UserId = userId,
            Kind = NotificationTemplateCatalog.Get(code).Kind ?? NotificationKind.AddedToTournament,
            Title = Fit(inbox.Title, NotificationTemplateCatalog.TitleMax),
            Body = Fit(inbox.Body, NotificationTemplateCatalog.BodyMax),
            InInbox = template.IsNotification,
            TournamentId = link?.TournamentId,
            Sport = link?.Sport,
            MatchId = link?.MatchId,
            DedupeKey = dedupeKey,
            CreatedAt = clock.GetUtcNow().UtcDateTime
        };

        if (template.IsEmail && await users.QueryFirstOrDefaultAsync(q => q.Where(u => u.UserId == userId).Select(u => u.EmailId), tracking: false) is { } email)
        {
            var mail = TemplateText.Email(template, values);
            row.EmailTo = email;
            row.EmailSubject = Fit(mail.Title, NotificationTemplateCatalog.SubjectMax);
            row.EmailBody = Fit(mail.Body, NotificationTemplateCatalog.EmailBodyMax);
        }
        if (!row.InInbox && row.EmailTo == null) return; // email only, and they have no email

        notifications.Add(row);
    }

    /// <summary>Placeholder values, as name-value pairs.</summary>
    public static IReadOnlyDictionary<string, string> With(params (string Name, string Value)[] values) =>
        values.ToDictionary(v => v.Name, v => v.Value);

    public async Task<List<int>> OwnersOfAsync(int tournamentId)
    {
        var creator = await tournaments.QueryFirstOrDefaultAsync(q => q.Where(t => t.Id == tournamentId).Select(t => t.CreatedByUserId), tracking: false);
        var owners = await members.QueryAsync(q => q
            .Where(m => m.TournamentId == tournamentId && !m.IsDisabled && m.MemberRole == TournamentMemberRole.Owner)
            .Select(m => m.UserId), tracking: false);
        var ids = owners.Append(creator).Where(id => id != SystemUser.Id).Distinct().ToList();
        return await users.QueryAsync(q => q
            .Where(u => ids.Contains(u.UserId) && !u.IsDisabled && u.DeletedOn == null)
            .Select(u => u.UserId), tracking: false);
    }

    public async Task<string> TournamentNameAsync(int tournamentId) =>
        await tournaments.QueryFirstOrDefaultAsync(q => q.Where(t => t.Id == tournamentId).Select(t => t.Name), tracking: false) ?? "the tournament";

    public async Task<(int Total, int Over)> PlayedAsync(int tournamentId, Sport sport)
    {
        if (sport == Sport.Cricket)
        {
            var statuses = await cricketMatches.QueryAsync(q => q.Where(m => m.TournamentId == tournamentId).Select(m => m.Status), tracking: false);
            return (statuses.Count, statuses.Count(s => s is CricketMatchStatus.Completed or CricketMatchStatus.Abandoned));
        }
        var football = await matches.QueryAsync(q => q.Where(m => m.TournamentId == tournamentId).Select(m => m.Status), tracking: false);
        return (football.Count, football.Count(s => s == MatchStatus.Completed));
    }

    public async Task AfterScoringAsync()
    {
        // Only an action on a match that is now over can have finished the last one.
        if (context.Match is not { IsOver: true }) return;

        var tournamentId = context.TournamentId;
        var tournament = await tournaments.QueryFirstOrDefaultAsync(q => q
            .Where(t => t.Id == tournamentId)
            .Select(t => new { t.Name, t.Sport, t.Status }), tracking: false);
        if (tournament is not { Status: TournamentStatus.Live }) return;

        var (total, over) = await PlayedAsync(tournamentId, tournament.Sport);
        if (total == 0 || over < total) return;

        var key = $"all-played:{tournamentId}";
        if (await AlreadySentAsync(key)) return;

        // Whoever scored the last ball sees the same prompt on the tournament's page already.
        var me = currentUser.UserId;
        var owners = (await OwnersOfAsync(tournamentId)).Where(id => id != me).ToList();
        if (owners.Count == 0) return;

        foreach (var owner in owners)
            await NotifyAsync(owner, NotificationTemplateCode.AllMatchesPlayed, With(("TournamentName", tournament.Name)), new NotificationLink(tournamentId), key);
        await unitOfWork.SaveChangesAsync();
    }

    public Task<bool> AlreadySentAsync(string dedupeKey) => notifications.AnyAsync(n => n.DedupeKey == dedupeKey);

    /// <summary>"Home v Away", for any scored match.</summary>
    public static string Describe(IScoredMatch match) => match switch
    {
        Match m => $"{m.HomeTeamName} v {m.AwayTeamName}",
        CricketMatch c => $"{c.HomeTeamName} v {c.AwayTeamName}",
        _ => "the match"
    };

    private static string Fit(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
