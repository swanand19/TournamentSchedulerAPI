using TournamentScheduler.Api.Auth;
using TournamentScheduler.Api.Data;
using TournamentScheduler.Api.Data.DataServices;
using TournamentScheduler.Api.Models;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Models.Cricket;
using TournamentScheduler.Api.Services.Accounts;

namespace TournamentScheduler.Api.Services.Access;

public interface ITournamentMemberService
{
    Task<ServiceResult<List<TournamentMemberDto>>> ListAsync();
    Task<ServiceResult<List<TournamentMemberDto>>> AddAsync(AddTournamentMemberRequest request);
    Task<ServiceResult<List<TournamentMemberDto>>> RemoveAsync(int userId);
    Task<ServiceResult<bool>> LeaveAsync();
}

/// <summary>
/// A tournament's owners and scorers. Owners add people by the email they signed up with, and
/// remove them; anyone but the creator can leave (a player leaving drops their team link). The
/// creator is always an owner and can't be removed — so a tournament can never be left without
/// one. Someone who goes stops holding any match's scoring.
/// </summary>
public class TournamentMemberService : ServiceBase, ITournamentMemberService
{
    private readonly IDataService<TournamentMember> _members;
    private readonly IDataService<User> _users;
    private readonly IDataService<Tournament> _tournaments;
    private readonly IDataService<Match> _matches;
    private readonly IDataService<CricketMatch> _cricketMatches;
    private readonly IDataService<Player> _players;
    private readonly IPlayerLinkService _links;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITournamentContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;

    public TournamentMemberService(
        IDataService<TournamentMember> members,
        IDataService<User> users,
        IDataService<Tournament> tournaments,
        IDataService<Match> matches,
        IDataService<CricketMatch> cricketMatches,
        IDataService<Player> players,
        IPlayerLinkService links,
        IUnitOfWork unitOfWork,
        ITournamentContext context,
        ICurrentUser currentUser,
        TimeProvider clock)
    {
        _members = members;
        _users = users;
        _tournaments = tournaments;
        _matches = matches;
        _cricketMatches = cricketMatches;
        _players = players;
        _links = links;
        _unitOfWork = unitOfWork;
        _context = context;
        _currentUser = currentUser;
        _clock = clock;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public async Task<ServiceResult<List<TournamentMemberDto>>> ListAsync() => Success(await Members());

    private async Task<List<TournamentMemberDto>> Members()
    {
        var tournamentId = _context.TournamentId;
        var creatorId = await _tournaments.QueryFirstOrDefaultAsync(q => q.Where(t => t.Id == tournamentId).Select(t => t.CreatedByUserId), tracking: false);
        var rows = await _members.QueryAsync(q => q
            .Where(m => m.TournamentId == tournamentId && !m.IsDisabled)
            .Select(m => new { m.UserId, m.MemberRole, m.User!.Name, m.User.EmailId }), tracking: false);

        var me = _currentUser.UserId;
        var seeEmails = _context.IsOwner;
        return rows
            .Select(r => new TournamentMemberDto
            {
                UserId = r.UserId,
                Name = r.Name,
                Email = seeEmails ? r.EmailId : null,
                Role = r.UserId == creatorId ? "Creator" : r.MemberRole.ToString(),
                IsCreator = r.UserId == creatorId,
                IsMe = r.UserId == me,
                CanRemove = _context.IsOwner && r.UserId != creatorId && r.UserId != me
            })
            .OrderBy(m => m.IsCreator ? 0 : m.Role == nameof(TournamentMemberRole.Owner) ? 1 : 2)
            .ThenBy(m => m.Name)
            .ToList();
    }

    public async Task<ServiceResult<List<TournamentMemberDto>>> AddAsync(AddTournamentMemberRequest request)
    {
        var email = AccountService.NormaliseEmail(request.Email);
        if (email == null) return BadRequest("Enter a valid email address.");
        if (!Enum.IsDefined(request.Role)) return BadRequest("Choose Owner or Scorer.");

        var user = await _users.QueryFirstOrDefaultAsync(q => q
            .Where(u => u.EmailId == email && u.DeletedOn == null)
            .Select(u => new { u.UserId, u.Name, u.IsDisabled }), tracking: false);
        if (user == null || user.IsDisabled)
            return NotFound($"There's no account for {email}. Ask them to sign up in the app first, then add them.");

        var tournamentId = _context.TournamentId;
        var creatorId = await _tournaments.QueryFirstOrDefaultAsync(q => q.Where(t => t.Id == tournamentId).Select(t => t.CreatedByUserId), tracking: false);
        if (user.UserId == creatorId) return Conflict($"{user.Name} created this tournament, so they're already an owner.");

        var now = Now;
        var existing = await _members.FirstOrDefaultAsync(m => m.TournamentId == tournamentId && m.UserId == user.UserId);
        if (existing is { IsDisabled: false } && existing.MemberRole == request.Role)
            return Conflict($"{user.Name} is already {(request.Role == TournamentMemberRole.Owner ? "an owner" : "a scorer")}.");

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

        try
        {
            await _unitOfWork.SaveChangesAsync();
        }
        catch (DuplicateEntryException)
        {
            return Conflict($"{user.Name} was added a moment ago. Reload the list.");
        }
        return Success(await Members());
    }

    public async Task<ServiceResult<List<TournamentMemberDto>>> RemoveAsync(int userId)
    {
        var tournamentId = _context.TournamentId;
        if (userId == _currentUser.UserId) return BadRequest("To leave the tournament yourself, use Leave.");

        var creatorId = await _tournaments.QueryFirstOrDefaultAsync(q => q.Where(t => t.Id == tournamentId).Select(t => t.CreatedByUserId), tracking: false);
        if (userId == creatorId) return Forbidden("The person who created the tournament can't be removed.");

        var member = await _members.FirstOrDefaultAsync(m => m.TournamentId == tournamentId && m.UserId == userId && !m.IsDisabled);
        if (member == null) return NotFound("That person isn't part of this tournament.");

        _members.Remove(member);
        await ReleaseScoring(tournamentId, userId);
        await _unitOfWork.SaveChangesAsync();
        return Success(await Members());
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
