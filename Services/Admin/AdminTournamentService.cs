using TournamentScheduler.Api.Data;
using TournamentScheduler.Api.Data.DataServices;
using TournamentScheduler.Api.Models;
using TournamentScheduler.Api.Models.Accounts;

namespace TournamentScheduler.Api.Services.Admin;

public interface IAdminTournamentService
{
    Task<ServiceResult<AdminTournamentPage>> ListAsync(AdminTournamentView view, string? search, int? page, int? pageSize);
    Task<ServiceResult<AdminTournamentDto>> ReopenAsync(int id, ReopenTournamentRequest request);
}

/// <summary>
/// Every tournament, for the portal — paged on the server, in three pages: Live, Upcoming, and
/// Past (completed and cancelled). Admins don't get into tournaments from here; they see who runs
/// each one, and can reopen one closed by mistake (owners can't).
/// </summary>
public class AdminTournamentService : ServiceBase, IAdminTournamentService
{
    private readonly IDataService<Tournament> _tournaments;
    private readonly IDataService<TournamentMember> _members;
    private readonly IDataService<Player> _players;
    private readonly IDataService<User> _users;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;

    public AdminTournamentService(
        IDataService<Tournament> tournaments,
        IDataService<TournamentMember> members,
        IDataService<Player> players,
        IDataService<User> users,
        IUnitOfWork unitOfWork,
        TimeProvider clock)
    {
        _tournaments = tournaments;
        _members = members;
        _players = players;
        _users = users;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<ServiceResult<AdminTournamentPage>> ListAsync(AdminTournamentView view, string? search, int? page, int? pageSize)
    {
        if (!Enum.IsDefined(view)) return BadRequest("Choose Live, Upcoming or Past.");
        var (p, size) = PagedResult<AdminTournamentDto>.Clamp(page, pageSize);
        var term = search?.Trim().ToLowerInvariant();

        IQueryable<Tournament> Searched(IQueryable<Tournament> q) =>
            string.IsNullOrEmpty(term) ? q : q.Where(t => t.Name.ToLower().Contains(term));

        var statuses = (await _tournaments.QueryAsync(q => Searched(q).Select(t => t.Status), tracking: false));
        var counts = new Dictionary<AdminTournamentView, int>
        {
            [AdminTournamentView.Live] = statuses.Count(s => s == TournamentStatus.Live),
            [AdminTournamentView.Upcoming] = statuses.Count(s => s == TournamentStatus.Upcoming),
            [AdminTournamentView.Past] = statuses.Count(s => s is TournamentStatus.Completed or TournamentStatus.Cancelled)
        };

        // Live: latest started first. Upcoming: soonest first, undated last. Past: latest closed first.
        var ids = await _tournaments.QueryAsync(q =>
        {
            q = Searched(q);
            q = view switch
            {
                AdminTournamentView.Live => q.Where(t => t.Status == TournamentStatus.Live)
                    .OrderByDescending(t => t.StartedAt).ThenByDescending(t => t.Id),
                AdminTournamentView.Upcoming => q.Where(t => t.Status == TournamentStatus.Upcoming)
                    .OrderBy(t => t.StartDate == null).ThenBy(t => t.StartDate).ThenByDescending(t => t.Id),
                _ => q.Where(t => t.Status == TournamentStatus.Completed || t.Status == TournamentStatus.Cancelled)
                    .OrderByDescending(t => t.CompletedAt ?? t.CreatedAt).ThenByDescending(t => t.Id)
            };
            return q.Skip((p - 1) * size).Take(size).Select(t => t.Id);
        }, tracking: false);

        return Success(new AdminTournamentPage
        {
            Items = await Rows(ids),
            Total = counts[view],
            Page = p,
            PageSize = size,
            Counts = counts
        });
    }

    /// <summary>
    /// Completed → Live again; cancelled (never started) → Upcoming. If its end date has passed, the
    /// hourly job would close it again straight away, so it needs a new end date.
    /// </summary>
    public async Task<ServiceResult<AdminTournamentDto>> ReopenAsync(int id, ReopenTournamentRequest request)
    {
        var tournament = await _tournaments.GetByIdAsync(id);
        if (tournament == null) return NotFound("Tournament not found.");
        if (!tournament.IsClosed) return Conflict($"{tournament.Name} isn't closed.");

        var now = _clock.GetUtcNow().UtcDateTime;
        if (request.EndDate != null && tournament.StartDate == null)
            return BadRequest("This tournament has no dates, so it won't close by itself. Reopen it without an end date.");
        var endDate = request.EndDate ?? tournament.EndDate;
        if (endDate != null && TournamentDates.ClosesAtUtc(endDate.Value, tournament.TimeZoneId) <= now)
        {
            if (request.EndDate == null)
                return BadRequest("Its end date has passed, so it would close again within the hour. Choose a new end date.");
            return BadRequest("That end date has passed too. Choose a later one.");
        }
        // Not the one-year limit: a tournament reopened long after it began is still that tournament.
        if (request.EndDate < tournament.StartDate)
            return BadRequest("The end date can't be before the start date.");
        if (request.EndDate < TournamentDates.Today(tournament.TimeZoneId, now))
            return BadRequest("That end date has passed too. Choose a later one.");

        tournament.EndDate = endDate;
        tournament.Status = tournament.IsStarted ? TournamentStatus.Live : TournamentStatus.Upcoming;
        tournament.CompletedAt = null;
        tournament.CompletedByUserId = null;
        await _unitOfWork.SaveChangesAsync();
        return Success((await Rows([id])).Single());
    }

    private async Task<List<AdminTournamentDto>> Rows(List<int> ids)
    {
        var rows = await _tournaments.QueryAsync(q => q
            .Where(t => ids.Contains(t.Id))
            .Select(t => new
            {
                t.Id, t.Name, t.Sport, t.Status, t.StartDate, t.EndDate, t.CreatedAt, t.CompletedAt, t.CreatedByUserId,
                Teams = t.Teams.Count
            }), tracking: false);

        var creatorIds = rows.Select(r => r.CreatedByUserId).Distinct().ToList();
        var creators = (await _users.QueryAsync(q => q.Where(u => creatorIds.Contains(u.UserId)).Select(u => new { u.UserId, u.Name, u.EmailId }), tracking: false))
            .ToDictionary(u => u.UserId);
        var members = await _members.QueryAsync(q => q.Where(m => ids.Contains(m.TournamentId) && !m.IsDisabled).Select(m => new { m.TournamentId, m.UserId }), tracking: false);
        var players = await _players.QueryAsync(q => q.Where(p => ids.Contains(p.TournamentId) && p.UserId != null).Select(p => new { p.TournamentId, UserId = p.UserId!.Value }), tracking: false);

        return ids.Select(id => rows.First(r => r.Id == id)).Select(r =>
        {
            var creator = creators.GetValueOrDefault(r.CreatedByUserId);
            return new AdminTournamentDto
            {
                Id = r.Id,
                Name = r.Name,
                Sport = r.Sport,
                Status = r.Status,
                StartDate = r.StartDate,
                EndDate = r.EndDate,
                CreatedAt = r.CreatedAt,
                CompletedAt = r.CompletedAt,
                Creator = new PersonDto { UserId = r.CreatedByUserId, Name = creator?.Name ?? "Unknown" },
                CreatorEmail = creator?.EmailId,
                Teams = r.Teams,
                People = members.Where(m => m.TournamentId == r.Id).Select(m => m.UserId)
                    .Concat(players.Where(p => p.TournamentId == r.Id).Select(p => p.UserId))
                    .Distinct().Count(),
                CanReopen = r.Status is TournamentStatus.Completed or TournamentStatus.Cancelled
            };
        }).ToList();
    }
}
