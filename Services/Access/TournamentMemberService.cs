using System.Globalization;
using Microsoft.Extensions.Options;
using TournamentScheduler.Api.Auth;
using TournamentScheduler.Api.Data;
using TournamentScheduler.Api.Data.DataServices;
using TournamentScheduler.Api.Messaging;
using TournamentScheduler.Api.Models;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Models.Cricket;
using TournamentScheduler.Api.Services.Accounts;
using TournamentScheduler.Api.Models.Messaging;
using TournamentScheduler.Api.Services.Notifications;

namespace TournamentScheduler.Api.Services.Access;

public interface ITournamentMemberService
{
    Task<ServiceResult<TournamentPeopleDto>> ListAsync();
    Task<ServiceResult<TournamentPeopleDto>> AddAsync(AddTournamentMemberRequest request);
    Task<ServiceResult<TournamentPeopleDto>> RemoveAsync(int userId);
    Task<ServiceResult<TournamentPeopleDto>> ResendInviteAsync(int inviteId);
    Task<ServiceResult<TournamentPeopleDto>> CancelInviteAsync(int inviteId);
    Task<ServiceResult<bool>> LeaveAsync();
}

/// <summary>
/// A tournament's owners and scorers. Owners add people by the email or mobile number they sign in
/// with, and remove them; anyone but the creator can leave (a player leaving drops their team
/// link). The creator is always an owner and can't be removed — so a tournament can never be left
/// without one. Someone who goes stops holding any match's scoring.
///
/// Someone with no account yet gets an invite (phase 6C): saved, sent to them, and turned into the
/// role when they sign up with that email or number (<see cref="TournamentInviteService"/>). Owners
/// see open invites and can send one again (which renews it) or cancel it.
/// </summary>
public class TournamentMemberService : ServiceBase, ITournamentMemberService
{
    private readonly IDataService<TournamentMember> _members;
    private readonly IDataService<TournamentInvite> _invites;
    private readonly IDataService<User> _users;
    private readonly IDataService<Tournament> _tournaments;
    private readonly IDataService<Match> _matches;
    private readonly IDataService<CricketMatch> _cricketMatches;
    private readonly IDataService<Player> _players;
    private readonly IPlayerLinkService _links;
    private readonly IMessageSender _messages;
    private readonly INotifier _notifier;
    private readonly NotificationTemplateStore _templates;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITournamentContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly AuthOptions _auth;
    private readonly SmsOptions _sms;
    private readonly TimeProvider _clock;
    private readonly ILogger<TournamentMemberService> _logger;

    public TournamentMemberService(
        IDataService<TournamentMember> members,
        IDataService<TournamentInvite> invites,
        IDataService<User> users,
        IDataService<Tournament> tournaments,
        IDataService<Match> matches,
        IDataService<CricketMatch> cricketMatches,
        IDataService<Player> players,
        IPlayerLinkService links,
        IMessageSender messages,
        INotifier notifier,
        NotificationTemplateStore templates,
        IUnitOfWork unitOfWork,
        ITournamentContext context,
        ICurrentUser currentUser,
        IOptions<AuthOptions> auth,
        IOptions<SmsOptions> sms,
        TimeProvider clock,
        ILogger<TournamentMemberService> logger)
    {
        _members = members;
        _invites = invites;
        _users = users;
        _tournaments = tournaments;
        _matches = matches;
        _cricketMatches = cricketMatches;
        _players = players;
        _links = links;
        _messages = messages;
        _notifier = notifier;
        _templates = templates;
        _unitOfWork = unitOfWork;
        _context = context;
        _currentUser = currentUser;
        _auth = auth.Value;
        _sms = sms.Value;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>An invite can be sent again after this long — enough to stop a double tap texting twice.</summary>
    public static readonly TimeSpan ResendAfter = TimeSpan.FromMinutes(1);

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public async Task<ServiceResult<TournamentPeopleDto>> ListAsync() => Success(await People(null));

    private async Task<TournamentPeopleDto> People(string? notice)
    {
        var tournamentId = _context.TournamentId;
        var creatorId = await CreatorId();
        var rows = await _members.QueryAsync(q => q
            .Where(m => m.TournamentId == tournamentId && !m.IsDisabled)
            .Select(m => new { m.UserId, m.MemberRole, m.User!.Name, m.User.EmailId, m.User.PhoneNumber }), tracking: false);

        var me = _currentUser.UserId;
        var owner = _context.IsOwner;
        var members = rows
            .Select(r => new TournamentMemberDto
            {
                UserId = r.UserId,
                Name = r.Name,
                Email = owner ? r.EmailId : null,
                Phone = owner ? r.PhoneNumber : null,
                Role = r.UserId == creatorId ? "Creator" : r.MemberRole.ToString(),
                IsCreator = r.UserId == creatorId,
                IsMe = r.UserId == me,
                CanRemove = owner && r.UserId != creatorId && r.UserId != me
            })
            .OrderBy(m => m.IsCreator ? 0 : m.Role == nameof(TournamentMemberRole.Owner) ? 1 : 2)
            .ThenBy(m => m.Name)
            .ToList();

        var invites = new List<TournamentInviteDto>();
        if (owner)
        {
            var now = Now;
            var open = await _invites.QueryAsync(q => q
                .Where(i => i.TournamentId == tournamentId && i.AcceptedAt == null && !i.IsDisabled)
                .OrderByDescending(i => i.LastSentAt)
                .Select(i => new { i.TournamentInviteId, i.Target, i.Channel, i.MemberRole, i.CrdBy, i.LastSentAt, i.ExpiresAt }), tracking: false);
            var inviterIds = open.Select(i => i.CrdBy).Distinct().ToList();
            var inviters = (await _users.QueryAsync(q => q.Where(u => inviterIds.Contains(u.UserId)).Select(u => new { u.UserId, u.Name }), tracking: false))
                .ToDictionary(u => u.UserId, u => u.Name);
            invites = open.Select(i => new TournamentInviteDto
            {
                InviteId = i.TournamentInviteId,
                Contact = i.Target,
                Channel = i.Channel,
                Role = i.MemberRole.ToString(),
                InvitedBy = inviters.GetValueOrDefault(i.CrdBy, ""),
                SentAt = i.LastSentAt,
                ExpiresAt = i.ExpiresAt,
                IsExpired = i.ExpiresAt <= now
            }).ToList();
        }

        return new TournamentPeopleDto { Members = members, Invites = invites, Notice = notice };
    }

    public async Task<ServiceResult<TournamentPeopleDto>> AddAsync(AddTournamentMemberRequest request)
    {
        var (read, problem) = string.IsNullOrWhiteSpace(request.Contact) && request.Email != null
            ? (AccountService.NormaliseEmail(request.Email) is { } email ? new Contact(ContactKind.Email, email) : (Contact?)null, "Enter a valid email address.")
            : Contacts.Read(request.Contact, _sms.IsEnabled);
        if (read is not { } to) return BadRequest(problem!);
        if (!Enum.IsDefined(request.Role)) return BadRequest("Choose Owner or Scorer.");

        var user = await _users.QueryFirstOrDefaultAsync(q => q
            .Where(u => (to.IsEmail ? u.EmailId == to.Value : u.PhoneNumber == to.Value) && u.DeletedOn == null)
            .Select(u => new { u.UserId, u.Name, u.IsDisabled }), tracking: false);
        if (user is { IsDisabled: true })
            return Conflict("That account has been blocked, so it can't be added.");
        if (user == null)
            return await InviteAsync(to, request.Role);

        var tournamentId = _context.TournamentId;
        if (user.UserId == await CreatorId()) return Conflict($"{user.Name} created this tournament, so they're already an owner.");

        var now = Now;
        var existing = await _members.FirstOrDefaultAsync(m => m.TournamentId == tournamentId && m.UserId == user.UserId);
        if (existing is { IsDisabled: false } && existing.MemberRole == request.Role)
            return Conflict($"{user.Name} is already {RolePhrase(request.Role)}.");

        if (existing == null)
        {
            var member = new TournamentMember { TournamentId = tournamentId, UserId = user.UserId, MemberRole = request.Role };
            member.Created(now, _currentUser.UserId);
            _members.Add(member);
        }
        else
        {
            // Promote, demote, or bring back someone who left.
            existing.MemberRole = request.Role;
            existing.IsDisabled = false;
            existing.Modified(now, _currentUser.UserId);
        }

        await _notifier.NotifyAsync(user.UserId,
            request.Role == TournamentMemberRole.Owner ? NotificationTemplateCode.AddedAsOwner : NotificationTemplateCode.AddedAsScorer,
            Notifier.With(("ActorName", string.IsNullOrWhiteSpace(_currentUser.Name) ? "Someone" : _currentUser.Name),
                ("TournamentName", await _notifier.TournamentNameAsync(tournamentId))),
            new NotificationLink(tournamentId));

        try
        {
            await _unitOfWork.SaveChangesAsync();
        }
        catch (DuplicateEntryException)
        {
            return Conflict($"{user.Name} was added a moment ago. Reload the list.");
        }
        return Success(await People($"{user.Name} is now {RolePhrase(request.Role)}."));
    }

    /// <summary>No account with this email or number: invite them, or renew the invite they already have.</summary>
    private async Task<ServiceResult<TournamentPeopleDto>> InviteAsync(Contact to, TournamentMemberRole role)
    {
        var tournamentId = _context.TournamentId;
        var now = Now;
        var invite = await _invites.FirstOrDefaultAsync(i => i.TournamentId == tournamentId && i.Target == to.Value && i.AcceptedAt == null && !i.IsDisabled);
        if (invite != null && now - invite.LastSentAt < ResendAfter)
            return TooManyRequests(WaitMessage(invite, now));

        if (invite == null)
        {
            invite = new TournamentInvite { TournamentId = tournamentId, Channel = to.Channel, Target = to.Value };
            invite.Created(now, _currentUser.UserId);
            _invites.Add(invite);
        }
        else
        {
            invite.Modified(now, _currentUser.UserId);
        }
        invite.MemberRole = role;
        invite.LastSentAt = now;
        invite.ExpiresAt = now.AddDays(_auth.InviteDays);

        try
        {
            await _unitOfWork.SaveChangesAsync();
        }
        catch (DuplicateEntryException)
        {
            return Conflict($"{Shown(to.Value)} was invited a moment ago. Reload the list.");
        }

        var sent = await SendAsync(invite);
        var who = Shown(to.Value);
        return Success(await People(sent
            ? $"No account uses {who} yet, so we've sent an invite. They'll be {RolePhrase(role)} as soon as they sign up with it."
            : $"No account uses {who} yet. The invite is saved, but we couldn't send it just now — try Send again in a minute. They'll still be {RolePhrase(role)} if they sign up with it."));
    }

    public async Task<ServiceResult<TournamentPeopleDto>> ResendInviteAsync(int inviteId)
    {
        var invite = await OpenInvite(inviteId);
        if (invite == null) return NotFound("That invite isn't open any more. Reload the list.");
        if (invite.Channel == OtpChannel.Sms && !_sms.IsEnabled) return BadRequest(Contacts.PhoneOff);

        var now = Now;
        if (now - invite.LastSentAt < ResendAfter) return TooManyRequests(WaitMessage(invite, now));

        invite.LastSentAt = now;
        invite.ExpiresAt = now.AddDays(_auth.InviteDays);
        invite.Modified(now, _currentUser.UserId);
        await _unitOfWork.SaveChangesAsync();

        var sent = await SendAsync(invite);
        return Success(await People(sent
            ? $"Invite sent again to {Shown(invite.Target)}."
            : $"We couldn't send the invite to {Shown(invite.Target)} just now. Try again in a minute."));
    }

    public async Task<ServiceResult<TournamentPeopleDto>> CancelInviteAsync(int inviteId)
    {
        var invite = await OpenInvite(inviteId);
        if (invite == null) return NotFound("That invite isn't open any more. Reload the list.");

        invite.IsDisabled = true;
        invite.Modified(Now, _currentUser.UserId);
        await _unitOfWork.SaveChangesAsync();
        return Success(await People($"Invite to {Shown(invite.Target)} cancelled. Signing up with it won't add them now."));
    }

    public async Task<ServiceResult<TournamentPeopleDto>> RemoveAsync(int userId)
    {
        var tournamentId = _context.TournamentId;
        if (userId == _currentUser.UserId) return BadRequest("To leave the tournament yourself, use Leave.");

        if (userId == await CreatorId()) return Forbidden("The person who created the tournament can't be removed.");

        var member = await _members.FirstOrDefaultAsync(m => m.TournamentId == tournamentId && m.UserId == userId && !m.IsDisabled);
        if (member == null) return NotFound("That person isn't part of this tournament.");

        _members.Remove(member);
        await ReleaseScoring(tournamentId, userId);
        await _unitOfWork.SaveChangesAsync();
        return Success(await People(null));
    }

    public async Task<ServiceResult<bool>> LeaveAsync()
    {
        if (_context.IsCreator) return Conflict("You created this tournament, so you can't leave it. You can delete it instead.");

        var tournamentId = _context.TournamentId;
        var me = _currentUser.UserId;
        var member = await _members.FirstOrDefaultAsync(m => m.TournamentId == tournamentId && m.UserId == me);
        if (member != null) _members.Remove(member);

        // A player leaves their team link too; the place and its name stay on the scorecards.
        var place = await _players.FirstOrDefaultAsync(p => p.TournamentId == tournamentId && p.UserId == me);
        if (place != null) _links.Unlink(place);

        await ReleaseScoring(tournamentId, me);
        await _unitOfWork.SaveChangesAsync();
        return Success(true);
    }

    // -----------------------------------------------------------------

    private Task<TournamentInvite?> OpenInvite(int inviteId)
    {
        var tournamentId = _context.TournamentId;
        return _invites.FirstOrDefaultAsync(i => i.TournamentInviteId == inviteId && i.TournamentId == tournamentId && i.AcceptedAt == null && !i.IsDisabled);
    }

    private Task<int> CreatorId()
    {
        var tournamentId = _context.TournamentId;
        return _tournaments.QueryFirstOrDefaultAsync(q => q.Where(t => t.Id == tournamentId).Select(t => t.CreatedByUserId), tracking: false);
    }

    /// <summary>
    /// Sends the invite. A failure is logged and reported in the notice, never as an error: the
    /// invite is saved either way, and signing up with that email or number still gives the role.
    /// </summary>
    private async Task<bool> SendAsync(TournamentInvite invite)
    {
        var tournamentId = _context.TournamentId;
        var tournament = await _tournaments.QueryFirstOrDefaultAsync(q => q
            .Where(t => t.Id == tournamentId)
            .Select(t => new { t.Name, t.Sport }), tracking: false);
        try
        {
            var template = await _templates.GetAsync(NotificationTemplateCode.TournamentInvite);
            await _messages.SendAsync(InviteMessages.Build(template, invite, _currentUser.Name, tournament!.Name, tournament.Sport));
            return true;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Couldn't send a tournament invite.");
            return false;
        }
    }

    private static string WaitMessage(TournamentInvite invite, DateTime now)
    {
        var wait = (int)Math.Ceiling((ResendAfter - (now - invite.LastSentAt)).TotalSeconds);
        return $"An invite was just sent to {Shown(invite.Target)}. You can send it again in {wait} seconds.";
    }

    private static string Shown(string target) => Contacts.Display(target);

    private static string RolePhrase(TournamentMemberRole role) => role == TournamentMemberRole.Owner ? "an owner" : "a scorer";

    /// <summary>Whoever goes stops holding any of this tournament's matches.</summary>
    private async Task ReleaseScoring(int tournamentId, int userId)
    {
        IEnumerable<IScoredMatch> held =
            (await _matches.ListAsync(m => m.TournamentId == tournamentId && m.ActiveScorerUserId == userId)).Cast<IScoredMatch>()
            .Concat(await _cricketMatches.ListAsync(m => m.TournamentId == tournamentId && m.ActiveScorerUserId == userId));
        foreach (var match in held)
        {
            if (match.IsOver) continue;
            match.ActiveScorerUserId = null;
            match.ScorerLastSeenAt = null;
        }
    }
}

/// <summary>The words of an invite, by email or text, from the <c>TournamentInvite</c> template.</summary>
public static class InviteMessages
{
    public static OutgoingMessage Build(NotificationTemplate template, TournamentInvite invite, string inviter, string tournament, Sport sport)
    {
        var owner = invite.MemberRole == TournamentMemberRole.Owner;
        var values = Notifier.With(
            ("InviterName", string.IsNullOrWhiteSpace(inviter) ? "Someone" : inviter),
            ("TournamentName", tournament),
            ("Sport", sport.ToString().ToLowerInvariant()),
            ("Role", owner ? "an owner" : "a scorer"),
            ("RoleAction", owner ? "help run" : "score"),
            ("Contact", invite.Target),
            ("ExpiresOn", invite.ExpiresAt.ToString("d MMM yyyy", CultureInfo.InvariantCulture)));

        if (invite.Channel == OtpChannel.Sms)
            return new OutgoingMessage(OtpChannel.Sms, invite.Target, "", TemplateText.Sms(template, values), "Invite");

        var mail = TemplateText.Email(template, values);
        return new OutgoingMessage(OtpChannel.Email, invite.Target, mail.Title, mail.Body, "Invite");
    }
}
