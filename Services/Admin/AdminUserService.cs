using TournamentScheduler.Api.Auth;
using TournamentScheduler.Api.Data;
using TournamentScheduler.Api.Data.DataServices;
using TournamentScheduler.Api.Models.Accounts;

namespace TournamentScheduler.Api.Services.Admin;

public interface IAdminUserService
{
    Task<ServiceResult<PagedResult<AdminUserDto>>> ListAsync(string? search, int? roleId, int? page, int? pageSize);
    Task<ServiceResult<AdminUserDto>> SetBlockedAsync(int userId, bool blocked);
    Task<ServiceResult<AdminUserDto>> SetRoleAsync(int userId, int roleId);
    Task<ServiceResult<AdminUserDto>> SignOutEverywhereAsync(int userId);
    Task<ServiceResult<AdminUserDto>> ResetPasswordAsync(int userId);
}

/// <summary>
/// The portal's people: find them, block or unblock, change their role, sign them out of every
/// device, and clear a forgotten portal password (they choose a new one at their next portal
/// sign-in). Blocking signs them out at once. Nobody can block themselves or change their own role
/// — that's how the last admin would lock everyone out — and the System account and deleted
/// accounts can't be changed.
/// </summary>
public class AdminUserService : ServiceBase, IAdminUserService
{
    private readonly IDataService<User> _users;
    private readonly IDataService<Role> _roles;
    private readonly IDataService<UserSession> _sessions;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;

    public AdminUserService(
        IDataService<User> users,
        IDataService<Role> roles,
        IDataService<UserSession> sessions,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        TimeProvider clock)
    {
        _users = users;
        _roles = roles;
        _sessions = sessions;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    /// <summary>Everyone but System and deleted accounts, newest first; <paramref name="search"/> matches name, email or mobile number.</summary>
    public async Task<ServiceResult<PagedResult<AdminUserDto>>> ListAsync(string? search, int? roleId, int? page, int? pageSize)
    {
        var (p, size) = PagedResult<AdminUserDto>.Clamp(page, pageSize);
        var term = search?.Trim().ToLowerInvariant();
        var digits = new string((term ?? "").Where(char.IsAsciiDigit).ToArray()); // "98765 43210" finds +919876543210

        IQueryable<User> Filter(IQueryable<User> q)
        {
            q = q.Where(u => u.DeletedOn == null && u.UserId != SystemUser.Id);
            if (!string.IsNullOrEmpty(term))
                q = q.Where(u => u.Name.ToLower().Contains(term) || (u.EmailId != null && u.EmailId.Contains(term))
                                 || (digits.Length >= 3 && u.PhoneNumber != null && u.PhoneNumber.Contains(digits)));
            if (roleId.HasValue)
                q = q.Where(u => u.RoleId == roleId);
            return q;
        }

        var total = (await _users.QueryAsync(q => Filter(q).Select(u => u.UserId), tracking: false)).Count;
        var ids = await _users.QueryAsync(q => Filter(q)
            .OrderByDescending(u => u.Crd).ThenByDescending(u => u.UserId)
            .Skip((p - 1) * size).Take(size)
            .Select(u => u.UserId), tracking: false);

        return Success(new PagedResult<AdminUserDto> { Items = await Rows(ids), Total = total, Page = p, PageSize = size });
    }

    public async Task<ServiceResult<AdminUserDto>> SetBlockedAsync(int userId, bool blocked)
    {
        var (user, error) = await Changeable(userId, "block");
        if (error is { } e) return e;

        user!.IsDisabled = blocked;
        user.Modified(Now, _currentUser.UserId);
        if (blocked) await Revoke(userId, "Blocked");
        await _unitOfWork.SaveChangesAsync();
        return await Row(userId);
    }

    public async Task<ServiceResult<AdminUserDto>> SetRoleAsync(int userId, int roleId)
    {
        var (user, error) = await Changeable(userId, "change the role of");
        if (error is { } e) return e;
        if (!await _roles.AnyAsync(r => r.RoleId == roleId)) return BadRequest("That role doesn't exist.");

        if (user!.RoleId != roleId)
        {
            user.RoleId = roleId;
            user.Modified(Now, _currentUser.UserId);
            await _unitOfWork.SaveChangesAsync();
        }
        return await Row(userId);
    }

    public async Task<ServiceResult<AdminUserDto>> SignOutEverywhereAsync(int userId)
    {
        var user = await _users.GetByIdAsync(userId);
        if (user == null || user.DeletedOn != null || userId == SystemUser.Id) return NotFound("Person not found.");

        await Revoke(userId, "SignedOutByAdmin");
        await _unitOfWork.SaveChangesAsync();
        return await Row(userId);
    }

    /// <summary>A forgotten portal password: cleared, and their portal sessions ended. They choose a new one at the next portal sign-in (with an emailed code).</summary>
    public async Task<ServiceResult<AdminUserDto>> ResetPasswordAsync(int userId)
    {
        var user = await _users.GetByIdAsync(userId);
        if (user == null || user.DeletedOn != null || userId == SystemUser.Id) return NotFound("Person not found.");
        if (user.PasswordHash == null) return Conflict($"{user.Name} hasn't set a portal password.");

        user.PasswordHash = null;
        user.PasswordSetAt = null;
        user.FailedPasswordCount = 0;
        user.PasswordLockedUntil = null;
        user.Modified(Now, _currentUser.UserId);
        await Revoke(userId, "PasswordReset", portalOnly: true);
        await _unitOfWork.SaveChangesAsync();
        return await Row(userId);
    }

    private async Task<(User? User, ServiceError? Error)> Changeable(int userId, string what)
    {
        var user = await _users.GetByIdAsync(userId);
        if (user == null || user.DeletedOn != null || userId == SystemUser.Id) return (null, NotFound("Person not found."));
        if (userId == _currentUser.UserId) return (null, BadRequest($"You can't {what} yourself."));
        return (user, null);
    }

    private async Task Revoke(int userId, string reason, bool portalOnly = false)
    {
        var now = Now;
        foreach (var session in await _sessions.ListAsync(s => s.UserId == userId && s.RevokedAt == null && s.ExpiresAt > now && (!portalOnly || s.IsPortal)))
        {
            session.RevokedAt = now;
            session.RevokedReason = reason;
        }
    }

    private async Task<ServiceResult<AdminUserDto>> Row(int userId) => Success((await Rows([userId])).Single());

    private async Task<List<AdminUserDto>> Rows(List<int> ids)
    {
        var now = Now;
        var users = await _users.QueryAsync(q => q
            .Where(u => ids.Contains(u.UserId))
            .Select(u => new
            {
                u.UserId, u.Name, u.EmailId, u.PhoneNumber, u.RoleId, RoleName = u.Role!.RoleName, u.IsDisabled, u.Crd, u.LastLoginAt,
                HasPassword = u.PasswordHash != null
            }), tracking: false);
        var sessions = (await _sessions.QueryAsync(q => q
                .Where(s => ids.Contains(s.UserId) && s.RevokedAt == null && s.ExpiresAt > now)
                .GroupBy(s => s.UserId)
                .Select(g => new { UserId = g.Key, Count = g.Count() }), tracking: false))
            .ToDictionary(x => x.UserId, x => x.Count);

        var me = _currentUser.UserId;
        return ids.Select(id => users.First(u => u.UserId == id)).Select(u => new AdminUserDto
        {
            UserId = u.UserId,
            Name = u.Name,
            Email = u.EmailId,
            Phone = u.PhoneNumber,
            RoleId = u.RoleId,
            RoleName = u.RoleName,
            IsBlocked = u.IsDisabled,
            MemberSince = u.Crd,
            LastLoginAt = u.LastLoginAt,
            ActiveSessions = sessions.GetValueOrDefault(u.UserId),
            HasPassword = u.HasPassword,
            IsMe = u.UserId == me
        }).ToList();
    }
}
