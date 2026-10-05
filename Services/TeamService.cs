using TournamentScheduler.Api.Auth;
using TournamentScheduler.Api.Data;
using TournamentScheduler.Api.Data.DataServices;
using TournamentScheduler.Api.Data.Queries;
using TournamentScheduler.Api.Models;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Models.Cricket;
using TournamentScheduler.Api.Services.Access;

namespace TournamentScheduler.Api.Services;

public interface ITeamService
{
    Task<ServiceResult<List<Team>>> GetTeamsAsync(int tournamentId);
    Task<ServiceResult<Team>> GetTeamAsync(int tournamentId, int id);
    Task<ServiceResult<Team>> CreateTeamAsync(int tournamentId, CreateTeamRequest request);
    Task<ServiceResult<Team>> UpdateTeamAsync(int tournamentId, int id, UpdateTeamRequest request);
    Task<ServiceResult<bool>> DeleteTeamAsync(int tournamentId, int id);
    Task<ServiceResult<Team>> ResetJoinCodeAsync(int tournamentId, int id);
    Task<ServiceResult<Player>> AddPlayerAsync(int tournamentId, int teamId, CreatePlayerRequest request);
    Task<ServiceResult<Player>> UpdatePlayerAsync(int tournamentId, int teamId, int playerId, UpdatePlayerRequest request);
    Task<ServiceResult<Player>> UnlinkPlayerAsync(int tournamentId, int teamId, int playerId);
    Task<ServiceResult<bool>> DeletePlayerAsync(int tournamentId, int teamId, int playerId);
}

/// <summary>
/// A tournament's teams and their players, including each player's cricket profile, their link to
/// a sign-in account (phase 4) and the team's join code. Players' emails and join codes are shown
/// to the tournament's owners only.
/// </summary>
public class TeamService : ServiceBase, ITeamService
{
    private readonly IDataService<Tournament> _tournaments;
    private readonly IDataService<Team> _teams;
    private readonly IDataService<Player> _players;
    private readonly IPlayerLinkService _links;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITournamentContext _context;
    private readonly ICurrentUser _currentUser;

    public TeamService(
        IDataService<Tournament> tournaments,
        IDataService<Team> teams,
        IDataService<Player> players,
        IPlayerLinkService links,
        IUnitOfWork unitOfWork,
        ITournamentContext context,
        ICurrentUser currentUser)
    {
        _tournaments = tournaments;
        _teams = teams;
        _players = players;
        _links = links;
        _unitOfWork = unitOfWork;
        _context = context;
        _currentUser = currentUser;
    }

    private Task<bool> TournamentExists(int tournamentId) => _tournaments.AnyAsync(t => t.Id == tournamentId);

    private Task<Team?> FindTeam(int tournamentId, int id) =>
        _teams.FirstOrDefaultAsync(t => t.Id == id && t.TournamentId == tournamentId);

    private static string DuplicateName(string name) => $"A team named '{name}' already exists in this tournament.";

    public async Task<ServiceResult<List<Team>>> GetTeamsAsync(int tournamentId)
    {
        if (!await TournamentExists(tournamentId)) return NotFound("Tournament not found.");

        var teams = await _teams.ListAsync(
            t => t.TournamentId == tournamentId,
            q => TeamQueries.WithPlayersAndProfiles(q).OrderBy(t => t.Name));
        teams.ForEach(Present);
        return Success(teams);
    }

    public async Task<ServiceResult<Team>> GetTeamAsync(int tournamentId, int id)
    {
        var team = await _teams.FirstOrDefaultAsync(
            t => t.Id == id && t.TournamentId == tournamentId, TeamQueries.WithPlayersAndProfiles);
        if (team == null) return NotFound();
        Present(team);
        return Success(team);
    }

    public async Task<ServiceResult<Team>> CreateTeamAsync(int tournamentId, CreateTeamRequest request)
    {
        if (!await TournamentExists(tournamentId)) return NotFound("Tournament not found.");

        var name = request.Name?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest("Team name is required.");

        if (await _teams.AnyAsync(t => t.Name == name && t.TournamentId == tournamentId))
            return Conflict(DuplicateName(name));

        var team = new Team { Name = name, TournamentId = tournamentId, JoinCode = await NewJoinCode() };
        _teams.Add(team);
        try
        {
            await _unitOfWork.SaveChangesAsync();
        }
        catch (DuplicateEntryException)
        {
            // Another request created the same name between the check above and this save.
            return Conflict(DuplicateName(name));
        }
        Present(team);
        return Success(team);
    }

    public async Task<ServiceResult<Team>> UpdateTeamAsync(int tournamentId, int id, UpdateTeamRequest request)
    {
        var team = await FindTeam(tournamentId, id);
        if (team == null) return NotFound();

        var name = request.Name?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest("Team name is required.");

        if (await _teams.AnyAsync(t => t.Name == name && t.TournamentId == tournamentId && t.Id != id))
            return Conflict(DuplicateName(name));

        if (request.SetCaptain)
        {
            if (request.DefaultCaptainPlayerId.HasValue)
            {
                var onThisTeam = await _players.AnyAsync(p => p.Id == request.DefaultCaptainPlayerId && p.TeamId == id);
                if (!onThisTeam)
                    return BadRequest("The captain must be a player on this team.");
            }
            team.DefaultCaptainPlayerId = request.DefaultCaptainPlayerId;
        }

        team.Name = name;
        await _unitOfWork.SaveChangesAsync();
        Present(team);
        return Success(team);
    }

    public async Task<ServiceResult<bool>> DeleteTeamAsync(int tournamentId, int id)
    {
        var team = await FindTeam(tournamentId, id);
        if (team == null) return NotFound();

        _teams.Remove(team); // cascades to players
        await _unitOfWork.SaveChangesAsync();
        return Success(true);
    }

    /// <summary>A new join code, for when the old one has been shared too widely. The old one stops working at once.</summary>
    public async Task<ServiceResult<Team>> ResetJoinCodeAsync(int tournamentId, int id)
    {
        var team = await _teams.FirstOrDefaultAsync(
            t => t.Id == id && t.TournamentId == tournamentId, TeamQueries.WithPlayersAndProfiles);
        if (team == null) return NotFound("Team not found.");

        team.JoinCode = await NewJoinCode();
        await _unitOfWork.SaveChangesAsync();
        Present(team);
        return Success(team);
    }

    public async Task<ServiceResult<Player>> AddPlayerAsync(int tournamentId, int teamId, CreatePlayerRequest request)
    {
        var team = await FindTeam(tournamentId, teamId);
        if (team == null) return NotFound("Team not found.");

        var name = request.Name?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest("Player name is required.");

        if (request.JerseyNumber.HasValue
            && await _players.AnyAsync(p => p.TeamId == teamId && p.JerseyNumber == request.JerseyNumber))
            return Conflict($"Jersey number {request.JerseyNumber} is already taken on this team.");

        if (CricketProfiles.Problem(request.Cricket) is { } cricketError)
            return BadRequest(cricketError);

        var player = new Player
        {
            Name = name,
            Position = request.Position,
            JerseyNumber = request.JerseyNumber,
            TeamId = teamId,
            TournamentId = tournamentId
        };
        if (request.Cricket != null)
            player.Cricket = CricketProfiles.ForPlayer(request.Cricket);

        // With an email that has an account, the place is linked now and takes the account's name.
        if (await _links.SetEmailAsync(player, tournamentId, request.Email) is { } linkError)
            return linkError;

        _players.Add(player);
        if (await SaveLinked() is { } raceError) return raceError;
        Present(player);
        return Success(player);
    }

    public async Task<ServiceResult<Player>> UpdatePlayerAsync(int tournamentId, int teamId, int playerId, UpdatePlayerRequest request)
    {
        var team = await FindTeam(tournamentId, teamId);
        if (team == null) return NotFound("Team not found.");

        var player = await _players.FirstOrDefaultAsync(
            p => p.Id == playerId && p.TeamId == teamId, PlayerQueries.WithCricketProfile);
        if (player == null) return NotFound();

        var name = request.Name?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest("Player name is required.");

        if (request.JerseyNumber.HasValue
            && await _players.AnyAsync(p => p.TeamId == teamId && p.JerseyNumber == request.JerseyNumber && p.Id != playerId))
            return Conflict($"Jersey number {request.JerseyNumber} is already taken on this team.");

        if (CricketProfiles.Problem(request.Cricket) is { } cricketError)
            return BadRequest(cricketError);

        if (await _links.SetEmailAsync(player, tournamentId, request.Email) is { } linkError)
            return linkError;

        player.Position = request.Position;
        player.JerseyNumber = request.JerseyNumber;

        // A linked player's name and cricket profile are theirs, kept in step with their account.
        if (!player.IsLinked)
        {
            player.Name = name;

            // Null means "not sent", not "clear it" — a football client never sends this block, and a
            // rename must not silently discard a cricketer's styles.
            if (request.Cricket != null)
            {
                if (player.Cricket == null)
                    player.Cricket = CricketProfiles.ForPlayer(request.Cricket);
                else
                    CricketProfiles.Copy(request.Cricket, player.Cricket);
            }
        }

        if (await SaveLinked() is { } raceError) return raceError;
        Present(player);
        return Success(player);
    }

    /// <summary>
    /// Takes a squad place off the account linked to it — an owner fixing a wrong link, or the
    /// player who picked the wrong name ("Leave team"). The name stays: it's on the scorecards.
    /// </summary>
    public async Task<ServiceResult<Player>> UnlinkPlayerAsync(int tournamentId, int teamId, int playerId)
    {
        var player = await _players.FirstOrDefaultAsync(
            p => p.Id == playerId && p.TeamId == teamId && p.TournamentId == tournamentId, PlayerQueries.WithCricketProfile);
        if (player == null) return NotFound("Player not found.");

        var mine = player.UserId == _currentUser.UserId;
        if (!_context.IsOwner && !mine)
            return Forbidden("Only the tournament's owners can unlink another player.");
        if (_context.Status is TournamentStatus.Completed or TournamentStatus.Cancelled)
            return Conflict("This tournament is over, so its squads can't be changed.");
        if (!player.IsLinked && player.EmailId == null)
            return Conflict("This player isn't linked to an account.");

        _links.Unlink(player);
        await _unitOfWork.SaveChangesAsync();
        Present(player);
        return Success(player);
    }

    public async Task<ServiceResult<bool>> DeletePlayerAsync(int tournamentId, int teamId, int playerId)
    {
        var team = await FindTeam(tournamentId, teamId);
        if (team == null) return NotFound("Team not found.");

        var player = await _players.FirstOrDefaultAsync(p => p.Id == playerId && p.TeamId == teamId);
        if (player == null) return NotFound();

        _players.Remove(player);
        await _unitOfWork.SaveChangesAsync();
        return Success(true);
    }

    /// <summary>Saves a player change that another request may have beaten to a unique index.</summary>
    private async Task<ServiceError?> SaveLinked()
    {
        try
        {
            await _unitOfWork.SaveChangesAsync();
            return null;
        }
        catch (DuplicateEntryException)
        {
            // The one-place-per-tournament or jersey-number index, beaten by another request.
            return Conflict("Someone else changed this squad at the same moment. Reload the teams and try again.");
        }
    }

    private async Task<string> NewJoinCode()
    {
        // A clash is about one in a billion; checking costs one indexed read.
        string code;
        do code = JoinCodes.New();
        while (await _teams.AnyAsync(t => t.JoinCode == code));
        return code;
    }

    /// <summary>What this caller may see: emails and the join code for owners; which place is theirs.</summary>
    private void Present(Team team)
    {
        team.VisibleJoinCode = _context.IsOwner ? team.JoinCode : null;
        team.Players.ForEach(Present);
    }

    private void Present(Player player)
    {
        player.IsMe = player.UserId != null && player.UserId == _currentUser.UserId;
        player.Email = _context.IsOwner ? player.EmailId : null;
    }
}
