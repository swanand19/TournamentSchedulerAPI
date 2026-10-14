using TournamentScheduler.Api.Auth;
using TournamentScheduler.Api.Data;
using TournamentScheduler.Api.Data.DataServices;
using TournamentScheduler.Api.Models;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Models.Cricket;
using TournamentScheduler.Api.Services.Access;

namespace TournamentScheduler.Api.Services.Accounts;

public interface IAccountDeletionService
{
    Task<ServiceResult<AccountDeletionPreviewDto>> PreviewAsync(ClientInfo client);
    Task<ServiceResult<object>> DeleteAsync(DeleteAccountRequest request);
}

/// <summary>
/// Deleting your own account (docs/accounts-and-access-plan.md §9). The preview says what will
/// happen and sends a code (by email, or by text to an account with only a number); the code
/// confirms it. The account row stays, anonymised, so every foreign key stays valid:
/// <list type="bullet">
/// <item>tournaments you're the only owner of are deleted (for everyone in them);</item>
/// <item>ones you created that have other owners pass to the longest-standing of them;</item>
/// <item>you're taken off every other tournament, and stop holding any match's scoring;</item>
/// <item>your squad places are unlinked — the names stay on the scorecards;</item>
/// <item>sessions, codes and your cricket profile are removed; the email and number are freed, so signing up
/// again with it makes a new, empty account.</item>
/// </list>
/// </summary>
public class AccountDeletionService : ServiceBase, IAccountDeletionService
{
    public const string DeletedName = "Deleted user";

    private readonly IDataService<User> _users;
    private readonly IDataService<UserSession> _sessions;
    private readonly IDataService<OtpChallenge> _challenges;
    private readonly IDataService<UserCricketProfile> _profiles;
    private readonly IDataService<Tournament> _tournaments;
    private readonly IDataService<TournamentMember> _members;
    private readonly IDataService<Player> _players;
    private readonly IDataService<Match> _matches;
    private readonly IDataService<CricketMatch> _cricketMatches;
    private readonly IOtpCodeService _codes;
    private readonly IPlayerLinkService _links;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;

    public AccountDeletionService(
        IDataService<User> users,
        IDataService<UserSession> sessions,
        IDataService<OtpChallenge> challenges,
        IDataService<UserCricketProfile> profiles,
        IDataService<Tournament> tournaments,
        IDataService<TournamentMember> members,
        IDataService<Player> players,
        IDataService<Match> matches,
        IDataService<CricketMatch> cricketMatches,
        IOtpCodeService codes,
        IPlayerLinkService links,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        TimeProvider clock)
    {
        _users = users;
        _sessions = sessions;
        _challenges = challenges;
        _profiles = profiles;
        _tournaments = tournaments;
        _members = members;
        _players = players;
        _matches = matches;
        _cricketMatches = cricketMatches;
        _codes = codes;
        _links = links;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
    }

    private sealed record ToDelete(int Id, string Name, Sport Sport, int OtherPeople);
    private sealed record ToHandOver(int Id, string Name, int NewCreatorId, string NewCreatorName);
    private sealed record Plan(List<ToDelete> Delete, List<ToHandOver> HandOver, int Leave, int Places);

    public async Task<ServiceResult<AccountDeletionPreviewDto>> PreviewAsync(ClientInfo client)
    {
        var user = await _users.GetByIdAsync(_currentUser.UserId);
        if (user == null || CodeGoesTo(user) is not { } to) return NotFound("Account not found.");
        if (user.RoleId == RoleIds.Admin) return Forbidden("Admin accounts can't be deleted from the app.");

        var plan = await MakePlan(user.UserId);

        var sent = await _codes.SendAsync(to, OtpPurpose.DeleteAccount, client);
        if (!sent.IsOk) return new ServiceError(sent.Outcome, sent.Error);

        return Success(new AccountDeletionPreviewDto
        {
            CodeSentTo = to.Value,
            ExpiresInSeconds = sent.Value!.ExpiresInSeconds,
            ResendAfterSeconds = sent.Value.ResendAfterSeconds,
            TournamentsDeleted = plan.Delete.Select(t => new DeletedTournamentDto { Id = t.Id, Name = t.Name, Sport = t.Sport, OtherPeople = t.OtherPeople }).ToList(),
            TournamentsHandedOver = plan.HandOver.Select(t => new HandedOverTournamentDto { Id = t.Id, Name = t.Name, NewCreator = t.NewCreatorName }).ToList(),
            TournamentsLeft = plan.Leave,
            SquadPlaces = plan.Places
        });
    }

    public async Task<ServiceResult<object>> DeleteAsync(DeleteAccountRequest request)
    {
        var user = await _users.GetByIdAsync(_currentUser.UserId);
        if (user == null || CodeGoesTo(user) is not { } to) return NotFound("Account not found.");
        if (user.RoleId == RoleIds.Admin) return Forbidden("Admin accounts can't be deleted from the app.");

        var email = user.EmailId;
        var phone = user.PhoneNumber;
        var (_, wrong) = await _codes.CheckAsync(to, OtpPurpose.DeleteAccount, request.Otp);
        if (wrong is { } error) return error;

        var me = user.UserId;
        var now = _clock.GetUtcNow().UtcDateTime;
        var plan = await MakePlan(me);
        var deleted = plan.Delete.Select(t => t.Id).ToList();

        // Tournaments: deleted (cascading to everything in them), or handed to another owner.
        foreach (var gone in await _tournaments.ListAsync(t => deleted.Contains(t.Id)))
            _tournaments.Remove(gone);
        var handOver = plan.HandOver.ToDictionary(h => h.Id, h => h.NewCreatorId);
        var handOverIds = handOver.Keys.ToList();
        foreach (var kept in await _tournaments.ListAsync(t => handOverIds.Contains(t.Id)))
            kept.CreatedByUserId = handOver[kept.Id];

        // Rows inside deleted tournaments go with them; touching them as well would race the cascade.
        _members.RemoveRange(await _members.ListAsync(m => m.UserId == me && !deleted.Contains(m.TournamentId)));

        foreach (var place in await _players.ListAsync(p =>
                     (p.UserId == me || (email != null && p.EmailId == email) || (phone != null && p.PhoneNumber == phone))
                     && !deleted.Contains(p.TournamentId)))
            _links.Unlink(place);

        IEnumerable<IScoredMatch> held =
            (await _matches.ListAsync(m => (m.ActiveScorerUserId == me || m.ScoringRequestedByUserId == me) && !deleted.Contains(m.TournamentId))).Cast<IScoredMatch>()
            .Concat(await _cricketMatches.ListAsync(m => (m.ActiveScorerUserId == me || m.ScoringRequestedByUserId == me) && !deleted.Contains(m.TournamentId)));
        foreach (var match in held)
        {
            if (match.ActiveScorerUserId == me)
            {
                match.ActiveScorerUserId = null;
                match.ScorerLastSeenAt = null;
            }
            if (match.ScoringRequestedByUserId == me)
            {
                match.ScoringRequestedByUserId = null;
                match.ScoringRequestedAt = null;
            }
        }

        // The person: signed out everywhere, nothing left that says who they were.
        _sessions.RemoveRange(await _sessions.ListAsync(s => s.UserId == me));
        _challenges.RemoveRange(await _challenges.ListAsync(c => c.Target == email || c.Target == phone));
        if (await _profiles.GetByIdAsync(me) is { } profile) _profiles.Remove(profile);

        user.Name = DeletedName;
        user.EmailId = null;
        user.PhoneNumber = null;
        user.EmailVerifiedAt = user.PhoneVerifiedAt = null;
        user.DeletedOn = now;
        user.Modified(now, me);

        await _unitOfWork.SaveChangesAsync();
        return Ok(new { deleted = true, tournamentsDeleted = deleted.Count });
    }

    /// <summary>The confirming code goes to the email, or, for an account with only a number, by text.</summary>
    private static Contact? CodeGoesTo(User user) =>
        user.EmailId != null ? new Contact(ContactKind.Email, user.EmailId)
        : user.PhoneNumber != null ? new Contact(ContactKind.Phone, user.PhoneNumber)
        : null;

    /// <summary>The same reckoning for the preview and the deletion, so what was shown is what happens.</summary>
    private async Task<Plan> MakePlan(int me)
    {
        var mine = await _members.QueryAsync(q => q
            .Where(m => m.UserId == me && !m.IsDisabled)
            .Select(m => new { m.TournamentId, m.MemberRole }), tracking: false);
        var created = await _tournaments.QueryAsync(q => q
            .Where(t => t.CreatedByUserId == me)
            .Select(t => t.Id), tracking: false);

        var owned = mine.Where(m => m.MemberRole == TournamentMemberRole.Owner).Select(m => m.TournamentId)
            .Concat(created).Distinct().ToList();

        var tournaments = (await _tournaments.QueryAsync(q => q
            .Where(t => owned.Contains(t.Id))
            .Select(t => new { t.Id, t.Name, t.Sport, t.CreatedByUserId }), tracking: false))
            .ToDictionary(t => t.Id);

        var others = await _members.QueryAsync(q => q
            .Where(m => owned.Contains(m.TournamentId) && m.UserId != me && !m.IsDisabled)
            .OrderBy(m => m.Crd).ThenBy(m => m.TournamentMemberId)
            .Select(m => new { m.TournamentId, m.UserId, m.MemberRole, m.User!.Name }), tracking: false);
        var players = await _players.QueryAsync(q => q
            .Where(p => owned.Contains(p.TournamentId) && p.UserId != null && p.UserId != me)
            .Select(p => new { p.TournamentId, UserId = p.UserId!.Value }), tracking: false);

        var delete = new List<ToDelete>();
        var handOver = new List<ToHandOver>();
        foreach (var id in owned)
        {
            if (!tournaments.TryGetValue(id, out var t)) continue;
            var nextOwner = others.FirstOrDefault(o => o.TournamentId == id && o.MemberRole == TournamentMemberRole.Owner);
            if (nextOwner == null)
            {
                var people = others.Where(o => o.TournamentId == id).Select(o => o.UserId)
                    .Concat(players.Where(p => p.TournamentId == id).Select(p => p.UserId))
                    .Distinct().Count();
                delete.Add(new ToDelete(t.Id, t.Name, t.Sport, people));
            }
            else if (t.CreatedByUserId == me)
            {
                handOver.Add(new ToHandOver(t.Id, t.Name, nextOwner.UserId, nextOwner.Name));
            }
        }

        var deleting = delete.Select(d => d.Id).ToList();
        var handingOver = handOver.Select(h => h.Id).ToList();
        var leave = mine.Count(m => !deleting.Contains(m.TournamentId) && !handingOver.Contains(m.TournamentId));
        var places = await _players.CountAsync(p => p.UserId == me && !deleting.Contains(p.TournamentId));
        return new Plan(delete.OrderBy(d => d.Name).ToList(), handOver.OrderBy(h => h.Name).ToList(), leave, places);
    }
}
