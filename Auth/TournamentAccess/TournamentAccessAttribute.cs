using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Services.Access;

namespace TournamentScheduler.Api.Auth.TournamentAccess;

/// <summary>
/// What an action needs inside its tournament, checked before it runs by
/// <see cref="TournamentAccessFilter"/> (rules in <see cref="TournamentAccessService"/>):
/// <code>[TournamentAccess(TournamentPermission.Score, From = TournamentScope.FootballMatch)]</code>
/// Every action has this or <see cref="NotTournamentScopedAttribute"/> — the architecture tests
/// check — so no endpoint can forget to ask.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class TournamentAccessAttribute(TournamentPermission permission) : Attribute
{
    public TournamentPermission Permission { get; } = permission;

    /// <summary>Where the tournament comes from. Default: the route's <c>id</c>.</summary>
    public TournamentScope From { get; init; } = TournamentScope.Tournament;

    /// <summary>For the scorer-lock actions themselves (asking to score must work while someone else scores).</summary>
    public bool IgnoresScorerLock { get; init; }
}

/// <summary>
/// An action that touches no stored tournament: signing in, the account, creating a tournament,
/// listing your own, and the stateless schedule helpers (which compute from what they're sent).
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class NotTournamentScopedAttribute : Attribute;
