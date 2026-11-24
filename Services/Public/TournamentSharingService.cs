using TournamentScheduler.Api.Auth;
using TournamentScheduler.Api.Data;
using TournamentScheduler.Api.Data.DataServices;
using TournamentScheduler.Api.Models;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Models.Public;

namespace TournamentScheduler.Api.Services.Public;

public interface ITournamentSharingService
{
    Task<ServiceResult<List<PublicSearchResultDto>>> SearchAsync(string? query);
    Task<ServiceResult<FollowedDto>> FollowAsync(string? code);
    Task<ServiceResult<object>> UnfollowAsync(int tournamentId);
}

/// <summary>
/// Finding and following tournaments (6E). Every tournament is public since 2026-11-23 — no owners' switch any more,
/// only an admin's takedown (<c>AdminTournamentService.SetHiddenAsync</c>), which shuts the link and followers out at
/// once (the rule is in <c>TournamentAccessService</c>). One link per tournament, made with it and kept for good.
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
    private readonly IDataService<Models.Knockouts.KnockoutFixture> _knockouts;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;

    public TournamentSharingService(IDataService<Tournament> tournaments, IDataService<TournamentFollow> follows,
        IDataService<TournamentMember> members, IDataService<Player> players, IDataService<Models.Knockouts.KnockoutFixture> knockouts, IUnitOfWork unitOfWork, ICurrentUser currentUser, TimeProvider clock)
    {
        _knockouts = knockouts;
        _tournaments = tournaments;
        _follows = follows;
        _members = members;
        _players = players;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
    }

    /// <summary>
    /// Tournaments by name (3 letters or more, anywhere in the name) or by their code, or the whole link
    /// pasted. By name only once it is set — its schedule approved, a knockout stage set up, or started — so
    /// half-made and test tournaments stay out of the list (the user's choice, 2026-11-23); by code always,
    /// as its link always works. Never one an admin has hidden. Live first, then upcoming, then finished; the newest
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
        // Tournaments with a knockout stage among the names that match (Tournament has no list of its knockout fixtures).
        var withKnockout = await _knockouts.QueryAsync(q => q
            .Where(k => k.Tournament!.Name.ToLower().Contains(lower))
            .Select(k => k.TournamentId).Distinct(), tracking: false);
        var found = await _tournaments.QueryAsync(q => q
            .Where(t => t.IsPublic && t.PublicCode != null && (t.PublicCode == code
                || (t.Name.ToLower().Contains(lower) && (t.IsStarted || t.Schedules.Any() || withKnockout.Contains(t.Id)))))
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
}
