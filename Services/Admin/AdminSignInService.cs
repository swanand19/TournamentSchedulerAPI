using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using TournamentScheduler.Api.Auth;
using TournamentScheduler.Api.Auth.Permissions;
using TournamentScheduler.Api.Data;
using TournamentScheduler.Api.Data.DataServices;
using TournamentScheduler.Api.Gateway;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Services.Accounts;

namespace TournamentScheduler.Api.Services.Admin;

/// <summary>Which portal (admin-only) services a role may use: Admin always has the protected ones; the rest follow the ticks.</summary>
public sealed class PortalAccess(PermissionCache cache, ServiceRegistry registry)
{
    public async Task<List<string>> ServicesForAsync(int roleId)
    {
        var snapshot = await cache.GetAsync();
        return registry.All
            .Where(s => s.IsAdminOnly)
            .Where(s => (s.IsProtected && roleId == RoleIds.Admin)
                        || (roleId != RoleIds.EndUser
                            && snapshot?.Services.TryGetValue(s.Id, out var entry) == true
                            && entry.RoleIds.Contains(roleId)
                            && (s.IsProtected || !entry.IsDisabled && !entry.IsObsolete)))
            .Select(s => s.Id)
            .Order(StringComparer.Ordinal)
            .ToList();
    }
}

public interface IAdminSignInService
{
    Task<ServiceResult<AdminSignInResponse>> SignInAsync(AdminSignInRequest request, ClientInfo client);
    Task<ServiceResult<AdminMeDto>> MeAsync();
}

/// <summary>
/// Signing in to the admin portal: the emailed code (sent by <c>AUTH_OTP_SEND</c>, like the apps)
/// and then a password — two factors, because the portal can block people and change what everyone
/// may do. Only accounts whose role has at least one portal service get in. The first time, the
/// person chooses their password here (the code proves the email is theirs).
///
/// Five wrong passwords lock the sign-in for 15 minutes. The code stays usable while the password
/// is retried, so a typo doesn't cost a new email. The session it makes is a portal session
/// (12 hours, sliding) — the only kind that can call admin-only services.
/// </summary>
public class AdminSignInService : ServiceBase, IAdminSignInService
{
    public const int PasswordMinLength = 10;
    public const int PasswordMaxLength = 128;

    private static readonly PasswordHasher<User> Hasher = new();

    private readonly IDataService<User> _users;
    private readonly IDataService<Role> _roles;
    private readonly IOtpCodeService _codes;
    private readonly ISessionService _sessions;
    private readonly PortalAccess _portal;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly AuthOptions _options;
    private readonly TimeProvider _clock;

    public AdminSignInService(
        IDataService<User> users,
        IDataService<Role> roles,
        IOtpCodeService codes,
        ISessionService sessions,
        PortalAccess portal,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        IOptions<AuthOptions> options,
        TimeProvider clock)
    {
        _users = users;
        _roles = roles;
        _codes = codes;
        _sessions = sessions;
        _portal = portal;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _options = options.Value;
        _clock = clock;
    }

    private const string NoAccess = "You don't have access to the admin portal.";

    public async Task<ServiceResult<AdminSignInResponse>> SignInAsync(AdminSignInRequest request, ClientInfo client)
    {
        var email = AccountService.NormaliseEmail(request.Email);
        if (email == null) return BadRequest("Enter a valid email address.");

        var (challenge, wrong) = await _codes.CheckAsync(new Contact(ContactKind.Email, email), OtpPurpose.SignIn, request.Otp);
        if (wrong is { } error) return error;

        var now = _clock.GetUtcNow().UtcDateTime;
        var user = await _users.FirstOrDefaultAsync(u => u.EmailId == email && u.DeletedOn == null);
        if (user == null || user.IsDisabled || (await _portal.ServicesForAsync(user.RoleId)).Count == 0)
        {
            // The code was right, so it's spent; the answer is the same whatever the reason.
            challenge!.ConsumedAt = now;
            await _unitOfWork.SaveChangesAsync();
            return user is { IsDisabled: true } ? Forbidden("This account has been blocked.") : Forbidden(NoAccess);
        }

        if (user.PasswordHash == null)
        {
            if (string.IsNullOrEmpty(request.NewPassword))
                return Success(new AdminSignInResponse { Outcome = AdminSignInOutcome.NeedsNewPassword });
            if (PasswordProblem(request.NewPassword, email) is { } problem)
                return BadRequest(problem);

            user.PasswordHash = Hasher.HashPassword(user, request.NewPassword);
            user.PasswordSetAt = now;
        }
        else
        {
            if (user.PasswordLockedUntil > now)
                return TooManyRequests(Locked(user.PasswordLockedUntil.Value - now));
            if (string.IsNullOrEmpty(request.Password))
                return Success(new AdminSignInResponse { Outcome = AdminSignInOutcome.NeedsPassword });

            var verdict = Hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
            if (verdict == PasswordVerificationResult.Failed)
            {
                user.FailedPasswordCount++;
                var locks = user.FailedPasswordCount >= _options.PasswordMaxAttempts;
                if (locks)
                {
                    user.PasswordLockedUntil = now.AddMinutes(_options.PasswordLockMinutes);
                    user.FailedPasswordCount = 0;
                }
                await _unitOfWork.SaveChangesAsync();
                return locks ? TooManyRequests(Locked(TimeSpan.FromMinutes(_options.PasswordLockMinutes))) : BadRequest("That password isn't right.");
            }
            if (verdict == PasswordVerificationResult.SuccessRehashNeeded)
                user.PasswordHash = Hasher.HashPassword(user, request.Password);
        }

        user.FailedPasswordCount = 0;
        user.PasswordLockedUntil = null;
        user.LastLoginAt = now;
        user.EmailVerifiedAt ??= now;
        challenge!.ConsumedAt = now;

        var (token, session) = _sessions.Start(user.UserId, client, portal: true);
        await _unitOfWork.SaveChangesAsync();

        return Success(new AdminSignInResponse
        {
            Outcome = AdminSignInOutcome.SignedIn,
            SessionToken = token,
            SessionId = session.SessionId,
            ExpiresAt = session.ExpiresAt,
            Me = await MeOf(user)
        });
    }

    public async Task<ServiceResult<AdminMeDto>> MeAsync()
    {
        var user = await _users.GetByIdAsync(_currentUser.UserId);
        if (user == null) return NotFound("Account not found.");
        return Success(await MeOf(user));
    }

    private async Task<AdminMeDto> MeOf(User user) => new()
    {
        UserId = user.UserId,
        Name = user.Name,
        Email = user.EmailId,
        RoleId = user.RoleId,
        RoleName = await _roles.QueryFirstOrDefaultAsync(q => q.Where(r => r.RoleId == user.RoleId).Select(r => r.RoleName), tracking: false),
        Services = await _portal.ServicesForAsync(user.RoleId)
    };

    private static string Locked(TimeSpan wait) =>
        $"Too many wrong passwords. Try again in {Math.Max(1, (int)Math.Ceiling(wait.TotalMinutes))} minutes.";

    public static string? PasswordProblem(string password, string email)
    {
        if (password.Length < PasswordMinLength) return $"Choose a password of at least {PasswordMinLength} characters.";
        if (password.Length > PasswordMaxLength) return $"A password can be at most {PasswordMaxLength} characters.";
        if (string.Equals(password.Trim(), email, StringComparison.OrdinalIgnoreCase)) return "Your password can't be your email address.";
        return null;
    }
}
