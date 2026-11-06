using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using TournamentScheduler.Api.Data.DataServices;
using TournamentScheduler.Api.Models;
using TournamentScheduler.Api.Models.Cricket;
using TournamentScheduler.Api.Models.Public;
using TournamentScheduler.Api.Services.Cricket;
using TournamentScheduler.Api.Services.Football;
using TournamentScheduler.Api.Services.Knockouts;
using TournamentScheduler.Api.Services.Profiles;
using TournamentScheduler.Api.Services.Watch;

namespace TournamentScheduler.Api.Services.Public;

public interface IPublicTournamentService
{
    Task<ServiceResult<PublicTournamentDto>> GetAsync(string code);
    Task<ServiceResult<JsonNode>> MatchesAsync(string code);
    Task<ServiceResult<JsonNode>> MatchAsync(string code, int matchId);
    Task<ServiceResult<JsonNode>> MatchEventsAsync(string code, int matchId);
    Task<ServiceResult<JsonNode>> WatchAsync(string code, int matchId);
    Task<ServiceResult<JsonNode>> ActivityAsync(string code, int matchId, int? before, int? take);
    Task<ServiceResult<JsonNode>> ScorecardAsync(string code, int matchId);
    Task<ServiceResult<JsonNode>> AwardsAsync(string code, int matchId);
    Task<ServiceResult<JsonNode>> StatsAsync(string code);
    Task<ServiceResult<JsonNode>> KnockoutAsync(string code);
    Task<ServiceResult<PublicPlayerDto>> PlayerAsync(string code, int playerId);
}

/// <summary>
/// Public live scores (6E): what anyone with a tournament's link sees, signed in or not, while its
/// owners keep it public. Every answer is the one the apps already get (the same services build it),
/// passed through <see cref="PublicShapes"/> so nothing about people or permissions goes out. A
/// wrong, old or switched-off code is "not found", exactly like a code that never existed.
///
/// The match answers are kept for a moment (<see cref="PublicOptions.CacheSeconds"/>): a crowd
/// watching the same match costs the database one read every couple of seconds, not one each.
/// </summary>
public class PublicTournamentService : ServiceBase, IPublicTournamentService
{
    public const string NotPublic =
        "This tournament isn't public, or its link has changed. Ask the organiser for the current link.";

    private readonly IDataService<Tournament> _tournaments;
    private readonly IDataService<Match> _matches;
    private readonly IDataService<CricketMatch> _cricketMatches;
    private readonly IDataService<Player> _players;
    private readonly ITournamentService _tournamentService;
    private readonly IFootballMatchService _football;
    private readonly ICricketScoringService _cricket;
    private readonly ICricketMatchQueryService _cricketQueries;
    private readonly IStatsService _stats;
    private readonly ICricketStatsService _cricketStats;
    private readonly IKnockoutService _knockouts;
    private readonly IWatchService _watch;
    private readonly IProfileService _profiles;
    private readonly IMemoryCache _cache;
    private readonly PublicOptions _options;

    public PublicTournamentService(
        IDataService<Tournament> tournaments, IDataService<Match> matches, IDataService<CricketMatch> cricketMatches,
        IDataService<Player> players, ITournamentService tournamentService, IFootballMatchService football,
        ICricketScoringService cricket, ICricketMatchQueryService cricketQueries, IStatsService stats,
        ICricketStatsService cricketStats, IKnockoutService knockouts, IWatchService watch, IProfileService profiles,
        IMemoryCache cache, IOptions<PublicOptions> options)
    {
        _tournaments = tournaments;
        _matches = matches;
        _cricketMatches = cricketMatches;
        _players = players;
        _tournamentService = tournamentService;
        _football = football;
        _cricket = cricket;
        _cricketQueries = cricketQueries;
        _stats = stats;
        _cricketStats = cricketStats;
        _knockouts = knockouts;
        _watch = watch;
        _profiles = profiles;
        _cache = cache;
        _options = options.Value;
    }

    public async Task<ServiceResult<PublicTournamentDto>> GetAsync(string code)
    {
        if (await FindAsync(code) is not { } t) return NotFound(NotPublic);
        return Success(new PublicTournamentDto
        {
            Id = t.Id,
            Code = t.PublicCode!,
            Name = t.Name,
            Sport = t.Sport,
            Status = t.Status,
            IsStarted = t.IsStarted,
            StartDate = t.StartDate,
            EndDate = t.EndDate,
            HasKnockouts = await _knockouts.AnyAsync(t.Id)
        });
    }

    public async Task<ServiceResult<JsonNode>> MatchesAsync(string code)
    {
        if (await FindAsync(code) is not { } t) return NotFound(NotPublic);
        return Shape(await _tournamentService.GetMatchesAsync(t.Id));
    }

    /// <summary>The sport's own match answer — football's match, cricket's state — as the viewer screens use it.</summary>
    public Task<ServiceResult<JsonNode>> MatchAsync(string code, int matchId) =>
        ForMatchAsync(code, matchId, "match", async (t, id) =>
        {
            if (t.Sport == Sport.Cricket)
                return await _cricket.GetAsync(id) is { } match ? Success<object>(_cricket.BuildState(match)) : NotFound("Match not found.");
            return await _football.GetMatch(id);
        });

    public Task<ServiceResult<JsonNode>> MatchEventsAsync(string code, int matchId) =>
        ForMatchAsync(code, matchId, "events", async (t, id) =>
            t.Sport == Sport.Cricket ? await _cricketQueries.GetEventsAsync(id) : await _football.GetEvents(id));

    public Task<ServiceResult<JsonNode>> WatchAsync(string code, int matchId) =>
        ForMatchAsync(code, matchId, "watch", async (t, id) => Widen(await _watch.GetMatchAsync(SportKey(t), id)));

    public Task<ServiceResult<JsonNode>> ActivityAsync(string code, int matchId, int? before, int? take) =>
        ForMatchAsync(code, matchId, $"activity:{before}:{take}", async (t, id) => Widen(await _watch.GetActivityAsync(SportKey(t), id, before, take)));

    public Task<ServiceResult<JsonNode>> ScorecardAsync(string code, int matchId) =>
        ForMatchAsync(code, matchId, "scorecard", async (t, id) =>
            t.Sport != Sport.Cricket ? BadRequest("Only cricket matches have a scorecard.")
            : await _cricket.BuildScorecardAsync(id) is { } card ? Success<object>(card) : NotFound("Match not found."));

    public Task<ServiceResult<JsonNode>> AwardsAsync(string code, int matchId) =>
        ForMatchAsync(code, matchId, "awards", async (t, id) =>
            t.Sport != Sport.Cricket ? BadRequest("Only cricket matches have awards.") : Widen(await _cricketQueries.GetAwardsAsync(id)));

    public async Task<ServiceResult<JsonNode>> StatsAsync(string code)
    {
        if (await FindAsync(code) is not { } t) return NotFound(NotPublic);
        object? stats = t.Sport == Sport.Cricket ? await _cricketStats.BuildAsync(t.Id) : await _stats.BuildAsync(t.Id);
        return stats == null ? NotFound(NotPublic) : Success(PublicShapes.Of(stats)!);
    }

    public async Task<ServiceResult<JsonNode>> KnockoutAsync(string code)
    {
        if (await FindAsync(code) is not { } t) return NotFound(NotPublic);
        return Shape(Widen(await _knockouts.GetAsync(t.Id)));
    }

    /// <summary>A player of this tournament: their name and top-level sport cards, never the detailed career.</summary>
    public async Task<ServiceResult<PublicPlayerDto>> PlayerAsync(string code, int playerId)
    {
        if (await FindAsync(code) is not { } t) return NotFound(NotPublic);
        if (!await _players.AnyAsync(p => p.Id == playerId && p.TournamentId == t.Id)) return NotFound("Player not found.");

        var profile = await _profiles.GetProfileAsync(null, playerId);
        if (!profile.IsOk) return new ServiceError(profile.Outcome, profile.Error);
        return Success(new PublicPlayerDto { Name = profile.Value!.Name, Sports = profile.Value.Sports });
    }

    // -----------------------------------------------------------------

    private async Task<Tournament?> FindAsync(string code) =>
        PublicCodes.Normalise(code) is { } c
            ? await _tournaments.FirstOrDefaultAsync(t => t.PublicCode == c && t.IsPublic, tracking: false)
            : null;

    private static string SportKey(Tournament t) => t.Sport == Sport.Cricket ? "cricket" : "football";

    /// <summary>
    /// A match answer for a match of this public tournament, kept for a moment. Only successes are kept,
    /// and the key carries the tournament, so a link switched off stops at once (the code lookup isn't cached).
    /// </summary>
    private async Task<ServiceResult<JsonNode>> ForMatchAsync(string code, int matchId, string what, Func<Tournament, int, Task<ServiceResult<object>>> build)
    {
        if (await FindAsync(code) is not { } t) return NotFound(NotPublic);
        var inTournament = t.Sport == Sport.Cricket
            ? await _cricketMatches.AnyAsync(m => m.Id == matchId && m.TournamentId == t.Id)
            : await _matches.AnyAsync(m => m.Id == matchId && m.TournamentId == t.Id);
        if (!inTournament) return NotFound("Match not found.");

        var key = $"public:{t.Id}:{matchId}:{what}";
        if (_options.CacheSeconds > 0 && _cache.TryGetValue(key, out JsonNode? kept) && kept != null)
            return Success(kept);

        var result = Shape(await build(t, matchId));
        if (result.IsOk && _options.CacheSeconds > 0)
            _cache.Set(key, result.Value, TimeSpan.FromSeconds(_options.CacheSeconds));
        return result;
    }

    private static ServiceResult<JsonNode> Shape(ServiceResult<object> result) =>
        result.IsOk ? Success(PublicShapes.Of(result.Value)!) : new ServiceError(result.Outcome, result.Error);

    private static ServiceResult<object> Widen<T>(ServiceResult<T> result) =>
        result.IsOk ? Success<object>(result.Value!) : new ServiceError(result.Outcome, result.Error);
}
