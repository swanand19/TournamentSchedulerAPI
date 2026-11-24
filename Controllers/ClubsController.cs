using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TournamentScheduler.Api.Auth;
using TournamentScheduler.Api.Auth.Permissions;
using TournamentScheduler.Api.Auth.TournamentAccess;
using TournamentScheduler.Api.Gateway;
using TournamentScheduler.Api.Http;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Models.Clubs;
using TournamentScheduler.Api.Services.Clubs;
using TournamentScheduler.Api.Services.Pictures;

namespace TournamentScheduler.Api.Controllers;

/// <summary>
/// Lasting teams (docs/reusable-teams-plan.md, step 2; people see "team", the code says club). Not tournament-scoped: a
/// club lives outside tournaments, and <see cref="ClubService"/> checks who runs it. Every change answers the whole club.
/// Route values: {clubId}, {clubPlayerId}, {userId}, {entryId}; keeping a tournament's team uses the tournament's {id}
/// and {teamId}. Entering tournaments (step 3): the admin's side here, not tournament-scoped; the organiser's side on
/// api/tournaments/{id}/entries, with Edit. The signed-out page (step 5) is api/public/clubs/{code}.
/// </summary>
[ApiController]
[DefaultRoles(RoleIds.EndUser)] // every signed-up user; Admin is ticked too
public class ClubsController : ApiController
{
    private readonly IClubService _clubs;
    private readonly IClubEntryService _entries;
    private readonly IPictureService _pictures;

    public ClubsController(IClubService clubs, IClubEntryService entries, IPictureService pictures)
    {
        _clubs = clubs;
        _entries = entries;
        _pictures = pictures;
    }

    // GET api/clubs — the caller's own teams (Home's Teams tab)
    [HttpGet("api/clubs")]
    [ServiceRequestId("CLUB_LIST")]
    [NotTournamentScoped]
    public async Task<ActionResult<ApiResponse<List<ClubListItemDto>>>> List() => Respond(await _clubs.ListMineAsync());

    // POST api/clubs — { name, sport, area, iPlay }
    [HttpPost("api/clubs")]
    [ServiceRequestId("CLUB_CREATE")]
    [NotTournamentScoped]
    public async Task<ActionResult<ApiResponse<ClubDto>>> Create([FromBody] CreateClubRequest request) => Respond(await _clubs.CreateAsync(request));

    // GET api/clubs/4 — anyone signed in (club pages are public, C-15)
    [HttpGet("api/clubs/{clubId}")]
    [ServiceRequestId("CLUB_GET")]
    [NotTournamentScoped]
    public async Task<ActionResult<ApiResponse<ClubDto>>> Get(int clubId) => Respond(await _clubs.GetAsync(clubId));

    // PUT api/clubs/4 — { name, area, captainClubPlayerId, setCaptain }
    [HttpPut("api/clubs/{clubId}")]
    [ServiceRequestId("CLUB_UPDATE")]
    [NotTournamentScoped]
    public async Task<ActionResult<ApiResponse<ClubDto>>> Update(int clubId, [FromBody] UpdateClubRequest request) =>
        Respond(await _clubs.UpdateAsync(clubId, request));

    // DELETE api/clubs/4 — the club only; its tournament squads stay as ordinary teams
    [HttpDelete("api/clubs/{clubId}")]
    [ServiceRequestId("CLUB_DELETE")]
    [NotTournamentScoped]
    public async Task<ActionResult<ApiResponse<bool>>> Delete(int clubId) => Respond(await _clubs.DeleteAsync(clubId));

    // PUT api/clubs/4/logo — { image }
    [HttpPut("api/clubs/{clubId}/logo")]
    [ServiceRequestId("CLUB_LOGO_SET")]
    [NotTournamentScoped]
    public async Task<ActionResult<ApiResponse<PictureDto>>> SetLogo(int clubId, [FromBody] SetPictureRequest request) =>
        Respond(await _pictures.SetClubLogoAsync(clubId, request));

    // DELETE api/clubs/4/logo
    [HttpDelete("api/clubs/{clubId}/logo")]
    [ServiceRequestId("CLUB_LOGO_REMOVE")]
    [NotTournamentScoped]
    public async Task<ActionResult<ApiResponse<PictureDto>>> RemoveLogo(int clubId) => Respond(await _pictures.RemoveClubLogoAsync(clubId));

    // POST api/clubs/4/join-code/reset
    [HttpPost("api/clubs/{clubId}/join-code/reset")]
    [ServiceRequestId("CLUB_JOIN_CODE_RESET")]
    [NotTournamentScoped]
    public async Task<ActionResult<ApiResponse<ClubDto>>> ResetJoinCode(int clubId) => Respond(await _clubs.ResetJoinCodeAsync(clubId));

    // POST api/clubs/4/players/lookup — { contact }
    [HttpPost("api/clubs/{clubId}/players/lookup")]
    [ServiceRequestId("CLUB_PLAYER_LOOKUP")]
    [NotTournamentScoped]
    public async Task<ActionResult<ApiResponse<ClubPlayerLookupDto>>> Lookup(int clubId, [FromBody] PlayerLookupRequest request) =>
        Respond(await _clubs.LookupPlayerAsync(clubId, request));

    // POST api/clubs/4/players — { name, jerseyNumber, position, email, phone, cricket }
    [HttpPost("api/clubs/{clubId}/players")]
    [ServiceRequestId("CLUB_PLAYER_ADD")]
    [NotTournamentScoped]
    public async Task<ActionResult<ApiResponse<ClubDto>>> AddPlayer(int clubId, [FromBody] ClubPlayerRequest request) =>
        Respond(await _clubs.AddPlayerAsync(clubId, request));

    // PUT api/clubs/4/players/12
    [HttpPut("api/clubs/{clubId}/players/{clubPlayerId}")]
    [ServiceRequestId("CLUB_PLAYER_UPDATE")]
    [NotTournamentScoped]
    public async Task<ActionResult<ApiResponse<ClubDto>>> UpdatePlayer(int clubId, int clubPlayerId, [FromBody] ClubPlayerRequest request) =>
        Respond(await _clubs.UpdatePlayerAsync(clubId, clubPlayerId, request));

    // DELETE api/clubs/4/players/12 — an admin, or the player leaving
    [HttpDelete("api/clubs/{clubId}/players/{clubPlayerId}")]
    [ServiceRequestId("CLUB_PLAYER_REMOVE")]
    [NotTournamentScoped]
    public async Task<ActionResult<ApiResponse<ClubDto>>> RemovePlayer(int clubId, int clubPlayerId) =>
        Respond(await _clubs.RemovePlayerAsync(clubId, clubPlayerId));

    // POST api/clubs/4/admins — { clubPlayerId } | { contact }
    [HttpPost("api/clubs/{clubId}/admins")]
    [ServiceRequestId("CLUB_ADMIN_ADD")]
    [NotTournamentScoped]
    public async Task<ActionResult<ApiResponse<ClubDto>>> AddAdmin(int clubId, [FromBody] AddClubAdminRequest request) =>
        Respond(await _clubs.AddAdminAsync(clubId, request));

    // DELETE api/clubs/4/admins/7 — an admin taking another off, or stepping down
    [HttpDelete("api/clubs/{clubId}/admins/{userId}")]
    [ServiceRequestId("CLUB_ADMIN_REMOVE")]
    [NotTournamentScoped]
    public async Task<ActionResult<ApiResponse<ClubDto>>> RemoveAdmin(int clubId, int userId) =>
        Respond(await _clubs.RemoveAdminAsync(clubId, userId));

    // POST api/clubs/join/preview — { code } (8 characters)
    [HttpPost("api/clubs/join/preview")]
    [ServiceRequestId("CLUB_JOIN_PREVIEW")]
    [NotTournamentScoped]
    public async Task<ActionResult<ApiResponse<ClubJoinPreviewDto>>> JoinPreview([FromBody] JoinTeamPreviewRequest request) =>
        Respond(await _clubs.JoinPreviewAsync(request));

    // POST api/clubs/join — { code, clubPlayerId } (null: join as new)
    [HttpPost("api/clubs/join")]
    [ServiceRequestId("CLUB_JOIN")]
    [NotTournamentScoped]
    public async Task<ActionResult<ApiResponse<ClubJoinResult>>> Join([FromBody] ClubJoinRequest request) => Respond(await _clubs.JoinAsync(request));

    // GET api/clubs/4/players/12/squads — the live tournaments a player could join (the "joined" notification's sheet)
    [HttpGet("api/clubs/{clubId}/players/{clubPlayerId}/squads")]
    [ServiceRequestId("CLUB_SQUADS_PREVIEW")]
    [NotTournamentScoped]
    public async Task<ActionResult<ApiResponse<ClubSquadQuestionDto>>> SquadsPreview(int clubId, int clubPlayerId) =>
        Respond(await _clubs.SquadsPreviewAsync(clubId, clubPlayerId));

    // POST api/clubs/4/squads — { clubPlayerId, teamIds } to add to live squads, or { playerIds } to leave places out
    [HttpPost("api/clubs/{clubId}/squads")]
    [ServiceRequestId("CLUB_SQUADS_APPLY")]
    [NotTournamentScoped]
    public async Task<ActionResult<ApiResponse<ClubDto>>> SquadsApply(int clubId, [FromBody] ClubSquadsApplyRequest request) =>
        Respond(await _clubs.SquadsApplyAsync(clubId, request));

    // POST api/clubs/entries/preview — { clubId, code } | { clubId, entryId }: the tournament and who can play
    [HttpPost("api/clubs/entries/preview")]
    [ServiceRequestId("ENTRY_PREVIEW")]
    [NotTournamentScoped]
    public async Task<ActionResult<ApiResponse<EntryPreviewDto>>> EntryPreview([FromBody] EntryPreviewRequest request) =>
        Respond(await _entries.PreviewAsync(request));

    // POST api/clubs/entries — { clubId, code, teamName, clubPlayerIds }
    [HttpPost("api/clubs/entries")]
    [ServiceRequestId("ENTRY_REQUEST")]
    [NotTournamentScoped]
    public async Task<ActionResult<ApiResponse<EntryPreviewDto>>> EntryRequest([FromBody] EntryRequest request) =>
        Respond(await _entries.RequestAsync(request));

    // PUT api/clubs/entries/3 — { teamName, clubPlayerIds }, while it waits
    [HttpPut("api/clubs/entries/{entryId}")]
    [ServiceRequestId("ENTRY_UPDATE")]
    [NotTournamentScoped]
    public async Task<ActionResult<ApiResponse<EntryPreviewDto>>> EntryUpdate(int entryId, [FromBody] EntryUpdateRequest request) =>
        Respond(await _entries.UpdateAsync(entryId, request));

    // DELETE api/clubs/entries/3 — withdraw it, while it waits
    [HttpDelete("api/clubs/entries/{entryId}")]
    [ServiceRequestId("ENTRY_WITHDRAW")]
    [NotTournamentScoped]
    public async Task<ActionResult<ApiResponse<bool>>> EntryWithdraw(int entryId) => Respond(await _entries.WithdrawAsync(entryId));

    // GET api/tournaments/5/entries — the code and the entries waiting (owners)
    [HttpGet("api/tournaments/{id}/entries")]
    [ServiceRequestId("TOURNAMENT_ENTRY_LIST")]
    [TournamentAccess(TournamentPermission.Edit)]
    public async Task<ActionResult<ApiResponse<TournamentEntriesDto>>> EntryList(int id) => Respond(await _entries.ListAsync(id));

    // PUT api/tournaments/5/entries/code — { open }: "Teams can enter with a code"
    [HttpPut("api/tournaments/{id}/entries/code")]
    [ServiceRequestId("TOURNAMENT_ENTRY_CODE")]
    [TournamentAccess(TournamentPermission.Edit)]
    public async Task<ActionResult<ApiResponse<TournamentEntriesDto>>> EntryCode(int id, [FromBody] EntryCodeRequest request) =>
        Respond(await _entries.SetCodeAsync(id, request));

    // POST api/tournaments/5/entries/3/accept — makes the team
    [HttpPost("api/tournaments/{id}/entries/{entryId}/accept")]
    [ServiceRequestId("TOURNAMENT_ENTRY_ACCEPT")]
    [TournamentAccess(TournamentPermission.Edit)]
    public async Task<ActionResult<ApiResponse<TournamentEntriesDto>>> EntryAccept(int id, int entryId) =>
        Respond(await _entries.AcceptAsync(id, entryId));

    // POST api/tournaments/5/entries/3/decline
    [HttpPost("api/tournaments/{id}/entries/{entryId}/decline")]
    [ServiceRequestId("TOURNAMENT_ENTRY_DECLINE")]
    [TournamentAccess(TournamentPermission.Edit)]
    public async Task<ActionResult<ApiResponse<TournamentEntriesDto>>> EntryDecline(int id, int entryId) =>
        Respond(await _entries.DeclineAsync(id, entryId));

    // GET api/public/clubs/ABCD2345 — the signed-out team page (C-15): names and results only, like the public tournament pages
    [HttpGet("api/public/clubs/{code}")]
    [ServiceRequestId("PUBLIC_CLUB")]
    [AllowAnonymousService]
    [NotTournamentScoped]
    [EnableRateLimiting(PublicRateLimit.Policy)]
    public async Task<ActionResult<ApiResponse<JsonNode>>> PublicClub(string code) => Respond(await _clubs.PublicAsync(code));

    // POST api/tournaments/5/teams/9/keep — "Keep this team" (C-23): the team's own admins only, checked by the service
    [HttpPost("api/tournaments/{id}/teams/{teamId}/keep")]
    [ServiceRequestId("CLUB_FROM_TEAM")]
    [TournamentAccess(TournamentPermission.View)]
    public async Task<ActionResult<ApiResponse<ClubDto>>> KeepTeam(int id, int teamId) => Respond(await _clubs.KeepTeamAsync(id, teamId));
}
