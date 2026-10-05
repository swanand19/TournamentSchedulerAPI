using Microsoft.Extensions.Options;
using TournamentScheduler.Api.Auth;
using TournamentScheduler.Api.Data;
using TournamentScheduler.Api.Data.DataServices;
using TournamentScheduler.Api.Models.Accounts;

namespace TournamentScheduler.Api.Services.Accounts;

public enum SessionCheckOutcome
{
    SignedIn,

    /// <summary>No session has this token (never did, or it was a guess).</summary>
    Unknown,

    Expired,

    /// <summary>Signed out, signed out everywhere, or the account was deleted.</summary>
    Revoked,

    /// <summary>The account is blocked by an admin.</summary>
    Blocked
}

public sealed record SessionCheck(SessionCheckOutcome Outcome, int UserId = 0, int RoleId = 0, string Name = "", Guid SessionId = default)
{
    public bool IsSignedIn => Outcome == SessionCheckOutcome.SignedIn;
}

public interface ISessionService
{
    /// <summary>Who a token belongs to, checked on every request. Moves the session's expiry forward now and then.</summary>
    Task<SessionCheck> CheckAsync(string token);

    /// <summary>A new session for <paramref name="userId"/>: the token (given to the app once) and the stored row.</summary>
    (string Token, UserSession Session) Start(int userId, ClientInfo client);
}

/// <summary>
/// Sessions: one per signed-in device, found by the fingerprint of the token the app sends. Checking
/// the table on every request is what makes signing out, blocking and deleting take effect at once.
/// </summary>
public class SessionService : ServiceBase, ISessionService
{
    private readonly IDataService<UserSession> _sessions;
    private readonly IUnitOfWork _unitOfWork;
    private readonly AuthOptions _options;
    private readonly TimeProvider _clock;

    public SessionService(IDataService<UserSession> sessions, IUnitOfWork unitOfWork, IOptions<AuthOptions> options, TimeProvider clock)
    {
        _sessions = sessions;
        _unitOfWork = unitOfWork;
        _options = options.Value;
        _clock = clock;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    private TimeSpan Lifetime(string? channel) =>
        TimeSpan.FromDays(string.Equals(channel, "WEB", StringComparison.OrdinalIgnoreCase) ? _options.WebSessionDays : _options.SessionDays);

    public (string Token, UserSession Session) Start(int userId, ClientInfo client)
    {
        var (token, hash) = SessionTokens.NewToken();
        var now = Now;
        var session = new UserSession
        {
            SessionId = Guid.NewGuid(),
            UserId = userId,
            TokenHash = hash,
            Channel = Trim(client.Channel, 30),
            DeviceId = Trim(client.DeviceId, 100),
            Crd = now,
            LastSeenAt = now,
            ExpiresAt = now + Lifetime(client.Channel)
        };
        _sessions.Add(session);
        return (token, session);
    }

    public async Task<SessionCheck> CheckAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 200) return new SessionCheck(SessionCheckOutcome.Unknown);

        var hash = SessionTokens.Hash(token);
        var found = await _sessions.QueryFirstOrDefaultAsync(q => q
            .Where(s => s.TokenHash == hash)
            .Select(s => new
            {
                s.SessionId,
                s.UserId,
                s.Channel,
                s.LastSeenAt,
                s.ExpiresAt,
                s.RevokedAt,
                s.User!.Name,
                s.User.RoleId,
                s.User.IsDisabled,
                s.User.DeletedOn
            }), tracking: false);

        var now = Now;
        if (found == null) return new SessionCheck(SessionCheckOutcome.Unknown);
        if (found.RevokedAt != null || found.DeletedOn != null) return new SessionCheck(SessionCheckOutcome.Revoked);
        if (found.ExpiresAt <= now) return new SessionCheck(SessionCheckOutcome.Expired);
        if (found.IsDisabled) return new SessionCheck(SessionCheckOutcome.Blocked);

        if (now - found.LastSeenAt >= TimeSpan.FromMinutes(_options.SessionTouchMinutes))
        {
            var session = await _sessions.GetByIdAsync(found.SessionId);
            if (session != null)
            {
                session.LastSeenAt = now;
                session.ExpiresAt = now + Lifetime(session.Channel);
                await _unitOfWork.SaveChangesAsync();
            }
        }

        return new SessionCheck(SessionCheckOutcome.SignedIn, found.UserId, found.RoleId, found.Name, found.SessionId);
    }

    private static string? Trim(string? value, int max) => value is { Length: > 0 } ? value.Length > max ? value[..max] : value : null;
}
