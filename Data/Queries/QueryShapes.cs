using Microsoft.EntityFrameworkCore;
using TournamentScheduler.Api.Models;
using TournamentScheduler.Api.Models.Cricket;

namespace TournamentScheduler.Api.Data.Queries;

// The related rows each screen needs, named once. Services pass these as the `shape` of a data
// service call (e.g. teams.ListAsync(..., TeamQueries.WithPlayersAndProfiles)) and so never need
// Entity Framework's Include themselves.

public static class TeamQueries
{
    public static IQueryable<Team> WithPlayers(IQueryable<Team> q) => q.Include(t => t.Players);

    public static IQueryable<Team> WithPlayersAndProfiles(IQueryable<Team> q) =>
        q.Include(t => t.Players).ThenInclude(p => p.Cricket);
}

public static class PlayerQueries
{
    public static IQueryable<Player> WithCricketProfile(IQueryable<Player> q) => q.Include(p => p.Cricket);
}

public static class ScheduleQueries
{
    public static IQueryable<SavedSchedule> WithFixtures(IQueryable<SavedSchedule> q) =>
        q.Include(s => s.Groups).ThenInclude(g => g.Fixtures);
}

public static class MatchQueries
{
    public static IQueryable<Match> WithSquad(IQueryable<Match> q) => q.Include(m => m.MatchPlayers);

    /// <summary>Both teams with their players, and the match squad: everything the match screen draws.</summary>
    public static IQueryable<Match> ForMatchScreen(IQueryable<Match> q) => q
        .Include(m => m.HomeTeam).ThenInclude(t => t!.Players)
        .Include(m => m.AwayTeam).ThenInclude(t => t!.Players)
        .Include(m => m.MatchPlayers);
}

public static class CricketMatchQueries
{
    public static IQueryable<CricketMatch> WithInnings(IQueryable<CricketMatch> q) => q.Include(m => m.Innings);

    /// <summary>The whole match: squad, every innings with its deliveries and rain interruptions.</summary>
    public static IQueryable<CricketMatch> Full(IQueryable<CricketMatch> q) => q
        .Include(m => m.Squad)
        .Include(m => m.Innings).ThenInclude(i => i.Balls)
        .Include(m => m.Innings).ThenInclude(i => i.Interruptions)
        .AsSplitQuery();
}
