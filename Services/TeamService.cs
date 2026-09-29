using TournamentScheduler.Api.Data;
using TournamentScheduler.Api.Data.DataServices;
using TournamentScheduler.Api.Data.Queries;
using TournamentScheduler.Api.Models;
using TournamentScheduler.Api.Models.Cricket;

namespace TournamentScheduler.Api.Services;

public interface ITeamService
{
    Task<ServiceResult<List<Team>>> GetTeamsAsync(int tournamentId);
    Task<ServiceResult<Team>> GetTeamAsync(int tournamentId, int id);
    Task<ServiceResult<Team>> CreateTeamAsync(int tournamentId, CreateTeamRequest request);
    Task<ServiceResult<Team>> UpdateTeamAsync(int tournamentId, int id, UpdateTeamRequest request);
    Task<ServiceResult<bool>> DeleteTeamAsync(int tournamentId, int id);
    Task<ServiceResult<Player>> AddPlayerAsync(int tournamentId, int teamId, CreatePlayerRequest request);
    Task<ServiceResult<Player>> UpdatePlayerAsync(int tournamentId, int teamId, int playerId, UpdatePlayerRequest request);
    Task<ServiceResult<bool>> DeletePlayerAsync(int tournamentId, int teamId, int playerId);
}

/// <summary>A tournament's teams and their players, including each player's cricket profile.</summary>
public class TeamService : ServiceBase, ITeamService
{
    private readonly IDataService<Tournament> _tournaments;
    private readonly IDataService<Team> _teams;
    private readonly IDataService<Player> _players;
    private readonly IUnitOfWork _unitOfWork;

    public TeamService(
        IDataService<Tournament> tournaments,
        IDataService<Team> teams,
        IDataService<Player> players,
        IUnitOfWork unitOfWork)
    {
        _tournaments = tournaments;
        _teams = teams;
        _players = players;
        _unitOfWork = unitOfWork;
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
        return Success(teams);
    }

    public async Task<ServiceResult<Team>> GetTeamAsync(int tournamentId, int id)
    {
        var team = await _teams.FirstOrDefaultAsync(
            t => t.Id == id && t.TournamentId == tournamentId, TeamQueries.WithPlayersAndProfiles);
        if (team == null) return NotFound();
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

        var team = new Team { Name = name, TournamentId = tournamentId };
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

        if (ValidateCricket(request.Cricket) is { } cricketError)
            return BadRequest(cricketError);

        var player = new Player
        {
            Name = name,
            Position = request.Position,
            JerseyNumber = request.JerseyNumber,
            TeamId = teamId
        };
        if (request.Cricket != null)
            player.Cricket = NewProfile(request.Cricket);

        _players.Add(player);
        await _unitOfWork.SaveChangesAsync();
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

        if (ValidateCricket(request.Cricket) is { } cricketError)
            return BadRequest(cricketError);

        player.Name = name;
        player.Position = request.Position;
        player.JerseyNumber = request.JerseyNumber;

        // Null means "not sent", not "clear it" — a football client never sends this block, and a
        // rename must not silently discard a cricketer's styles.
        if (request.Cricket != null)
        {
            if (player.Cricket == null)
                player.Cricket = NewProfile(request.Cricket);
            else
                Apply(request.Cricket, player.Cricket);
        }

        await _unitOfWork.SaveChangesAsync();
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

    /// <summary>
    /// An arm without a type (or the reverse) is half a bowling style, and would render as
    /// nothing at all. Reject it here rather than storing a profile that cannot be described.
    /// </summary>
    private static string? ValidateCricket(CricketProfileInput? input)
    {
        if (input == null) return null;

        if (input.BowlingArm.HasValue != input.BowlingType.HasValue)
            return "A bowling style needs both an arm and a type, or neither.";

        if (input.BattingOrderPreference is < 1 or > 11)
            return "Batting order preference must be between 1 and 11.";

        return null;
    }

    private static PlayerCricketProfile NewProfile(CricketProfileInput input)
    {
        var profile = new PlayerCricketProfile();
        Apply(input, profile);
        return profile;
    }

    private static void Apply(CricketProfileInput input, PlayerCricketProfile profile)
    {
        profile.PrimaryRole = input.PrimaryRole;
        profile.BattingStyle = input.BattingStyle;
        profile.BattingOrderPreference = input.BattingOrderPreference;

        // A pure batter keeps no bowling style: the form hides those fields, so anything still
        // sitting in the payload is stale rather than intended.
        var bowls = CricketRoles.Bowls(input.PrimaryRole);
        profile.BowlingArm = bowls ? input.BowlingArm : null;
        profile.BowlingType = bowls ? input.BowlingType : null;
    }
}
