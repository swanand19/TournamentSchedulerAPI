namespace TournamentScheduler.Api.Auth.Permissions;

/// <summary>
/// Which roles get this service ticked when it first appears in the Permissions table — and only
/// then. After that the ticks belong to the admin portal (or deploy/sql/grant-permission.sql until
/// it exists): a later deploy never ticks or unticks anything, so an admin's "no" survives every
/// release. Admin is always ticked for a new service as well; there is no need to list it.
///
/// On a controller it applies to every action; an action's own attribute replaces it. Every action
/// needs one (or <see cref="AllowAnonymousServiceAttribute"/>) — the architecture tests check.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class DefaultRolesAttribute(params int[] roleIds) : Attribute
{
    public IReadOnlyList<int> RoleIds { get; } = roleIds;
}

/// <summary>
/// An admin-portal service. It can never be granted to EndUser (the role everyone who signs up
/// gets) — the check ignores such a tick — so one wrong click in the role editor can't hand the
/// user list to every user. Other roles get it only if ticked.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class AdminOnlyAttribute : Attribute;

/// <summary>
/// A service nobody may switch off, because switching it off would lock people out: signing out,
/// seeing your own account, and (later) the portal's own permission and role screens. It ignores
/// <c>IsDisabled</c>; an ordinary one is open to every signed-in user, an admin-only one always to
/// Admin.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class ProtectedServiceAttribute : Attribute;
