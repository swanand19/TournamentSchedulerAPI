using TournamentScheduler.Api.Auth;
using TournamentScheduler.Api.Data;
using TournamentScheduler.Api.Data.DataServices;
using TournamentScheduler.Api.Models;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Models.Cricket;

namespace TournamentScheduler.Api.Services.Access;

/// <summary>How an action's tournament is found: from which route value or body.</summary>
public enum TournamentScope
{
    /// <summary>The route's <c>id</c> is the tournament (tournaments/{id}/…, its teams and players).</summary>
    Tournament,

    /// <summary>The route's <c>matchId</c> is a football match.</summary>
    FootballMatch,

    /// <summary>The route's <c>matchId</c> is a cricket match.</summary>
    CricketMatch,

    /// <summary>The route's <c>sport</c> ("football" / "cricket") says which kind <c>matchId</c> is.</summary>
    SportMatch,

    /// <summary>The route's <c>scheduleId</c> is a saved schedule.</summary>
    Schedule,

    /// <summary>The request body names it (<see cref="ITournamentScopedRequest"/>).</summary>
    Body
}

/// <summary>What the check found out about this request, for the services that act on it.</summary>
public interface ITournamentContext
{
    bool IsResolved { get; }
    int TournamentId { get; }
    TournamentStatus Status { get; }

    /// <summary>The caller's role here; the creator always counts as an owner. Null for someone who only plays in it.</summary>
    TournamentMemberRole? Role { get; }

    bool IsCreator { get; }
    bool IsOwner { get; }

    /// <summary>The caller's own squad place here, if they play in it.</summary>
    PlayerPlace? MyPlace { get; }

    /// <summary>The match the action is about, when it is about one (tracked, so the lock can be updated).</summary>
    IScoredMatch? Match { get; }
}

public sealed class TournamentContext : ITournamentContext
{
    public bool IsResolved { get; private set; }
    public int TournamentId { get; private set; }
    public TournamentStatus Status { get; private set; }
    public TournamentMemberRole? Role { get; private set; }
    public bool IsCreator { get; private set; }
    public bool IsOwner => IsCreator || Role == TournamentMemberRole.Owner;
    public PlayerPlace? MyPlace { get; private set; }
    public IScoredMatch? Match { get; private set; }

    public void Set(int tournamentId, TournamentStatus status, TournamentMemberRole? role, bool isCreator, PlayerPlace? myPlace, IScoredMatch? match)
    {
        IsResolved = true;
        MyPlace = myPlace;
        TournamentId = tournamentId;
        Status = status;
        Role = isCreator ? TournamentMemberRole.Owner : role;
        IsCreator = isCreator;
        Match = match;
    }
}

/// <summary>A squad place: the player row and its team.</summary>
public sealed record PlayerPlace(int PlayerId, int TeamId);

/// <summary>The outcome of a check: null <see cref="Message"/> means allowed.</summary>
public sealed record AccessOutcome(int StatusCode, string? Message)
{
    public static readonly AccessOutcome Allowed = new(200, null);
    public bool IsAllowed => Message == null;
}

public interface ITournamentAccessService
{
    /// <summary>
    /// May the signed-in user do <paramref name="permission"/> on the tournament <paramref name="scope"/>
    /// and <paramref name="id"/> point at? Fills <see cref="ITournamentContext"/> on the way.
    /// </summary>
    Task<AccessOutcome> CheckAsync(TournamentScope scope, int id, string? sport, TournamentPermission permission, bool ignoresScorerLock);

    /// <summary>After an action succeeded: the first person to score a match takes its lock, and the holder's activity keeps it fresh.</summary>
    Task AfterSuccessAsync(TournamentPermission permission);

    /// <summary>The <c>scoring</c> block for the match in context, as the caller sees it.</summary>
    Task<ScoringDto?> ScoringForContextMatchAsync();
}

/// <summary>
/// The tournament-level rules from docs/accounts-and-access-plan.md §5, §7 and §8, applied before
/// every action that touches a tournament (by <c>TournamentAccessFilter</c>, from the action's
/// <c>[TournamentAccess]</c>):
/// <list type="number">
/// <item>Find the tournament (from a match, team, schedule or the body). Missing → 404.</item>
/// <item>Not a member and not playing in it → 404 as well: a stranger can't even learn a private tournament exists.</item>
/// <item>The role decides: View any member or player, Score owners and scorers, Edit and
/// ManageMembers owners, Delete only the creator.</item>
/// <item>A completed or cancelled tournament can't be changed; a match already in play can still be finished.</item>
/// <item>The scorer lock: while someone else is scoring a match, nobody else may score it.</item>
/// </list>
/// </summary>
public class TournamentAccessService : ServiceBase, ITournamentAccessService
{
    /// <summary>The active scorer's activity is recorded at most this often.</summary>
    public static readonly TimeSpan TouchEvery = TimeSpan.FromSeconds(30);

    /// <summary>A request to take over waits this long for an answer.</summary>
    public static readonly TimeSpan RequestLifetime = TimeSpan.FromMinutes(2);

    /// <summary>An active scorer not seen for this long loses the lock to the next request at once.</summary>
    public static readonly TimeSpan ScorerAwayAfter = TimeSpan.FromMinutes(5);

    private readonly IDataService<Tournament> _tournaments;
    private readonly IDataService<TournamentMember> _members;
    private readonly IDataService<Match> _matches;
    private readonly IDataService<CricketMatch> _cricketMatches;
    private readonly IDataService<SavedSchedule> _schedules;
    private readonly IDataService<Player> _players;
    private readonly IDataService<User> _users;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly TournamentContext _context;
    private readonly TimeProvider _clock;

    public TournamentAccessService(
        IDataService<Tournament> tournaments,
        IDataService<TournamentMember> members,
        IDataService<Match> matches,
        IDataService<CricketMatch> cricketMatches,
        IDataService<SavedSchedule> schedules,
        IDataService<Player> players,
        IDataService<User> users,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        TournamentContext context,
        TimeProvider clock)
    {
        _tournaments = tournaments;
        _members = members;
        _matches = matches;
        _cricketMatches = cricketMatches;
        _schedules = schedules;
        _players = players;
        _users = users;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _context = context;
        _clock = clock;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public async Task<AccessOutcome> CheckAsync(TournamentScope scope, int id, string? sport, TournamentPermission permission, bool ignoresScorerLock)
    {
        // 1. Which tournament (and match).
        IScoredMatch? match = null;
        int? tournamentId;
        var notFound = "Tournament not found.";

        switch (scope)
        {
            case TournamentScope.FootballMatch:
            case TournamentScope.CricketMatch:
            case TournamentScope.SportMatch:
                var cricket = scope == TournamentScope.CricketMatch
                              || scope == TournamentScope.SportMatch && string.Equals(sport, "cricket", StringComparison.OrdinalIgnoreCase);
                if (scope == TournamentScope.SportMatch && !cricket && !string.Equals(sport, "football", StringComparison.OrdinalIgnoreCase))
                    return new AccessOutcome(404, "Match not found.");
                match = cricket ? await _cricketMatches.GetByIdAsync(id) : await _matches.GetByIdAsync(id);
                tournamentId = match?.TournamentId;
                notFound = "Match not found.";
                break;
            case TournamentScope.Schedule:
                tournamentId = await _schedules.QueryFirstOrDefaultAsync(q => q.Where(s => s.Id == id).Select(s => (int?)s.TournamentId), tracking: false);
                notFound = "Schedule not found.";
                break;
            default:
                tournamentId = id;
                break;
        }
        if (tournamentId == null) return new AccessOutcome(404, notFound);

        var tournament = await _tournaments.QueryFirstOrDefaultAsync(q => q
            .Where(t => t.Id == tournamentId)
            .Select(t => new { t.Status, t.CreatedByUserId }), tracking: false);
        if (tournament == null) return new AccessOutcome(404, notFound);

        // 2. Who the caller is here. Strangers hear "not found", never "forbidden".
        var userId = _currentUser.UserId;
        var role = await _members.QueryFirstOrDefaultAsync(q => q
            .Where(m => m.TournamentId == tournamentId && m.UserId == userId && !m.IsDisabled)
            .Select(m => (TournamentMemberRole?)m.MemberRole), tracking: false);
        var isCreator = tournament.CreatedByUserId == userId;
        var place = await _players.QueryFirstOrDefaultAsync(q => q
            .Where(p => p.TournamentId == tournamentId && p.UserId == userId)
            .Select(p => new PlayerPlace(p.Id, p.TeamId)), tracking: false);
        if (role == null && !isCreator && place == null) return new AccessOutcome(404, notFound);

        _context.Set(tournamentId.Value, tournament.Status, role, isCreator, place, match);

        // 3. The role. Someone who only plays in it may look, nothing more.
        var allowed = permission switch
        {
            TournamentPermission.View => true,
            TournamentPermission.Score => _context.Role != null,
            TournamentPermission.Edit or TournamentPermission.ManageMembers => _context.IsOwner,
            TournamentPermission.Delete => isCreator,
            _ => false
        };
        if (!allowed)
        {
            return new AccessOutcome(403, permission switch
            {
                TournamentPermission.Delete => "Only the person who created this tournament can delete it.",
                TournamentPermission.Score => "Only the tournament's owners and scorers can do that.",
                _ => "Only the tournament's owners can do that."
            });
        }

        // 4. Completed or cancelled: frozen, except to finish a match already in play.
        var closed = tournament.Status is TournamentStatus.Completed or TournamentStatus.Cancelled;
        var state = tournament.Status == TournamentStatus.Cancelled ? "cancelled" : "completed";
        if (closed && permission == TournamentPermission.Edit)
            return new AccessOutcome(409, $"This tournament is {state}, so it can't be changed.");
        if (closed && permission == TournamentPermission.Score && match is not { IsInPlay: true })
            return new AccessOutcome(409, $"This tournament is {state}, so no more matches can be played.");

        // 5. The scorer lock.
        if (permission == TournamentPermission.Score && !ignoresScorerLock && match is { IsOver: false, ActiveScorerUserId: { } scorer } && scorer != userId)
            return new AccessOutcome(409, $"{await NameOf(scorer)} is scoring this match.");

        return AccessOutcome.Allowed;
    }

    public async Task AfterSuccessAsync(TournamentPermission permission)
    {
        if (_context.Match is not { IsOver: false } match) return;

        var me = _currentUser.UserId;
        var now = Now;
        var changed = false;

        if (permission == TournamentPermission.Score && match.ActiveScorerUserId == null)
        {
            // Whoever first scores a match (setting it up) holds it.
            match.ActiveScorerUserId = me;
            match.ScorerLastSeenAt = now;
            changed = true;
        }
        else if (match.ActiveScorerUserId == me && (match.ScorerLastSeenAt == null || now - match.ScorerLastSeenAt >= TouchEvery))
        {
            // The holder is still here (scoring, or their screen refreshing).
            match.ScorerLastSeenAt = now;
            changed = true;
        }

        if (changed) await _unitOfWork.SaveChangesAsync();
    }

    public async Task<ScoringDto?> ScoringForContextMatchAsync() =>
        _context.Match is { } match ? await BuildScoringAsync(match) : null;

    /// <summary>The scoring block for <paramref name="match"/> as the signed-in member sees it.</summary>
    public async Task<ScoringDto> BuildScoringAsync(IScoredMatch match)
    {
        var me = _currentUser.UserId;
        var now = Now;
        var active = match.IsOver ? null : match.ActiveScorerUserId;
        var pending = !match.IsOver && match.ScoringRequestedByUserId is { } by && match.ScoringRequestedAt is { } at && now - at < RequestLifetime
            ? (By: by, At: at)
            : ((int By, DateTime At)?)null;

        var names = await NamesOf(new[] { active, pending?.By }.OfType<int>().Distinct().ToList());
        var isActive = active == me;
        var isMember = _context.Role != null;
        var open = !(_context.Status is TournamentStatus.Completed or TournamentStatus.Cancelled) || match.IsInPlay;

        return new ScoringDto
        {
            ActiveScorer = active is { } a ? new PersonDto { UserId = a, Name = names.GetValueOrDefault(a, "Someone") } : null,
            IsActiveScorer = isActive,
            CanScore = isMember && open && !match.IsOver && (active == null || isActive),
            CanRequestScoring = _context.Role == TournamentMemberRole.Scorer && !_context.IsOwner && active != null && !isActive
                                && pending?.By != me && !match.IsOver && open,
            CanTakeOverScoring = _context.IsOwner && active != null && !isActive && !match.IsOver && open,
            PendingRequest = pending is { } p
                ? new ScoringRequestDto { By = new PersonDto { UserId = p.By, Name = names.GetValueOrDefault(p.By, "Someone") }, RequestedAt = p.At, ExpiresAt = p.At + RequestLifetime }
                : null,
            CanRespondToRequest = isActive && pending != null
        };
    }

    private async Task<string> NameOf(int userId) => (await NamesOf([userId])).GetValueOrDefault(userId, "Someone else");

    private async Task<Dictionary<int, string>> NamesOf(List<int> userIds)
    {
        if (userIds.Count == 0) return new Dictionary<int, string>();
        var rows = await _users.QueryAsync(q => q.Where(u => userIds.Contains(u.UserId)).Select(u => new { u.UserId, u.Name }), tracking: false);
        return rows.ToDictionary(r => r.UserId, r => r.Name);
    }
}
