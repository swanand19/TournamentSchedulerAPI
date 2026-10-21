using TournamentScheduler.Api.Auth;
using TournamentScheduler.Api.Data;
using TournamentScheduler.Api.Data.DataServices;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Models.Messaging;
using TournamentScheduler.Api.Services.Notifications;

namespace TournamentScheduler.Api.Services.Access;

public interface IScoringLockService
{
    Task<ServiceResult<ScoringDto>> RequestAsync();
    Task<ServiceResult<ScoringDto>> RespondAsync(RespondToScoringRequest request);
    Task<ServiceResult<ScoringDto>> TakeOverAsync();
}

/// <summary>
/// Handing a match's scoring from one person to another (both sports). The match comes from
/// <see cref="ITournamentContext"/>, which the access check has filled.
/// <list type="bullet">
/// <item><b>Request</b> — nobody scoring: it's yours. The scorer's app not seen for 5 minutes: it's
/// yours at once. Otherwise the scorer is asked, and has 2 minutes to answer.</item>
/// <item><b>Respond</b> — the scorer allows (the requester takes over) or declines.</item>
/// <item><b>Take over</b> — owners only, no asking.</item>
/// </list>
/// </summary>
public class ScoringLockService : ServiceBase, IScoringLockService
{
    private readonly ITournamentContext _context;
    private readonly TournamentAccessService _access;
    private readonly IDataService<User> _users;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;
    private readonly INotifier _notifier;

    public ScoringLockService(ITournamentContext context, TournamentAccessService access, IDataService<User> users,
        IUnitOfWork unitOfWork, ICurrentUser currentUser, TimeProvider clock, INotifier notifier)
    {
        _context = context;
        _access = access;
        _users = users;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
        _notifier = notifier;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    private bool HasPendingRequest(Models.IScoredMatch match) =>
        match.ScoringRequestedByUserId != null && match.ScoringRequestedAt is { } at && Now - at < TournamentAccessService.RequestLifetime;

    private static void Hand(Models.IScoredMatch match, int userId, DateTime now)
    {
        match.ActiveScorerUserId = userId;
        match.ScorerLastSeenAt = now;
        match.ScoringRequestedByUserId = null;
        match.ScoringRequestedAt = null;
    }

    private async Task<ServiceResult<ScoringDto>> Saved(Models.IScoredMatch match)
    {
        await _unitOfWork.SaveChangesAsync();
        return Success(await _access.BuildScoringAsync(match));
    }

    public async Task<ServiceResult<ScoringDto>> RequestAsync()
    {
        if (_context.Match is not { } match) return NotFound("Match not found.");
        if (match.IsOver) return Conflict("This match is over.");

        var me = _currentUser.UserId;
        var now = Now;

        if (match.ActiveScorerUserId == null || match.ActiveScorerUserId == me)
        {
            Hand(match, me, now);
            return await Saved(match);
        }

        if (HasPendingRequest(match) && match.ScoringRequestedByUserId != me)
        {
            var by = await _users.QueryFirstOrDefaultAsync(q => q.Where(u => u.UserId == match.ScoringRequestedByUserId).Select(u => u.Name), tracking: false);
            return Conflict($"{by ?? "Someone"} has already asked to take over. Try again in a couple of minutes.");
        }

        // The scorer's phone went quiet (battery, pocket, gone home): no point waiting for an answer.
        if (match.ScorerLastSeenAt == null || now - match.ScorerLastSeenAt >= TournamentAccessService.ScorerAwayAfter)
        {
            await Tell(match.ActiveScorerUserId.Value, match, NotificationTemplateCode.ScoringPassedOn);
            Hand(match, me, now);
            return await Saved(match);
        }

        match.ScoringRequestedByUserId = me;
        match.ScoringRequestedAt = now;
        await Tell(match.ActiveScorerUserId.Value, match, NotificationTemplateCode.ScoringRequested);
        return await Saved(match);
    }

    public async Task<ServiceResult<ScoringDto>> RespondAsync(RespondToScoringRequest request)
    {
        if (_context.Match is not { } match) return NotFound("Match not found.");
        if (match.IsOver) return Conflict("This match is over.");
        if (match.ActiveScorerUserId != _currentUser.UserId) return Forbidden("Only the person scoring this match can answer.");
        if (!HasPendingRequest(match)) return Conflict("That request has expired.");

        var requester = match.ScoringRequestedByUserId!.Value;
        if (request.Allow)
        {
            Hand(match, requester, Now);
            await Tell(requester, match, NotificationTemplateCode.ScoringAllowed);
        }
        else
        {
            match.ScoringRequestedByUserId = null;
            match.ScoringRequestedAt = null;
            await Tell(requester, match, NotificationTemplateCode.ScoringDeclined);
        }
        return await Saved(match);
    }

    public async Task<ServiceResult<ScoringDto>> TakeOverAsync()
    {
        if (_context.Match is not { } match) return NotFound("Match not found.");
        if (!_context.IsOwner) return Forbidden("Only the tournament's owners can take over scoring. Ask to take over instead.");
        if (match.IsOver) return Conflict("This match is over.");

        if (match.ActiveScorerUserId is { } previous && previous != _currentUser.UserId)
            await Tell(previous, match, NotificationTemplateCode.ScoringTakenOver);
        Hand(match, _currentUser.UserId, Now);
        return await Saved(match);
    }

    private string Me => string.IsNullOrWhiteSpace(_currentUser.Name) ? "Someone" : _currentUser.Name;

    /// <summary>A handover notification, saved with the handover itself.</summary>
    private async Task Tell(int userId, Models.IScoredMatch match, NotificationTemplateCode code) =>
        await _notifier.NotifyAsync(userId, code, Notifier.With(
            ("ActorName", Me), ("Match", Notifier.Describe(match)), ("TournamentName", await _notifier.TournamentNameAsync(match.TournamentId))),
            NotificationLink.To(match));
}
