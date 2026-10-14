namespace TournamentScheduler.Api.Models.Accounts;

// The admin portal (phase 5 of docs/accounts-and-access-plan.md): its sign-in (code + password),
// permissions, roles and the role editor, users, and every tournament in pages.

/// <summary>One page of a list. Pages count from 1.</summary>
public class PagedResult<T>
{
    public List<T> Items { get; set; } = [];
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }

    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public static (int Page, int PageSize) Clamp(int? page, int? pageSize) =>
        (Math.Max(1, page ?? 1), Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize));
}

// --- Signing in ------------------------------------------------------------

/// <summary>The code from <c>AUTH_OTP_SEND</c>, then the password — or a new one the first time.</summary>
public class AdminSignInRequest
{
    public string Email { get; set; } = string.Empty;
    public string Otp { get; set; } = string.Empty;
    public string? Password { get; set; }

    /// <summary>Only when the answer was <see cref="AdminSignInOutcome.NeedsNewPassword"/>.</summary>
    public string? NewPassword { get; set; }
}

public enum AdminSignInOutcome
{
    SignedIn,

    /// <summary>The code is right: now the password (send the same code again with it).</summary>
    NeedsPassword,

    /// <summary>The code is right and no password has been chosen yet: choose one.</summary>
    NeedsNewPassword
}

public class AdminSignInResponse
{
    public AdminSignInOutcome Outcome { get; set; }
    public string? SessionToken { get; set; }
    public Guid? SessionId { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public AdminMeDto? Me { get; set; }
}

/// <summary>Who is signed in to the portal and which portal services their role may use — the portal shows only those pages.</summary>
public class AdminMeDto
{
    public int UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public int RoleId { get; set; }
    public string? RoleName { get; set; }
    public List<string> Services { get; set; } = [];
}

// --- Permissions and roles -----------------------------------------------------

public class AdminPermissionDto
{
    public int PermissionId { get; set; }
    public string ServiceRequestId { get; set; } = string.Empty;
    public string ControllerName { get; set; } = string.Empty;
    public string ActionName { get; set; } = string.Empty;
    public string HttpMethod { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public bool IsAnonymous { get; set; }
    public bool IsAdminOnly { get; set; }
    public bool IsProtected { get; set; }
    public bool IsObsolete { get; set; }
    public bool IsDisabled { get; set; }

    /// <summary>Roles ticked for it.</summary>
    public List<int> RoleIds { get; set; } = [];

    /// <summary>Anonymous and protected services can't be switched off — they'd lock people out.</summary>
    public bool CanDisable { get; set; }
}

public class SetPermissionDisabledRequest
{
    public bool Disabled { get; set; }
}

public class AdminRoleDto
{
    public int RoleId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Admin and EndUser: the code relies on their ids, so they can't be renamed or deleted.</summary>
    public bool IsBuiltIn { get; set; }

    public int Users { get; set; }
    public int Ticks { get; set; }

    /// <summary>Has at least one portal service ticked: its people sign in to the portal (with a password).</summary>
    public bool PortalAccess { get; set; }
}

public class SaveRoleRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

/// <summary>One line of the role editor: a service, whether this role has it, and whether that can change.</summary>
public class RolePermissionDto
{
    public int PermissionId { get; set; }
    public string ServiceRequestId { get; set; } = string.Empty;
    public string ControllerName { get; set; } = string.Empty;
    public string ActionName { get; set; } = string.Empty;
    public string HttpMethod { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public bool IsAdminOnly { get; set; }
    public bool IsObsolete { get; set; }
    public bool IsDisabled { get; set; }
    public bool Ticked { get; set; }

    /// <summary>Why the box can't be changed ("Everyone, signed in or not", "Admin always keeps this", …); null when it can.</summary>
    public string? Locked { get; set; }
}

/// <summary>The role editor saves the whole set: these ticked, every other one not.</summary>
public class SaveRolePermissionsRequest
{
    public List<int> PermissionIds { get; set; } = [];
}

// --- Users ------------------------------------------------------------------------

public class AdminUserDto
{
    public int UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }

    /// <summary>E.164; null when the account has none.</summary>
    public string? Phone { get; set; }

    public int RoleId { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public bool IsBlocked { get; set; }
    public DateTime MemberSince { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public int ActiveSessions { get; set; }
    public bool HasPassword { get; set; }
    public bool IsMe { get; set; }
}

public class SetUserBlockedRequest
{
    public bool Blocked { get; set; }
}

public class SetUserRoleRequest
{
    public int RoleId { get; set; }
}

// --- Tournaments ----------------------------------------------------------------

/// <summary>The portal's three tournament pages.</summary>
public enum AdminTournamentView
{
    Live,
    Upcoming,

    /// <summary>Completed and cancelled.</summary>
    Past
}

public class AdminTournamentDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Sport Sport { get; set; }
    public TournamentStatus Status { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public PersonDto Creator { get; set; } = new();
    public string? CreatorEmail { get; set; }
    public int Teams { get; set; }
    public int People { get; set; }

    /// <summary>Completed or cancelled: an admin can open it again (for mistakes).</summary>
    public bool CanReopen { get; set; }
}

public class AdminTournamentPage : PagedResult<AdminTournamentDto>
{
    /// <summary>How many on each page, for the tabs.</summary>
    public Dictionary<AdminTournamentView, int> Counts { get; set; } = [];
}

/// <summary>
/// Opening a closed tournament again. If its end date has passed it would close again within the
/// hour, so a new end date is needed then.
/// </summary>
public class ReopenTournamentRequest
{
    public DateOnly? EndDate { get; set; }
}
