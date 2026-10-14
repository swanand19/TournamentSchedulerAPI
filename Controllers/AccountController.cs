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

    // POST api/account/contact/send — { contact }: texts or emails a code to an email or number to add
    [HttpPost("contact/send")]
    [ServiceRequestId("ACCOUNT_CONTACT_SEND")]
    public async Task<ActionResult<ApiResponse<SendOtpResponse>>> SendContactCode([FromBody] ContactCodeRequest request) =>
        Respond(await _accounts.SendContactCodeAsync(request, Client));

    // POST api/account/contact/verify — { contact, otp }: adds it (replacing the one of that kind)
    [HttpPost("contact/verify")]
    [ServiceRequestId("ACCOUNT_CONTACT_VERIFY")]
    public async Task<ActionResult<ApiResponse<AccountDto>>> VerifyContact([FromBody] VerifyContactRequest request) =>
        Respond(await _accounts.VerifyContactAsync(request));

    // POST api/account/contact/remove — { kind: Email | Phone }; never the last one
    [HttpPost("contact/remove")]
    [ServiceRequestId("ACCOUNT_CONTACT_REMOVE")]
    public async Task<ActionResult<ApiResponse<AccountDto>>> RemoveContact([FromBody] RemoveContactRequest request) =>
        Respond(await _accounts.RemoveContactAsync(request));

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
