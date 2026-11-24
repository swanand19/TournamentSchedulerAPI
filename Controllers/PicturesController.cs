using Microsoft.AspNetCore.Mvc;
using TournamentScheduler.Api.Auth.Permissions;
using TournamentScheduler.Api.Auth.TournamentAccess;
using TournamentScheduler.Api.Gateway;
using TournamentScheduler.Api.Http;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Services.Pictures;

namespace TournamentScheduler.Api.Controllers;

/// <summary>
/// Putting pictures in (HLD P-26): your own profile photo, and a tournament's logo and banner (owners). They
/// arrive sealed through the gateway like every other request; they're shown through the signed links the
/// answers carry (<c>/api/pictures/{id}</c>, served outside the gateway).
/// </summary>
[ApiController]
[DefaultRoles(RoleIds.EndUser)] // every signed-up user; Admin is ticked too
public class PicturesController : ApiController
{
    private readonly IPictureService _pictures;

    public PicturesController(IPictureService pictures)
    {
        _pictures = pictures;
    }

    // PUT api/account/photo — { image: base64 JPEG or PNG }
    [HttpPut("api/account/photo")]
    [ServiceRequestId("ACCOUNT_PHOTO_SET")]
    [NotTournamentScoped]
    public async Task<ActionResult<ApiResponse<PictureDto>>> SetPhoto([FromBody] SetPictureRequest request) =>
        Respond(await _pictures.SetMyPhotoAsync(request));

    // DELETE api/account/photo
    [HttpDelete("api/account/photo")]
    [ServiceRequestId("ACCOUNT_PHOTO_REMOVE")]
    [NotTournamentScoped]
    public async Task<ActionResult<ApiResponse<PictureDto>>> RemovePhoto() =>
        Respond(await _pictures.RemoveMyPhotoAsync());

    // PUT api/tournaments/5/pictures/logo|banner — { image }
    [HttpPut("api/tournaments/{id}/pictures/{kind}")]
    [ServiceRequestId("TOURNAMENT_PICTURE_SET")]
    [TournamentAccess(TournamentPermission.Edit)]
    public async Task<ActionResult<ApiResponse<PictureDto>>> SetTournamentPicture(int id, string kind, [FromBody] SetPictureRequest request) =>
        Respond(await _pictures.SetTournamentPictureAsync(id, kind, request));

    // DELETE api/tournaments/5/pictures/logo|banner
    [HttpDelete("api/tournaments/{id}/pictures/{kind}")]
    [ServiceRequestId("TOURNAMENT_PICTURE_REMOVE")]
    [TournamentAccess(TournamentPermission.Edit)]
    public async Task<ActionResult<ApiResponse<PictureDto>>> RemoveTournamentPicture(int id, string kind) =>
        Respond(await _pictures.RemoveTournamentPictureAsync(id, kind));
}
