using Microsoft.AspNetCore.Mvc;
using TournamentScheduler.Api.Auth.TournamentAccess;
using TournamentScheduler.Api.Auth;
using TournamentScheduler.Api.Auth.Permissions;
using TournamentScheduler.Api.Gateway;
using TournamentScheduler.Api.Http;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Services.Accounts;

namespace TournamentScheduler.Api.Controllers;

/// <summary>
/// Signing in and out. The two sign-in steps are the only services (with the health check) a
/// signed-out app can call. They still go through the encrypted gateway like everything else.
/// </summary>
[ApiController]
[Route("api/auth")]
[DefaultRoles(RoleIds.EndUser)] // every signed-up user; Admin is ticked too
[NotTournamentScoped]
public class AuthController : ApiController
{
    private readonly IAccountService _accounts;

    public AuthController(IAccountService accounts)
    {
        _accounts = accounts;
    }

    // POST api/auth/otp/send — emails a code; the same answer whether or not the email has an account
    [HttpPost("otp/send")]
    [ServiceRequestId("AUTH_OTP_SEND")]
    [AllowAnonymousService]
    public async Task<ActionResult<ApiResponse<SendOtpResponse>>> SendOtp([FromBody] SendOtpRequest request) =>
        Respond(await _accounts.SendOtpAsync(request, Client));

    // POST api/auth/otp/verify — signs in (or, for a new email with a name, signs up)
    [HttpPost("otp/verify")]
    [ServiceRequestId("AUTH_OTP_VERIFY")]
    [AllowAnonymousService]
    public async Task<ActionResult<ApiResponse<VerifyOtpResponse>>> VerifyOtp([FromBody] VerifyOtpRequest request) =>
        Respond(await _accounts.VerifyOtpAsync(request, Client));

    // POST api/auth/logout — this device only
    [HttpPost("logout")]
    [ServiceRequestId("AUTH_LOGOUT")]
    [ProtectedService] // signing out must always work
    public async Task<ActionResult<ApiResponse<object>>> Logout() =>
        Respond(await _accounts.LogoutAsync(), _ => Ok());

    // POST api/auth/logout-all — every device this account is signed in on, this one included
    [HttpPost("logout-all")]
    [ServiceRequestId("AUTH_LOGOUT_ALL")]
    [ProtectedService] // the answer to a lost phone must always work
    public async Task<ActionResult<ApiResponse<LogoutAllResponse>>> LogoutAll() =>
        Respond(await _accounts.LogoutAllAsync());
}
