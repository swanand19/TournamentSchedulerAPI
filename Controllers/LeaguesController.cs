using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TournamentScheduler.Api.Auth;
using TournamentScheduler.Api.Auth.Permissions;
using TournamentScheduler.Api.Auth.TournamentAccess;
using TournamentScheduler.Api.Gateway;
using TournamentScheduler.Api.Http;
using TournamentScheduler.Api.Models;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Models.Leagues;
using TournamentScheduler.Api.Services.Leagues;
using TournamentScheduler.Api.Services.Pictures;

namespace TournamentScheduler.Api.Controllers;

/// <summary>
/// Leagues and seasons (2026-11-24). Not tournament-scoped: a league lives above its tournaments, and
/// <see cref="LeagueService"/> checks who runs it. Bringing teams back is a season's own business (its owners, Edit).
/// A season is started with TOURNAMENT_CREATE { leagueId }. Route values: {leagueId}, {userId}, {tournamentId}; a
/// season's own routes use the tournament's {id}. The signed-out pages are api/public/leagues/{code}.
/// </summary>
[ApiController]
[DefaultRoles(RoleIds.EndUser)] // every signed-up user; Admin is ticked too
public class LeaguesController : ApiController
{
    private readonly ILeagueService _leagues;
    private readonly IPictureService _pictures;

    public LeaguesController(ILeagueService leagues, IPictureService pictures)
    {
        _leagues = leagues;
        _pictures = pictures;
    }

    // GET api/leagues?sport=Cricket — the leagues the caller runs ("Part of a league?", Home)
    [HttpGet("api/leagues")]
    [ServiceRequestId("LEAGUE_LIST")]
    [NotTournamentScoped]
    public async Task<ActionResult<ApiResponse<List<LeagueListItemDto>>>> List([FromQuery] Sport? sport) => Respond(await _leagues.ListMineAsync(sport));

    // GET api/leagues/candidates?sport=Cricket — the caller's tournaments that could become earlier seasons
    [HttpGet("api/leagues/candidates")]
    [ServiceRequestId("LEAGUE_CANDIDATES")]
    [NotTournamentScoped]
    public async Task<ActionResult<ApiResponse<List<LeagueCandidateDto>>>> Candidates([FromQuery] Sport sport) =>
        Respond(await _leagues.CandidatesAsync(sport));

    // POST api/leagues — { name, shortName, sport, area, tournamentIds }
    [HttpPost("api/leagues")]
    [ServiceRequestId("LEAGUE_CREATE")]
    [NotTournamentScoped]
    public async Task<ActionResult<ApiResponse<LeagueDto>>> Create([FromBody] CreateLeagueRequest request) => Respond(await _leagues.CreateAsync(request));

    // GET api/leagues/3 — anyone signed in (leagues are public)
    [HttpGet("api/leagues/{leagueId}")]
    [ServiceRequestId("LEAGUE_GET")]
    [NotTournamentScoped]
    public async Task<ActionResult<ApiResponse<LeagueDto>>> Get(int leagueId) => Respond(await _leagues.GetAsync(leagueId));

    // PUT api/leagues/3 — { name, shortName, area }
    [HttpPut("api/leagues/{leagueId}")]
    [ServiceRequestId("LEAGUE_UPDATE")]
    [NotTournamentScoped]
    public async Task<ActionResult<ApiResponse<LeagueDto>>> Update(int leagueId, [FromBody] UpdateLeagueRequest request) =>
        Respond(await _leagues.UpdateAsync(leagueId, request));

    // DELETE api/leagues/3 — the league only; its tournaments stay
    [HttpDelete("api/leagues/{leagueId}")]
    [ServiceRequestId("LEAGUE_DELETE")]
    [NotTournamentScoped]
    public async Task<ActionResult<ApiResponse<bool>>> Delete(int leagueId) => Respond(await _leagues.DeleteAsync(leagueId));

    // PUT api/leagues/3/logo — { image }
    [HttpPut("api/leagues/{leagueId}/logo")]
    [ServiceRequestId("LEAGUE_LOGO_SET")]
    [NotTournamentScoped]
    public async Task<ActionResult<ApiResponse<PictureDto>>> SetLogo(int leagueId, [FromBody] SetPictureRequest request) =>
        Respond(await _pictures.SetLeagueLogoAsync(leagueId, request));

    // DELETE api/leagues/3/logo
    [HttpDelete("api/leagues/{leagueId}/logo")]
    [ServiceRequestId("LEAGUE_LOGO_REMOVE")]
    [NotTournamentScoped]
    public async Task<ActionResult<ApiResponse<PictureDto>>> RemoveLogo(int leagueId) => Respond(await _pictures.RemoveLeagueLogoAsync(leagueId));

    // POST api/leagues/3/organisers — { contact }
    [HttpPost("api/leagues/{leagueId}/organisers")]
    [ServiceRequestId("LEAGUE_ORGANISER_ADD")]
    [NotTournamentScoped]
    public async Task<ActionResult<ApiResponse<LeagueDto>>> AddOrganiser(int leagueId, [FromBody] AddLeagueOrganiserRequest request) =>
        Respond(await _leagues.AddOrganiserAsync(leagueId, request));

    // DELETE api/leagues/3/organisers/7 — an organiser taking another off, or stepping down
    [HttpDelete("api/leagues/{leagueId}/organisers/{userId}")]
    [ServiceRequestId("LEAGUE_ORGANISER_REMOVE")]
    [NotTournamentScoped]
    public async Task<ActionResult<ApiResponse<LeagueDto>>> RemoveOrganiser(int leagueId, int userId) =>
        Respond(await _leagues.RemoveOrganiserAsync(leagueId, userId));

    // POST api/leagues/3/seasons — { tournamentId }: tick a tournament the caller owns in as a season
    [HttpPost("api/leagues/{leagueId}/seasons")]
    [ServiceRequestId("LEAGUE_SEASON_ADD")]
    [NotTournamentScoped]
    public async Task<ActionResult<ApiResponse<LeagueDto>>> AddSeason(int leagueId, [FromBody] LeagueSeasonRequest request) =>
        Respond(await _leagues.AddSeasonAsync(leagueId, request));

    // DELETE api/leagues/3/seasons/12 — take a season out (the tournament stays)
    [HttpDelete("api/leagues/{leagueId}/seasons/{tournamentId}")]
    [ServiceRequestId("LEAGUE_SEASON_REMOVE")]
    [NotTournamentScoped]
    public async Task<ActionResult<ApiResponse<LeagueDto>>> RemoveSeason(int leagueId, int tournamentId) =>
        Respond(await _leagues.RemoveSeasonAsync(leagueId, tournamentId));

    // GET api/leagues/3/stats?season=2 — every season, or one
    [HttpGet("api/leagues/{leagueId}/stats")]
    [ServiceRequestId("LEAGUE_STATS")]
    [NotTournamentScoped]
    public async Task<ActionResult<ApiResponse<LeagueStatsDto>>> Stats(int leagueId, [FromQuery] int? season) =>
        Respond(await _leagues.StatsAsync(leagueId, season));

    // GET api/leagues/3/teams — titles and every team's record
    [HttpGet("api/leagues/{leagueId}/teams")]
    [ServiceRequestId("LEAGUE_TEAMS")]
    [NotTournamentScoped]
    public async Task<ActionResult<ApiResponse<LeagueTeamsDto>>> Teams(int leagueId) => Respond(await _leagues.TeamsAsync(leagueId));

    // GET api/tournaments/12/league/teams — "Bring back teams": every earlier season's teams
    [HttpGet("api/tournaments/{id}/league/teams")]
    [ServiceRequestId("LEAGUE_BRING_BACK_OPTIONS")]
    [TournamentAccess(TournamentPermission.Edit)]
    public async Task<ActionResult<ApiResponse<BringBackOptionsDto>>> BringBackOptions(int id) => Respond(await _leagues.BringBackOptionsAsync(id));

    // POST api/tournaments/12/league/teams — { teamIds }
    [HttpPost("api/tournaments/{id}/league/teams")]
    [ServiceRequestId("LEAGUE_BRING_BACK")]
    [TournamentAccess(TournamentPermission.Edit)]
    public async Task<ActionResult<ApiResponse<BringBackResultDto>>> BringBack(int id, [FromBody] BringBackTeamsRequest request) =>
        Respond(await _leagues.BringBackAsync(id, request));

    // GET api/public/leagues/ABCD2345 — the signed-out league page: names and results only
    [HttpGet("api/public/leagues/{code}")]
    [ServiceRequestId("PUBLIC_LEAGUE")]
    [AllowAnonymousService]
    [NotTournamentScoped]
    [EnableRateLimiting(PublicRateLimit.Policy)]
    public async Task<ActionResult<ApiResponse<JsonNode>>> PublicLeague(string code) => Respond(await _leagues.PublicAsync(code));

    // GET api/public/leagues/ABCD2345/stats?season=2
    [HttpGet("api/public/leagues/{code}/stats")]
    [ServiceRequestId("PUBLIC_LEAGUE_STATS")]
    [AllowAnonymousService]
    [NotTournamentScoped]
    [EnableRateLimiting(PublicRateLimit.Policy)]
    public async Task<ActionResult<ApiResponse<JsonNode>>> PublicStats(string code, [FromQuery] int? season) =>
        Respond(await _leagues.PublicStatsAsync(code, season));

    // GET api/public/leagues/ABCD2345/teams
    [HttpGet("api/public/leagues/{code}/teams")]
    [ServiceRequestId("PUBLIC_LEAGUE_TEAMS")]
    [AllowAnonymousService]
    [NotTournamentScoped]
    [EnableRateLimiting(PublicRateLimit.Policy)]
    public async Task<ActionResult<ApiResponse<JsonNode>>> PublicTeams(string code) => Respond(await _leagues.PublicTeamsAsync(code));
}
