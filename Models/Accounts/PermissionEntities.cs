namespace TournamentScheduler.Api.Models.Accounts;

// Role permissions (phase 2 of docs/accounts-and-access-plan.md): which kinds of account may call
// which services at all. Whether a user may touch a particular tournament is a separate, later check.

/// <summary>
/// One callable service, kept in step with the code by the permission sync at start-up — the
/// <c>ServiceRequestId</c> is the key, so renaming a method never loses its grants.
///
/// Two different "off" switches, owned by two different parties:
/// <see cref="AuditedEntity.IsDisabled"/> is the admin's (nobody may call it, though it still
/// exists); <see cref="IsObsolete"/> is the sync's (it is no longer in the code). Neither ever
/// changes the other's, so a deploy can't switch back on what an admin turned off.
/// </summary>
public class Permission : AuditedEntity
{
    public int PermissionId { get; set; }
    public string ServiceRequestId { get; set; } = string.Empty;

    // Descriptive only, refreshed by every sync: what the admin portal shows.
    public string ControllerName { get; set; } = string.Empty;
    public string ActionName { get; set; } = string.Empty;
    public string HttpMethod { get; set; } = string.Empty;

    /// <summary>The action's route template, e.g. <c>api/tournaments/{id}/teams</c>.</summary>
    public string URL { get; set; } = string.Empty;

    /// <summary>Callable signed out (sign-in itself, the health check). Role ticks don't apply.</summary>
    public bool IsAnonymous { get; set; }

    /// <summary>An admin-portal service: can never be granted to EndUser, whatever is ticked.</summary>
    public bool IsAdminOnly { get; set; }

    /// <summary>Can't be switched off, and Admin always keeps it — so nobody can lock everyone out.</summary>
    public bool IsProtected { get; set; }

    /// <summary>Set by the sync when the service is gone from the code; cleared if it comes back.</summary>
    public bool IsObsolete { get; set; }
}

/// <summary>
/// A tick: <see cref="RoleId"/> may call <see cref="PermissionId"/>. Unticking sets
/// <see cref="AuditedEntity.IsDisabled"/> rather than deleting the row, so the history stays and a
/// deploy can tell "never decided" (no row) from "an admin said no" (a disabled row).
/// </summary>
public class RolePermission : AuditedEntity
{
    public int RolePermissionId { get; set; }
    public int RoleId { get; set; }
    public int PermissionId { get; set; }
}
