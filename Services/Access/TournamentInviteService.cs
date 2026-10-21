using TournamentScheduler.Api.Data.DataServices;
using TournamentScheduler.Api.Models;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Models.Messaging;
using TournamentScheduler.Api.Services.Notifications;

namespace TournamentScheduler.Api.Services.Access;

public interface ITournamentInviteService
{
    /// <summary>
    /// Turns this person's open invites (to their email or number) into owner / scorer places.
    /// Stages the change; the caller saves. Returns how many tournaments they joined.
    /// </summary>
    Task<int> AcceptWaitingAsync(User user);
}

/// <summary>
/// Invites waiting for someone to sign up (phase 6C). Run when an email or number is proved — at
/// sign-in, and when one is added to an account. An expired or cancelled invite gives nothing. An
/// invite never lowers a role: an owner invited as scorer stays an owner, and the creator is an
/// owner already.
/// </summary>
public class TournamentInviteService(
    IDataService<TournamentInvite> invites,
    IDataService<TournamentMember> members,
    IDataService<Tournament> tournaments,
    INotifier notifier,
    TimeProvider clock) : ServiceBase, ITournamentInviteService
{
    public async Task<int> AcceptWaitingAsync(User user)
    {
        if (user.EmailId == null && user.PhoneNumber == null) return 0;

        var now = clock.GetUtcNow().UtcDateTime;
        var email = user.EmailId;
        var phone = user.PhoneNumber;
        var waiting = await invites.ListAsync(i =>
            i.AcceptedAt == null && !i.IsDisabled && i.ExpiresAt > now
            && ((email != null && i.Target == email) || (phone != null && i.Target == phone)));
        if (waiting.Count == 0) return 0;

        var tournamentIds = waiting.Select(i => i.TournamentId).Distinct().ToList();
        var tournamentRows = (await tournaments.QueryAsync(q => q
                .Where(t => tournamentIds.Contains(t.Id))
                .Select(t => new { t.Id, t.CreatedByUserId, t.Name }), tracking: false))
            .ToDictionary(t => t.Id);
        var mine = (await members.ListAsync(m => m.UserId == user.UserId && tournamentIds.Contains(m.TournamentId)))
            .ToDictionary(m => m.TournamentId);

        var joined = 0;
        foreach (var group in waiting.GroupBy(i => i.TournamentId))
        {
            // Invited by email and by number to the same tournament: the higher role wins.
            var role = group.Any(i => i.MemberRole == TournamentMemberRole.Owner) ? TournamentMemberRole.Owner : TournamentMemberRole.Scorer;
            var invitedBy = group.OrderBy(i => i.Crd).First().CrdBy;
            foreach (var invite in group)
            {
                invite.AcceptedAt = now;
                invite.AcceptedByUserId = user.UserId;
                invite.Modified(now, user.UserId);
            }

            if (!tournamentRows.TryGetValue(group.Key, out var tournament) || tournament.CreatedByUserId == user.UserId) continue;

            if (mine.TryGetValue(group.Key, out var member))
            {
                if (!member.IsDisabled && member.MemberRole == TournamentMemberRole.Owner) continue;
                if (!member.IsDisabled && member.MemberRole == role) continue;
                member.MemberRole = role; // back after leaving, or a scorer made owner
                member.IsDisabled = false;
                member.Modified(now, invitedBy);
            }
            else
            {
                var added = new TournamentMember { TournamentId = group.Key, UserId = user.UserId, MemberRole = role };
                added.Created(now, invitedBy);
                members.Add(added);
            }
            joined++;

            var phrase = role == TournamentMemberRole.Owner ? "an owner" : "a scorer";
            var contact = user.EmailId != null && group.Any(i => i.Target == user.EmailId) ? "email" : "number";
            var link = new NotificationLink(group.Key);
            await notifier.NotifyAsync(user.UserId, NotificationTemplateCode.InviteJoined,
                Notifier.With(("Role", phrase), ("TournamentName", tournament.Name), ("ContactKind", contact)), link);
            await notifier.NotifyAsync(invitedBy, NotificationTemplateCode.InviteAccepted,
                Notifier.With(("PersonName", user.Name), ("TournamentName", tournament.Name), ("ContactKind", contact), ("Role", phrase)), link);
        }
        return joined;
    }
}
