using TournamentScheduler.Api.Data.DataServices;
using TournamentScheduler.Api.Models;
using TournamentScheduler.Api.Services.Cricket;

namespace TournamentScheduler.Api.Services;

public interface IPlayerMatchLinesService
{
    /// <summary>
    /// What a stats leaderboard row unfolds into: the player's innings and spells (cricket) or goals
    /// and assists (football) in this tournament, match by match.
    /// </summary>
    Task<ServiceResult<PlayerMatchLines>> GetAsync(int tournamentId, int playerId);
}

/// <summary>
/// Picks the sport's stats service for one player's lines (2026-11-20). The player must be in this
/// tournament's squads — the same check whether the call comes from the app or a public page, so
/// a public link can't be used to read players of another tournament.
/// </summary>
public class PlayerMatchLinesService : ServiceBase, IPlayerMatchLinesService
{
    public const string NotInTournament = "That player isn't in this tournament.";

    private readonly IDataService<Tournament> _tournaments;
    private readonly IDataService<Player> _players;
    private readonly IDataService<Team> _teams;
    private readonly IStatsService _football;
    private readonly ICricketStatsService _cricket;

    public PlayerMatchLinesService(
        IDataService<Tournament> tournaments,
        IDataService<Player> players,
        IDataService<Team> teams,
        IStatsService football,
        ICricketStatsService cricket)
    {
        _tournaments = tournaments;
        _players = players;
        _teams = teams;
        _football = football;
        _cricket = cricket;
    }

    public async Task<ServiceResult<PlayerMatchLines>> GetAsync(int tournamentId, int playerId)
    {
        var tournament = await _tournaments.FirstOrDefaultAsync(t => t.Id == tournamentId, tracking: false);
        if (tournament == null) return NotFound("Tournament not found.");

        var player = await _players.FirstOrDefaultAsync(p => p.Id == playerId && p.TournamentId == tournamentId, tracking: false);
        if (player == null) return NotFound(NotInTournament);
        var team = await _teams.FirstOrDefaultAsync(t => t.Id == player.TeamId, tracking: false);

        var lines = new PlayerMatchLines { PlayerId = player.Id, PlayerName = player.Name, TeamName = team?.Name ?? "" };
        if (tournament.Sport == Sport.Cricket)
            (lines.Batting, lines.Bowling) = await _cricket.PlayerLinesAsync(tournamentId, playerId);
        else
            (lines.Goals, lines.Assists) = await _football.PlayerLinesAsync(tournamentId, playerId);
        return Success(lines);
    }
}
