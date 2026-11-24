using TournamentScheduler.Api.Auth;
using TournamentScheduler.Api.Data;
using TournamentScheduler.Api.Data.DataServices;
using TournamentScheduler.Api.Data.Queries;
using TournamentScheduler.Api.Models;
using TournamentScheduler.Api.Models.Accounts;

namespace TournamentScheduler.Api.Services.Access;

public interface ITeamJoinService
{
    Task<ServiceResult<JoinTeamPreviewDto>> PreviewAsync(JoinTeamPreviewRequest request);
    Task<ServiceResult<JoinTeamResult>> JoinAsync(JoinTeamRequest request);
}

/// <summary>
/// Joining a team with its code (docs/accounts-and-access-plan.md §6.1): the code shows the team and
/// the names in it nobody has claimed; the person taps theirs, or joins as a new player. Instant,
/// no approval. One place per person per tournament; a completed tournament can't be joined.
/// Wrong codes are limited per person (<see cref="JoinCodeAttempts"/>).
/// </summary>
public class TeamJoinService : ServiceBase, ITeamJoinService
{
    private readonly IDataService<Team> _teams;
    private readonly IDataService<Player> _players;
    private readonly IDataService<User> _users;
    private readonly IPlayerLinkService _links;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly JoinCodeAttempts _attempts;

    public TeamJoinService(
        IDataService<Team> teams,
        IDataService<Player> players,
        IDataService<User> users,
        IPlayerLinkService links,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        JoinCodeAttempts attempts)
    {
        _teams = teams;
        _players = players;
        _users = users;
        _links = links;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _attempts = attempts;
    }

    private sealed record FoundTeam(int TeamId, string TeamName, int TournamentId, string TournamentName, Sport Sport, bool AlreadyIn = false);

    public async Task<ServiceResult<JoinTeamPreviewDto>> PreviewAsync(JoinTeamPreviewRequest request)
    {
        var (team, error) = await FindOpenTeam(request.Code);
        if (error is { } e) return e;

        var places = await _players.ListAsync(
            p => p.TeamId == team!.TeamId && p.UserId == null && p.EmailId == null && p.PhoneNumber == null && p.LeftOn == null,
            q => PlayerQueries.WithCricketProfile(q).OrderBy(p => p.Name).ThenBy(p => p.JerseyNumber),
            tracking: false);

        return Success(new JoinTeamPreviewDto
        {
            TournamentId = team!.TournamentId,
            TournamentName = team.TournamentName,
            Sport = team.Sport,
            TeamId = team.TeamId,
            TeamName = team.TeamName,
            Places = places.Select(p => new JoinPlaceDto { PlayerId = p.Id, Name = p.Name, Detail = Detail(p, team.Sport) }).ToList()
        });
    }

    public async Task<ServiceResult<JoinTeamResult>> JoinAsync(JoinTeamRequest request)
    {
        var (team, error) = await FindOpenTeam(request.Code, request.Link);
        if (error is { } e) return e;

        var user = await _users.GetByIdAsync(_currentUser.UserId);
        if (user == null) return NotFound("Account not found.");

        // An invite link opened again by someone already in this team: nothing to do, the apps just open it.
        if (team!.AlreadyIn)
        {
            var mine = await _players.FirstOrDefaultAsync(p => p.TeamId == team.TeamId && p.UserId == user.UserId, tracking: false);
            return Success(new JoinTeamResult
            {
                TournamentId = team.TournamentId, TournamentName = team.TournamentName, Sport = team.Sport,
                TeamId = team.TeamId, TeamName = team.TeamName, PlayerId = mine?.Id ?? 0, AlreadyIn = true
            });
        }

        Player place;
        // A link joins straight in as new (the user's choice, 2026-11-23): an admin removes a duplicate name by hand.
        if (request.PlayerId is { } playerId && !request.Link)
        {
            // Names the organiser gave an email or number are waiting for that person, so they can't be picked here.
            var picked = await _players.FirstOrDefaultAsync(p => p.Id == playerId && p.TeamId == team!.TeamId && p.LeftOn == null, PlayerQueries.WithCricketProfile);
            if (picked == null) return NotFound("That name isn't in this team any more. Pick again.");
            if (picked.UserId != null || picked.EmailId != null || picked.PhoneNumber != null) return Conflict("Someone else has just claimed that name. Pick again.");
            place = picked;
        }
        else
        {
            place = new Player { Name = user.Name, TeamId = team!.TeamId, TournamentId = team.TournamentId };
            _players.Add(place);
        }

        await _links.LinkAsync(place, user, team.Sport);
        try
        {
            await _unitOfWork.SaveChangesAsync();
        }
        catch (DuplicateEntryException)
        {
            return Conflict("You've just joined a team in this tournament.");
        }

        return Success(new JoinTeamResult
        {
            TournamentId = team.TournamentId,
            TournamentName = team.TournamentName,
            Sport = team.Sport,
            TeamId = team.TeamId,
            TeamName = team.TeamName,
            PlayerId = place.Id
        });
    }

    /// <summary>
    /// The code's team, if it can be joined by this person now. From an invite link a dead code says so in a link's
    /// words, and being in this very team already isn't an error (<see cref="FoundTeam.AlreadyIn"/>).
    /// </summary>
    private async Task<(FoundTeam? Team, ServiceError? Error)> FindOpenTeam(string? typed, bool link = false)
    {
        var me = _currentUser.UserId;
        if (_attempts.WaitFor(me) is { } wait)
            return (null, TooManyRequests($"Too many wrong codes. Try again in {Math.Max(1, (int)Math.Ceiling(wait.TotalMinutes))} minutes."));

        var code = JoinCodes.Normalise(typed);
        if (code == null) return (null, link ? NotFound(InviteLinks.NoLongerWorks) : BadRequest($"A team code is {JoinCodes.Length} letters and numbers."));

        var team = await _teams.QueryFirstOrDefaultAsync(q => q
            .Where(t => t.JoinCode == code)
            .Select(t => new { t.Id, t.Name, t.TournamentId, TournamentName = t.Tournament!.Name, t.Tournament.Sport, t.Tournament.Status }), tracking: false);
        if (team == null)
        {
            _attempts.Missed(me);
            return (null, NotFound(link ? InviteLinks.NoLongerWorks : "No team has that code. Check it with the organiser."));
        }

        if (team.Status is TournamentStatus.Completed or TournamentStatus.Cancelled)
            return (null, Conflict(link ? InviteLinks.TournamentOver(team.TournamentName) : $"{team.TournamentName} is over, so its teams can't be joined."));

        if (await _links.TeamAlreadyInAsync(team.TournamentId, me) is { } already)
        {
            if (link && already == team.Name)
                return (new FoundTeam(team.Id, team.Name, team.TournamentId, team.TournamentName, team.Sport, AlreadyIn: true), null);
            return (null, Conflict(already == team.Name
                ? $"You're already in {already}."
                : $"You're already in {already} in this tournament. Leave that team first."));
        }

        return (new FoundTeam(team.Id, team.Name, team.TournamentId, team.TournamentName, team.Sport), null);
    }

    /// <summary>What tells two players of the same name apart: "#7 · Batter" (cricket) or "#7 · Defender" (football).</summary>
    private static string? Detail(Player place, Sport sport)
    {
        var parts = new List<string>();
        if (place.JerseyNumber is { } number) parts.Add($"#{number}");
        var what = sport == Sport.Cricket ? place.Cricket?.RoleLabel : place.Position;
        if (!string.IsNullOrWhiteSpace(what)) parts.Add(what.Trim());
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }
}
