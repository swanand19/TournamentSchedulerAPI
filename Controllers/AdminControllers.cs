using Microsoft.AspNetCore.Mvc;
using TournamentScheduler.Api.Auth;
using TournamentScheduler.Api.Auth.Permissions;
using TournamentScheduler.Api.Auth.TournamentAccess;
using TournamentScheduler.Api.Gateway;
using TournamentScheduler.Api.Http;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Services.Admin;

namespace TournamentScheduler.Api.Controllers;

// The admin portal's services (phase 5). Everything but its own sign-in and "who am I" is
// [AdminOnly]: never EndUser's, and only from a portal session (code + password). The permission
// and role screens are also [ProtectedService], so Admin can never be locked out of them.
// Route parameters: {permissionId}, {roleId}, {userId}, and a tournament's {id}.

/// <summary>Signing in to the portal, and who is signed in.</summary>
[ApiController]
[Route("api/admin")]
[DefaultRoles(RoleIds.Admin)]
[NotTournamentScoped]
public class AdminAuthController : ApiController
{
    private readonly IAdminSignInService _signIn;

    public AdminAuthController(IAdminSignInService signIn)
    {
        _signIn = signIn;
    }

    // POST api/admin/auth/sign-in — { email, otp, password?, newPassword? } (the code comes from AUTH_OTP_SEND)
    [HttpPost("auth/sign-in")]
    [ServiceRequestId("ADMIN_AUTH_SIGN_IN")]
    [AllowAnonymousService]
    public async Task<ActionResult<ApiResponse<AdminSignInResponse>>> SignIn([FromBody] AdminSignInRequest request) =>
        Respond(await _signIn.SignInAsync(request, Client));

    // GET api/admin/me — the portal's pages follow `services`
    [HttpGet("me")]
    [ServiceRequestId("ADMIN_ME")]
    [ProtectedService]
    public async Task<ActionResult<ApiResponse<AdminMeDto>>> Me() =>
        Respond(await _signIn.MeAsync());
}

/// <summary>Permissions (the sync, switching services off) and roles (the role editor).</summary>
[ApiController]
[Route("api/admin")]
[DefaultRoles(RoleIds.Admin)]
[AdminOnly]
[ProtectedService]
[NotTournamentScoped]
public class AdminPermissionsController : ApiController
{
    private readonly IAdminPermissionService _permissions;

    public AdminPermissionsController(IAdminPermissionService permissions)
    {
        _permissions = permissions;
    }

    [HttpGet("permissions")]
    [ServiceRequestId("ADMIN_PERMISSION_LIST")]
    public async Task<ActionResult<ApiResponse<List<AdminPermissionDto>>>> List() =>
        Respond(await _permissions.ListPermissionsAsync());

    // POST api/admin/permissions/sync — "Update permissions" after a deploy
    [HttpPost("permissions/sync")]
    [ServiceRequestId("ADMIN_PERMISSION_SYNC")]
    public async Task<ActionResult<ApiResponse<object>>> Sync() =>
        Respond(await _permissions.SyncAsync());

    // PUT api/admin/permissions/12 — { disabled }
    [HttpPut("permissions/{permissionId}")]
    [ServiceRequestId("ADMIN_PERMISSION_SET_DISABLED")]
    public async Task<ActionResult<ApiResponse<AdminPermissionDto>>> SetDisabled(int permissionId, [FromBody] SetPermissionDisabledRequest request) =>
        Respond(await _permissions.SetDisabledAsync(permissionId, request.Disabled));

    [HttpGet("roles")]
    [ServiceRequestId("ADMIN_ROLE_LIST")]
    public async Task<ActionResult<ApiResponse<List<AdminRoleDto>>>> Roles() =>
        Respond(await _permissions.ListRolesAsync());

    // POST api/admin/roles — { name, description }
    [HttpPost("roles")]
    [ServiceRequestId("ADMIN_ROLE_CREATE")]
    public async Task<ActionResult<ApiResponse<AdminRoleDto>>> CreateRole([FromBody] SaveRoleRequest request) =>
        Respond(await _permissions.CreateRoleAsync(request));

    [HttpPut("roles/{roleId}")]
    [ServiceRequestId("ADMIN_ROLE_UPDATE")]
    public async Task<ActionResult<ApiResponse<AdminRoleDto>>> UpdateRole(int roleId, [FromBody] SaveRoleRequest request) =>
        Respond(await _permissions.UpdateRoleAsync(roleId, request));

    [HttpDelete("roles/{roleId}")]
    [ServiceRequestId("ADMIN_ROLE_DELETE")]
    public async Task<ActionResult<ApiResponse<object>>> DeleteRole(int roleId) =>
        Respond(await _permissions.DeleteRoleAsync(roleId), _ => Ok());

    // GET api/admin/roles/3/permissions — every service, ticked or not, and which boxes are locked
    [HttpGet("roles/{roleId}/permissions")]
    [ServiceRequestId("ADMIN_ROLE_PERMISSIONS")]
    public async Task<ActionResult<ApiResponse<List<RolePermissionDto>>>> RolePermissions(int roleId) =>
        Respond(await _permissions.GetRolePermissionsAsync(roleId));

    // PUT api/admin/roles/3/permissions — { permissionIds }: the whole ticked set
    [HttpPut("roles/{roleId}/permissions")]
    [ServiceRequestId("ADMIN_ROLE_PERMISSIONS_SAVE")]
    public async Task<ActionResult<ApiResponse<List<RolePermissionDto>>>> SaveRolePermissions(int roleId, [FromBody] SaveRolePermissionsRequest request) =>
        Respond(await _permissions.SaveRolePermissionsAsync(roleId, request));
}

/// <summary>People: search, block, change role, sign out of every device, reset the portal password.</summary>
[ApiController]
[Route("api/admin/users")]
[DefaultRoles(RoleIds.Admin)]
[AdminOnly]
[NotTournamentScoped]
public class AdminUsersController : ApiController
{
    private readonly IAdminUserService _users;

    public AdminUsersController(IAdminUserService users)
    {
        _users = users;
    }

    // GET api/admin/users?search=&roleId=&page=1&pageSize=20
    [HttpGet]
    [ServiceRequestId("ADMIN_USER_LIST")]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminUserDto>>>> List(string? search, int? roleId, int? page, int? pageSize) =>
        Respond(await _users.ListAsync(search, roleId, page, pageSize));

    // PUT api/admin/users/42/blocked — { blocked }
    [HttpPut("{userId}/blocked")]
    [ServiceRequestId("ADMIN_USER_SET_BLOCKED")]
    public async Task<ActionResult<ApiResponse<AdminUserDto>>> SetBlocked(int userId, [FromBody] SetUserBlockedRequest request) =>
        Respond(await _users.SetBlockedAsync(userId, request.Blocked));

    // PUT api/admin/users/42/role — { roleId }
    [HttpPut("{userId}/role")]
    [ServiceRequestId("ADMIN_USER_SET_ROLE")]
    public async Task<ActionResult<ApiResponse<AdminUserDto>>> SetRole(int userId, [FromBody] SetUserRoleRequest request) =>
        Respond(await _users.SetRoleAsync(userId, request.RoleId));

    [HttpPost("{userId}/sign-out")]
    [ServiceRequestId("ADMIN_USER_SIGN_OUT")]
    public async Task<ActionResult<ApiResponse<AdminUserDto>>> SignOutEverywhere(int userId) =>
        Respond(await _users.SignOutEverywhereAsync(userId));

    [HttpPost("{userId}/reset-password")]
    [ServiceRequestId("ADMIN_USER_RESET_PASSWORD")]
    public async Task<ActionResult<ApiResponse<AdminUserDto>>> ResetPassword(int userId) =>
        Respond(await _users.ResetPasswordAsync(userId));
}

/// <summary>Every tournament, paged: Live, Upcoming, Past — and reopening one closed by mistake.</summary>
[ApiController]
[Route("api/admin/tournaments")]
[DefaultRoles(RoleIds.Admin)]
[AdminOnly]
[NotTournamentScoped]
public class AdminTournamentsController : ApiController
{
    private readonly IAdminTournamentService _tournaments;

    public AdminTournamentsController(IAdminTournamentService tournaments)
    {
        _tournaments = tournaments;
    }

    // GET api/admin/tournaments?view=Live|Upcoming|Past&search=&page=1&pageSize=20
    [HttpGet]
    [ServiceRequestId("ADMIN_TOURNAMENT_LIST")]
    public async Task<ActionResult<ApiResponse<AdminTournamentPage>>> List(AdminTournamentView view, string? search, int? page, int? pageSize) =>
        Respond(await _tournaments.ListAsync(view, search, page, pageSize));

    // POST api/admin/tournaments/5/reopen — { endDate? }
    [HttpPost("{id}/reopen")]
    [ServiceRequestId("ADMIN_TOURNAMENT_REOPEN")]
    public async Task<ActionResult<ApiResponse<AdminTournamentDto>>> Reopen(int id, [FromBody] ReopenTournamentRequest request) =>
        Respond(await _tournaments.ReopenAsync(id, request));
}
