using TournamentScheduler.Api.Auth;
using TournamentScheduler.Api.Data;
using TournamentScheduler.Api.Data.DataServices;
using TournamentScheduler.Api.Models;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Models.Public;

namespace TournamentScheduler.Api.Services.Public;

public interface ITournamentSharingService
{
    Task<ServiceResult<Tournament>> SetPublicAsync(int id, bool isPublic);
    Task<ServiceResult<List<PublicSearchResultDto>>> SearchAsync(string? query);
    Task<ServiceResult<FollowedDto>> FollowAsync(string? code);
    Task<ServiceResult<object>> UnfollowAsync(int tournamentId);
}

/// <summary>
/// The owners' side of public live scores (6E) and following. An owner switches a tournament public or
/// not; while public anyone signed in can find it (by name or code) and follow it, which lets them
/// view it for as long as it stays public. One link per tournament, for good: once anyone can find
/// it, a link is no secret worth replacing (decided 2026-10-21) — switching it off is the way to shut people out (the rule is in <c>TournamentAccessService</c>).
/// </summary>
public class TournamentSharingService : ServiceBase, ITournamentSharingService
{
    public const string NoSuchLink =
        "No public tournament has that link or code. Check it, or search for the tournament by name.";

    public const int MinSearchLength = 3;
    public const int MaxSearchResults = 20;

    private readonly IDataService<Tournament> _tournaments;
    private readonly IDataService<TournamentFollow> _follows;
    private readonly IDataService<TournamentMember> _members;
    private readonly IDataService<Player> _players;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly ITournamentService _tournamentAnswers;
    private readonly TimeProvider _clock;

    public TournamentSharingService(IDataService<Tournament> tournaments, IDataService<TournamentFollow> follows,
        IDataService<TournamentMember> members, IDataService<Player> players, IUnitOfWork unitOfWork, ICurrentUser currentUser, ITournamentService tournamentAnswers, TimeProvider clock)
    {
        _tournaments = tournaments;
        _follows = follows;
        _members = members;
        _players = players;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _tournamentAnswers = tournamentAnswers;
        _clock = clock;
    }

    /// <summary>Switching it on the first time makes its link; off shuts the link and followers out at once (the code is kept for switching back on).</summary>
    public async Task<ServiceResult<Tournament>> SetPublicAsync(int id, bool isPublic)
    {
        var tournament = await _tournaments.GetByIdAsync(id);
        if (tournament == null) return NotFound("Tournament not found.");

        tournament.IsPublic = isPublic;
        if (isPublic && tournament.PublicCode == null) tournament.PublicCode = await UnusedCodeAsync();
        await _unitOfWork.SaveChangesAsync();
        return await _tournamentAnswers.GetByIdAsync(id);
    }

    /// <summary>
    /// Public tournaments by name (3 letters or more, anywhere in the name) or by their code, or the
    /// whole link pasted. Never a private one. Live first, then upcoming, then finished; the newest
    /// first within each. Each says whether the caller already follows it or belongs to it, so the
    /// apps offer Open rather than Follow.
    /// </summary>
    public async Task<ServiceResult<List<PublicSearchResultDto>>> SearchAsync(string? query)
    {
        var text = (query ?? "").Trim();
        var code = PublicCodes.Normalise(text) ?? "-"; // "-" matches no code
        if (code == "-" && text.Length < MinSearchLength)
            return BadRequest($"Type at least {MinSearchLength} letters of the tournament's name, or its code.");

        var lower = text.ToLowerInvariant();
        var found = await _tournaments.QueryAsync(q => q
            .Where(t => t.IsPublic && (t.PublicCode == code || t.Name.ToLower().Contains(lower)))
            .OrderBy(t => t.PublicCode == code ? 0 : 1)
            .ThenBy(t => t.Status == TournamentStatus.Live ? 0 : t.Status == TournamentStatus.Upcoming ? 1 : 2)
            .ThenByDescending(t => t.StartDate)
            .ThenByDescending(t => t.CreatedAt)
            .Take(MaxSearchResults)
            .Select(t => new PublicSearchResultDto
            {
                TournamentId = t.Id,
                Code = t.PublicCode!,
                Name = t.Name,
                Sport = t.Sport,
                Status = t.Status,
                StartDate = t.StartDate,
                EndDate = t.EndDate,
                Teams = t.Teams.Count,
                CreatedByUserId = t.CreatedByUserId
            }), tracking: false);
        if (found.Count == 0) return Success(found);

        var me = _currentUser.UserId;
        var ids = found.Select(f => f.TournamentId).ToList();
        var following = (await _follows.QueryAsync(q => q.Where(f => f.UserId == me && ids.Contains(f.TournamentId)).Select(f => f.TournamentId), tracking: false)).ToHashSet();
        var member = (await _members.QueryAsync(q => q.Where(m => m.UserId == me && !m.IsDisabled && ids.Contains(m.TournamentId)).Select(m => m.TournamentId), tracking: false)).ToHashSet();
        var plays = (await _players.QueryAsync(q => q.Where(p => p.UserId == me && ids.Contains(p.TournamentId)).Select(p => p.TournamentId), tracking: false)).ToHashSet();
        foreach (var f in found)
        {
            f.IsFollowing = following.Contains(f.TournamentId);
            f.IsMine = f.CreatedByUserId == me || member.Contains(f.TournamentId) || plays.Contains(f.TournamentId);
        }
        return Success(found);
    }

    public async Task<ServiceResult<FollowedDto>> FollowAsync(string? code)
    {
        var normalised = PublicCodes.Normalise(code);
        if (normalised == null) return BadRequest("Paste the tournament's link, or type its 8-character code.");

        var tournament = await _tournaments.FirstOrDefaultAsync(t => t.PublicCode == normalised && t.IsPublic, tracking: false);
        if (tournament == null) return NotFound(NoSuchLink);

        var me = _currentUser.UserId;
        if (!await _follows.AnyAsync(f => f.TournamentId == tournament.Id && f.UserId == me))
        {
            _follows.Add(new TournamentFollow { TournamentId = tournament.Id, UserId = me, Crd = _clock.GetUtcNow().UtcDateTime });
            try
            {
                await _unitOfWork.SaveChangesAsync();
            }
            catch (DuplicateEntryException)
            {
                // Followed twice at once (a double tap): already following, which is what was asked.
            }
        }

        return Success(new FollowedDto { TournamentId = tournament.Id, Name = tournament.Name, Sport = tournament.Sport, IsStarted = tournament.IsStarted });
    }

    /// <summary>Your own follow only; not following already is fine.</summary>
    public async Task<ServiceResult<object>> UnfollowAsync(int tournamentId)
    {
        var me = _currentUser.UserId;
        var rows = await _follows.ListAsync(f => f.TournamentId == tournamentId && f.UserId == me);
        foreach (var row in rows) _follows.Remove(row);
        if (rows.Count > 0) await _unitOfWork.SaveChangesAsync();
        return Ok(new { following = false });
    }

    private async Task<string> UnusedCodeAsync()
    {
        while (true)
        {
            var code = PublicCodes.New();
            if (!await _tournaments.AnyAsync(t => t.PublicCode == code)) return code;
        }
    }
}
