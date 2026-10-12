using TournamentScheduler.Api.Logging;

namespace TournamentScheduler.Api.Auth;

/// <summary>
/// Who is making this request. Services take this instead of reading the HTTP request, so the same
/// service code works from any caller — and a test can say who is calling.
/// </summary>
public interface ICurrentUser
{
    bool IsSignedIn { get; }

    /// <summary>The signed-in user. Throws when nobody is signed in — only anonymous services run without one.</summary>
    int UserId { get; }

    int RoleId { get; }
    string Name { get; }

    /// <summary>The device's session, e.g. to sign just this device out.</summary>
    Guid SessionId { get; }

    /// <summary>Signed in to the admin portal (code and password) — needed for every admin-only service.</summary>
    bool IsPortal { get; }
}

/// <summary>Filled in by <see cref="SessionAuthenticationMiddleware"/> once per request.</summary>
public sealed class CurrentUser : ICurrentUser
{
    private int? _userId;

    public bool IsSignedIn => _userId.HasValue;

    public int UserId => _userId ?? throw new InvalidOperationException("Nobody is signed in on this request.");

    public int RoleId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public Guid SessionId { get; private set; }
    public bool IsPortal { get; private set; }

    public void SignIn(int userId, int roleId, string name, Guid sessionId, bool isPortal = false)
    {
        _userId = userId;
        RoleId = roleId;
        Name = name;
        SessionId = sessionId;
        IsPortal = isPortal;
    }
}

/// <summary>
/// Where a request came from, as its gateway header says: which app, which install, which address.
/// Controllers hand it to services that record it (sessions, code limits).
/// </summary>
public sealed record ClientInfo(string? Channel, string? DeviceId, string? Ip)
{
    public static ClientInfo From(HttpContext context)
    {
        var log = RequestLogContext.Of(context);
        return new ClientInfo(log?.Channel, log?.DeviceId, context.Connection.RemoteIpAddress?.ToString());
    }

    public bool IsWeb => string.Equals(Channel, "WEB", StringComparison.OrdinalIgnoreCase);
}
