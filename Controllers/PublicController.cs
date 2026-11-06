using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TournamentScheduler.Api.Auth;
using TournamentScheduler.Api.Auth.TournamentAccess;
using TournamentScheduler.Api.Gateway;
using TournamentScheduler.Api.Http;
using TournamentScheduler.Api.Models.Public;
using TournamentScheduler.Api.Services.Public;

namespace TournamentScheduler.Api.Controllers;

/// <summary>
/// Public live scores (6E): a public tournament for anyone with its link, no account needed. Read
/// only, people and permissions taken out (<see cref="PublicShapes"/>), limited per address
/// (<see cref="PublicRateLimit"/>). {code} is the 8 characters in the link; a wrong or switched-off
/// one is 404. Still through the encrypted gateway: the public page is the web app itself.
/// </summary>
[ApiController]
[Route("api/public/{code}")]
[AllowAnonymousService]
[NotTournamentScoped]
[EnableRateLimiting(PublicRateLimit.Policy)]
public class PublicController : ApiController
{
    private readonly IPublicTournamentService _public;

    public PublicController(IPublicTournamentService service) => _public = service;

    // GET api/public/ABCD2345 — name, sport, status, dates
    [HttpGet]
    [ServiceRequestId("PUBLIC_TOURNAMENT")]
    public async Task<ActionResult<ApiResponse<PublicTournamentDto>>> Tournament(string code) =>
        Respond(await _public.GetAsync(code));

    // GET api/public/ABCD2345/matches — the match cards, as the apps' match list gets them
    [HttpGet("matches")]
    [ServiceRequestId("PUBLIC_MATCHES")]
    public async Task<ActionResult<ApiResponse<JsonNode>>> Matches(string code) =>
        Respond(await _public.MatchesAsync(code));

    // GET api/public/ABCD2345/matches/7 — the sport's own match answer (football match / cricket state)
    [HttpGet("matches/{matchId}")]
    [ServiceRequestId("PUBLIC_MATCH")]
    public async Task<ActionResult<ApiResponse<JsonNode>>> Match(string code, int matchId) =>
        Respond(await _public.MatchAsync(code, matchId));

    // GET api/public/ABCD2345/matches/7/events
    [HttpGet("matches/{matchId}/events")]
    [ServiceRequestId("PUBLIC_MATCH_EVENTS")]
    public async Task<ActionResult<ApiResponse<JsonNode>>> Events(string code, int matchId) =>
        Respond(await _public.MatchEventsAsync(code, matchId));

    // GET api/public/ABCD2345/matches/7/watch — as WATCH_MATCH: the header and both sides
    [HttpGet("matches/{matchId}/watch")]
    [ServiceRequestId("PUBLIC_WATCH")]
    public async Task<ActionResult<ApiResponse<JsonNode>>> Watch(string code, int matchId) =>
        Respond(await _public.WatchAsync(code, matchId));

    // GET api/public/ABCD2345/matches/7/activity?before=&take= — as WATCH_ACTIVITY
    [HttpGet("matches/{matchId}/activity")]
    [ServiceRequestId("PUBLIC_ACTIVITY")]
    public async Task<ActionResult<ApiResponse<JsonNode>>> Activity(string code, int matchId, [FromQuery] int? before, [FromQuery] int? take) =>
        Respond(await _public.ActivityAsync(code, matchId, before, take));

    // GET api/public/ABCD2345/matches/7/scorecard — cricket
    [HttpGet("matches/{matchId}/scorecard")]
    [ServiceRequestId("PUBLIC_SCORECARD")]
    public async Task<ActionResult<ApiResponse<JsonNode>>> Scorecard(string code, int matchId) =>
        Respond(await _public.ScorecardAsync(code, matchId));

    // GET api/public/ABCD2345/matches/7/awards — cricket: player of the match and the best of each discipline
    [HttpGet("matches/{matchId}/awards")]
    [ServiceRequestId("PUBLIC_AWARDS")]
    public async Task<ActionResult<ApiResponse<JsonNode>>> Awards(string code, int matchId) =>
        Respond(await _public.AwardsAsync(code, matchId));

    // GET api/public/ABCD2345/stats — the sport's tables and leaderboards
    [HttpGet("stats")]
    [ServiceRequestId("PUBLIC_STATS")]
    public async Task<ActionResult<ApiResponse<JsonNode>>> Stats(string code) =>
        Respond(await _public.StatsAsync(code));

    // GET api/public/ABCD2345/knockout — the bracket
    [HttpGet("knockout")]
    [ServiceRequestId("PUBLIC_KNOCKOUT")]
    public async Task<ActionResult<ApiResponse<JsonNode>>> Knockout(string code) =>
        Respond(await _public.KnockoutAsync(code));

    // GET api/public/ABCD2345/players/12 — a player's name and sport cards
    [HttpGet("players/{playerId}")]
    [ServiceRequestId("PUBLIC_PLAYER")]
    public async Task<ActionResult<ApiResponse<PublicPlayerDto>>> Player(string code, int playerId) =>
        Respond(await _public.PlayerAsync(code, playerId));
}
