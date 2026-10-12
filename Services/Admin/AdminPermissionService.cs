using TournamentScheduler.Api.Auth;
using TournamentScheduler.Api.Auth.Permissions;
using TournamentScheduler.Api.Data;
using TournamentScheduler.Api.Data.DataServices;
using TournamentScheduler.Api.Gateway;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Services.Accounts;

namespace TournamentScheduler.Api.Services.Admin;

public interface IAdminPermissionService
{
    Task<ServiceResult<List<AdminPermissionDto>>> ListPermissionsAsync();
    Task<ServiceResult<object>> SyncAsync();
    Task<ServiceResult<AdminPermissionDto>> SetDisabledAsync(int permissionId, bool disabled);

    Task<ServiceResult<List<AdminRoleDto>>> ListRolesAsync();
    Task<ServiceResult<AdminRoleDto>> CreateRoleAsync(SaveRoleRequest request);
    Task<ServiceResult<AdminRoleDto>> UpdateRoleAsync(int roleId, SaveRoleRequest request);
    Task<ServiceResult<bool>> DeleteRoleAsync(int roleId);
    Task<ServiceResult<List<RolePermissionDto>>> GetRolePermissionsAsync(int roleId);
    Task<ServiceResult<List<RolePermissionDto>>> SaveRolePermissionsAsync(int roleId, SaveRolePermissionsRequest request);
}

/// <summary>
/// The portal's permission and role screens (docs/accounts-and-access-plan.md §4). The guards, so
/// nobody can lock everyone out by mistake:
/// <list type="bullet">
/// <item>Sign-in, health and the protected services can't be switched off.</item>
/// <item>Admin always keeps the protected ones; ticks on anonymous services mean nothing.</item>
/// <item>Portal (admin-only) services can never be ticked for EndUser, the role every sign-up gets.</item>
/// <item>Admin and EndUser can't be renamed or deleted (the code relies on their ids); a role
/// people still have can't be deleted.</item>
/// </list>
/// Every change makes the request check reload at once rather than within the minute.
/// </summary>
public class AdminPermissionService : ServiceBase, IAdminPermissionService
{
    private readonly IDataService<Permission> _permissions;
    private readonly IDataService<RolePermission> _ticks;
    private readonly IDataService<Role> _roles;
    private readonly IDataService<User> _users;
    private readonly IPermissionService _sync;
    private readonly ServiceRegistry _registry;
    private readonly PermissionCache _cache;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;

    public AdminPermissionService(
        IDataService<Permission> permissions,
        IDataService<RolePermission> ticks,
        IDataService<Role> roles,
        IDataService<User> users,
        IPermissionService sync,
        ServiceRegistry registry,
        PermissionCache cache,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        TimeProvider clock)
    {
        _permissions = permissions;
        _ticks = ticks;
        _roles = roles;
        _users = users;
        _sync = sync;
        _registry = registry;
        _cache = cache;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    private static bool IsBuiltIn(int roleId) => roleId is RoleIds.Admin or RoleIds.EndUser;

    // -----------------------------------------------------------------
    // Permissions
    // -----------------------------------------------------------------

    public async Task<ServiceResult<List<AdminPermissionDto>>> ListPermissionsAsync()
    {
        var rows = await _permissions.ListAsync(shape: q => q.OrderBy(p => p.ControllerName).ThenBy(p => p.ServiceRequestId), tracking: false);
        var ticks = await TicksByPermission();
        return Success(rows.Select(p => ToDto(p, ticks)).ToList());
    }

    /// <summary>The same sync the API runs at start-up — for after a deploy, without a restart.</summary>
    public async Task<ServiceResult<object>> SyncAsync()
    {
        var result = await _sync.SyncAsync(_registry.All);
        _cache.Invalidate();
        return Ok(new { result.Added, result.Refreshed, result.Retired, result.Returned, Message = $"Permissions synced with the code: {result}." });
    }

    public async Task<ServiceResult<AdminPermissionDto>> SetDisabledAsync(int permissionId, bool disabled)
    {
        var row = await _permissions.GetByIdAsync(permissionId);
        if (row == null) return NotFound("That service isn't in the list any more. Reload it.");
        if (disabled && (row.IsAnonymous || row.IsProtected))
            return BadRequest($"{row.ServiceRequestId} can't be switched off: it would lock people out.");

        if (row.IsDisabled != disabled)
        {
            row.IsDisabled = disabled;
            row.Modified(Now, _currentUser.UserId);
            await _unitOfWork.SaveChangesAsync();
            _cache.Invalidate();
        }
        return Success(ToDto(row, await TicksByPermission()));
    }

    private async Task<Dictionary<int, List<int>>> TicksByPermission() =>
        (await _ticks.QueryAsync(q => q.Where(t => !t.IsDisabled).Select(t => new { t.PermissionId, t.RoleId }), tracking: false))
        .GroupBy(t => t.PermissionId)
        .ToDictionary(g => g.Key, g => g.Select(t => t.RoleId).Order().ToList());

    private static AdminPermissionDto ToDto(Permission p, Dictionary<int, List<int>> ticks) => new()
    {
        PermissionId = p.PermissionId,
        ServiceRequestId = p.ServiceRequestId,
        ControllerName = p.ControllerName,
        ActionName = p.ActionName,
        HttpMethod = p.HttpMethod,
        Url = p.URL,
        IsAnonymous = p.IsAnonymous,
        IsAdminOnly = p.IsAdminOnly,
        IsProtected = p.IsProtected,
        IsObsolete = p.IsObsolete,
        IsDisabled = p.IsDisabled,
        RoleIds = ticks.GetValueOrDefault(p.PermissionId) ?? [],
        CanDisable = !p.IsAnonymous && !p.IsProtected
    };

    // -----------------------------------------------------------------
    // Roles
    // -----------------------------------------------------------------

    public async Task<ServiceResult<List<AdminRoleDto>>> ListRolesAsync()
    {
        var roles = await _roles.ListAsync(shape: q => q.OrderBy(r => r.RoleId), tracking: false);
        var users = (await _users.QueryAsync(q => q
                .Where(u => u.DeletedOn == null && u.UserId != SystemUser.Id)
                .GroupBy(u => u.RoleId)
                .Select(g => new { RoleId = g.Key, Count = g.Count() }), tracking: false))
            .ToDictionary(x => x.RoleId, x => x.Count);
        var adminOnly = (await _permissions.QueryAsync(q => q.Where(p => p.IsAdminOnly && !p.IsObsolete).Select(p => p.PermissionId), tracking: false)).ToHashSet();
        var ticks = await _ticks.QueryAsync(q => q.Where(t => !t.IsDisabled).Select(t => new { t.RoleId, t.PermissionId }), tracking: false);

        return Success(roles.Select(r => new AdminRoleDto
        {
            RoleId = r.RoleId,
            Name = r.RoleName,
            Description = r.Description,
            IsBuiltIn = IsBuiltIn(r.RoleId),
            Users = users.GetValueOrDefault(r.RoleId),
            Ticks = ticks.Count(t => t.RoleId == r.RoleId),
            PortalAccess = r.RoleId == RoleIds.Admin
                           || r.RoleId != RoleIds.EndUser && ticks.Any(t => t.RoleId == r.RoleId && adminOnly.Contains(t.PermissionId))
        }).ToList());
    }

    public async Task<ServiceResult<AdminRoleDto>> CreateRoleAsync(SaveRoleRequest request)
    {
        if (RoleProblem(request) is { } problem) return BadRequest(problem);
        var name = request.Name.Trim();
        if (await _roles.AnyAsync(r => r.RoleName == name)) return Conflict($"There's already a role called {name}.");

        var role = new Role { RoleName = name, Description = Clean(request.Description) };
        role.Created(Now, _currentUser.UserId);
        _roles.Add(role);
        try
        {
            await _unitOfWork.SaveChangesAsync();
        }
        catch (DuplicateEntryException)
        {
            return Conflict($"There's already a role called {name}.");
        }
        return await RoleResult(role.RoleId);
    }

    public async Task<ServiceResult<AdminRoleDto>> UpdateRoleAsync(int roleId, SaveRoleRequest request)
    {
        var role = await _roles.GetByIdAsync(roleId);
        if (role == null) return NotFound("Role not found.");
        if (RoleProblem(request) is { } problem) return BadRequest(problem);

        var name = request.Name.Trim();
        if (IsBuiltIn(roleId) && name != role.RoleName)
            return BadRequest($"{role.RoleName} is built in, so it can't be renamed. Its description can change.");
        if (await _roles.AnyAsync(r => r.RoleName == name && r.RoleId != roleId))
            return Conflict($"There's already a role called {name}.");

        role.RoleName = name;
        role.Description = Clean(request.Description);
        role.Modified(Now, _currentUser.UserId);
        await _unitOfWork.SaveChangesAsync();
        return await RoleResult(roleId);
    }

    public async Task<ServiceResult<bool>> DeleteRoleAsync(int roleId)
    {
        var role = await _roles.GetByIdAsync(roleId);
        if (role == null) return NotFound("Role not found.");
        if (IsBuiltIn(roleId)) return BadRequest($"{role.RoleName} is built in, so it can't be deleted.");

        var holders = await _users.CountAsync(u => u.RoleId == roleId && u.DeletedOn == null);
        if (holders > 0)
            return Conflict($"{holders} {(holders == 1 ? "person has" : "people have")} this role. Give them another role first.");

        _ticks.RemoveRange(await _ticks.ListAsync(t => t.RoleId == roleId));
        _roles.Remove(role);
        await _unitOfWork.SaveChangesAsync();
        _cache.Invalidate();
        return Success(true);
    }

    private async Task<ServiceResult<AdminRoleDto>> RoleResult(int roleId)
    {
        var all = await ListRolesAsync();
        return Success(all.Value!.Single(r => r.RoleId == roleId));
    }

    private static string? RoleProblem(SaveRoleRequest request)
    {
        var name = request.Name?.Trim() ?? "";
        if (name.Length is < 2 or > 100) return "A role's name must be 2 to 100 characters.";
        if (request.Description?.Trim().Length > 500) return "A description can be at most 500 characters.";
        return null;
    }

    private static string? Clean(string? description) => string.IsNullOrWhiteSpace(description) ? null : description.Trim();

    // -----------------------------------------------------------------
    // The role editor
    // -----------------------------------------------------------------

    public async Task<ServiceResult<List<RolePermissionDto>>> GetRolePermissionsAsync(int roleId)
    {
        if (!await _roles.AnyAsync(r => r.RoleId == roleId)) return NotFound("Role not found.");
        return Success(await Editor(roleId));
    }

    /// <summary>
    /// Saves the whole set: what's listed is ticked, every other changeable box is unticked (the row
    /// kept, disabled — so a later deploy knows an admin said no). Locked boxes keep their state.
    /// </summary>
    public async Task<ServiceResult<List<RolePermissionDto>>> SaveRolePermissionsAsync(int roleId, SaveRolePermissionsRequest request)
    {
        if (!await _roles.AnyAsync(r => r.RoleId == roleId)) return NotFound("Role not found.");

        var permissions = await _permissions.ListAsync(tracking: false);
        var wanted = (request.PermissionIds ?? []).ToHashSet();
        if (wanted.Any(id => permissions.All(p => p.PermissionId != id)))
            return BadRequest("Some of those services aren't in the list any more. Reload and try again.");

        if (roleId == RoleIds.EndUser)
        {
            var portal = permissions.Where(p => p.IsAdminOnly && wanted.Contains(p.PermissionId)).Select(p => p.ServiceRequestId).ToList();
            if (portal.Count > 0)
                return BadRequest($"Portal services can't be given to EndUser, the role everyone who signs up gets: {string.Join(", ", portal)}.");
        }

        var existing = (await _ticks.ListAsync(t => t.RoleId == roleId)).ToDictionary(t => t.PermissionId);
        var now = Now;
        var me = _currentUser.UserId;
        foreach (var p in permissions)
        {
            if (LockedReason(p, roleId) != null) continue;

            var want = wanted.Contains(p.PermissionId);
            if (existing.TryGetValue(p.PermissionId, out var tick))
            {
                if (tick.IsDisabled == !want) continue;
                tick.IsDisabled = !want;
                tick.Modified(now, me);
            }
            else if (want)
            {
                tick = new RolePermission { RoleId = roleId, PermissionId = p.PermissionId };
                tick.Created(now, me);
                _ticks.Add(tick);
            }
        }

        await _unitOfWork.SaveChangesAsync();
        _cache.Invalidate();
        return Success(await Editor(roleId));
    }

    private async Task<List<RolePermissionDto>> Editor(int roleId)
    {
        var permissions = await _permissions.ListAsync(shape: q => q.OrderBy(p => p.ControllerName).ThenBy(p => p.ServiceRequestId), tracking: false);
        var ticked = (await _ticks.QueryAsync(q => q.Where(t => t.RoleId == roleId && !t.IsDisabled).Select(t => t.PermissionId), tracking: false)).ToHashSet();

        return permissions.Select(p =>
        {
            var locked = LockedReason(p, roleId);
            return new RolePermissionDto
            {
                PermissionId = p.PermissionId,
                ServiceRequestId = p.ServiceRequestId,
                ControllerName = p.ControllerName,
                ActionName = p.ActionName,
                HttpMethod = p.HttpMethod,
                Url = p.URL,
                IsAdminOnly = p.IsAdminOnly,
                IsObsolete = p.IsObsolete,
                IsDisabled = p.IsDisabled,
                // A locked box shows what actually applies, whatever row is (or isn't) there.
                Ticked = locked switch
                {
                    null => ticked.Contains(p.PermissionId),
                    _ => !(roleId == RoleIds.EndUser && p.IsAdminOnly)
                },
                Locked = locked
            };
        }).ToList();
    }

    /// <summary>Why this box can't change for this role, or null when it can.</summary>
    private static string? LockedReason(Permission p, int roleId) => p switch
    {
        { IsAnonymous: true } => "Open to everyone, signed in or not",
        { IsProtected: true, IsAdminOnly: false } => "Every signed-in person keeps this",
        { IsProtected: true } when roleId == RoleIds.Admin => "Admin always keeps this",
        { IsAdminOnly: true } when roleId == RoleIds.EndUser => "Portal services are never given to EndUser",
        _ => null
    };
}
