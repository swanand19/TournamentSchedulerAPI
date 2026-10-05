using Microsoft.AspNetCore.Mvc;
using TournamentScheduler.Api.Auth.TournamentAccess;
using TournamentScheduler.Api.Auth.Permissions;
using TournamentScheduler.Api.Gateway;
using TournamentScheduler.Api.Http;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Services.Accounts;

namespace TournamentScheduler.Api.Controllers;

/// <summary>The signed-in person's own account.</summary>
[ApiController]
[Route("api/account")]
[DefaultRoles(RoleIds.EndUser)] // every signed-up user; Admin is ticked too
[NotTournamentScoped]
public class AccountController : ApiController
{
    private readonly IAccountService _accounts;
    private readonly IAccountDeletionService _deletion;

    public AccountController(IAccountService accounts, IAccountDeletionService deletion)
    {
        _accounts = accounts;
        _deletion = deletion;
    }

    // GET api/account
    [HttpGet]
    [ServiceRequestId("ACCOUNT_GET")]
    [ProtectedService] // the Account screen (and its Sign out) must always open
    public async Task<ActionResult<ApiResponse<AccountDto>>> Get() =>
        Respond(await _accounts.GetAccountAsync());

    // PUT api/account — { name, cricket? }
    [HttpPut]
    [ServiceRequestId("ACCOUNT_UPDATE")]
    public async Task<ActionResult<ApiResponse<AccountDto>>> Update([FromBody] UpdateAccountRequest request) =>
        Respond(await _accounts.UpdateAccountAsync(request));

    // POST api/account/delete/preview — what deleting would do; emails the code that confirms it
    [HttpPost("delete/preview")]
    [ServiceRequestId("ACCOUNT_DELETE_PREVIEW")]
    public async Task<ActionResult<ApiResponse<AccountDeletionPreviewDto>>> DeletePreview() =>
        Respond(await _deletion.PreviewAsync(Client));

    // POST api/account/delete — { otp }
    [HttpPost("delete")]
    [ServiceRequestId("ACCOUNT_DELETE")]
    public async Task<ActionResult<ApiResponse<object>>> Delete([FromBody] DeleteAccountRequest request) =>
        Respond(await _deletion.DeleteAsync(request));
}
