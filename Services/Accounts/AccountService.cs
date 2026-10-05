using System.Net.Mail;
using TournamentScheduler.Api.Auth;
using TournamentScheduler.Api.Data;
using TournamentScheduler.Api.Data.DataServices;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Models.Cricket;
using TournamentScheduler.Api.Services.Access;

namespace TournamentScheduler.Api.Services.Accounts;

public interface IAccountService
{
    Task<ServiceResult<SendOtpResponse>> SendOtpAsync(SendOtpRequest request, ClientInfo client);
    Task<ServiceResult<VerifyOtpResponse>> VerifyOtpAsync(VerifyOtpRequest request, ClientInfo client);
    Task<ServiceResult<bool>> LogoutAsync();
    Task<ServiceResult<LogoutAllResponse>> LogoutAllAsync();
    Task<ServiceResult<AccountDto>> GetAccountAsync();
    Task<ServiceResult<AccountDto>> UpdateAccountAsync(UpdateAccountRequest request);
}

/// <summary>
/// Signing in with an emailed code, and the signed-in person's own account.
///
/// Signing up and signing in are one flow: send a code to an email, then prove it. A known email is
/// signed in; a new one is asked for a name (the same code, sent again with it) and then signed in.
/// Asking for a code answers the same whether or not the email has an account, so the app can't be
/// used to find out who uses it. Signing in also links any squad places an organiser gave this
/// email, and the person's name and cricket profile are copied to every place linked to them.
/// </summary>
public class AccountService : ServiceBase, IAccountService
{
    private readonly IDataService<User> _users;
    private readonly IDataService<UserSession> _sessions;
    private readonly IDataService<Role> _roles;
    private readonly IDataService<UserCricketProfile> _profiles;
    private readonly ISessionService _sessionService;
    private readonly IOtpCodeService _codes;
    private readonly IPlayerLinkService _links;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;
    private readonly ILogger<AccountService> _logger;

    public AccountService(
        IDataService<User> users,
        IDataService<UserSession> sessions,
        IDataService<Role> roles,
        IDataService<UserCricketProfile> profiles,
        ISessionService sessionService,
        IOtpCodeService codes,
        IPlayerLinkService links,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        TimeProvider clock,
        ILogger<AccountService> logger)
    {
        _users = users;
        _sessions = sessions;
        _roles = roles;
        _profiles = profiles;
        _sessionService = sessionService;
        _codes = codes;
        _links = links;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
        _logger = logger;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public const int NameMinLength = 2;
    public const int NameMaxLength = 100;

    // -----------------------------------------------------------------
    // Step 1: send a code
    // -----------------------------------------------------------------

    public async Task<ServiceResult<SendOtpResponse>> SendOtpAsync(SendOtpRequest request, ClientInfo client)
    {
        var email = NormaliseEmail(request.Email);
        if (email == null) return BadRequest("Enter a valid email address.");
        return await _codes.SendAsync(email, OtpPurpose.SignIn, client);
    }

    // -----------------------------------------------------------------
    // Step 2: prove the code (and, for a new email, give a name)
    // -----------------------------------------------------------------

    public async Task<ServiceResult<VerifyOtpResponse>> VerifyOtpAsync(VerifyOtpRequest request, ClientInfo client)
    {
        var email = NormaliseEmail(request.Email);
        if (email == null) return BadRequest("Enter a valid email address.");

        var (challenge, wrong) = await _codes.CheckAsync(email, OtpPurpose.SignIn, request.Otp);
        if (wrong is { } error) return error;

        var now = Now;
        var user = await _users.FirstOrDefaultAsync(u => u.EmailId == email);
        var isNew = user == null;

        if (user is { IsDisabled: true })
        {
            challenge!.ConsumedAt = now;
            await _unitOfWork.SaveChangesAsync();
            return Forbidden("This account has been blocked.");
        }

        if (user == null)
        {
            var name = request.Name?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                // Right code, new email: the app asks for a name and sends the same code again.
                return Success(new VerifyOtpResponse { Outcome = VerifyOtpOutcome.NeedsName, IsNewUser = true });
            }
            if (name.Length < NameMinLength || name.Length > NameMaxLength)
                return BadRequest($"Your name must be {NameMinLength} to {NameMaxLength} characters.");

            user = new User { Name = name, EmailId = email, RoleId = RoleIds.EndUser, EmailVerifiedAt = now };
            user.Created(now, SystemUser.Id);
            _users.Add(user);
            try
            {
                await _unitOfWork.SaveChangesAsync();
            }
            catch (DuplicateEntryException)
            {
                return Conflict("An account with this email was created a moment ago. Ask for a new code to sign in.");
            }

            // Created by themselves, now that there is an id to say so with.
            user.CrdBy = user.LmdBy = user.UserId;
        }

        user.EmailVerifiedAt ??= now;
        user.LastLoginAt = now;
        challenge!.ConsumedAt = now;

        var (token, session) = _sessionService.Start(user.UserId, client);
        await _unitOfWork.SaveChangesAsync();

        await LinkWaitingPlaces(user);

        return Success(new VerifyOtpResponse
        {
            Outcome = VerifyOtpOutcome.SignedIn,
            IsNewUser = isNew,
            SessionToken = token,
            SessionId = session.SessionId,
            ExpiresAt = session.ExpiresAt,
            User = await ToDto(user)
        });
    }

    /// <summary>
    /// Squad places an organiser gave this email before the account existed. Done after the
    /// sign-in is saved: a clash here (another request linking the same tournament first) must
    /// never stop someone signing in.
    /// </summary>
    private async Task LinkWaitingPlaces(User user)
    {
        try
        {
            if (await _links.LinkWaitingPlacesAsync(user) > 0)
                await _unitOfWork.SaveChangesAsync();
        }
        catch (DuplicateEntryException e)
        {
            _logger.LogWarning(e, "Couldn't link waiting squad places for user {UserId}; they'll link at the next sign-in.", user.UserId);
        }
    }

    // -----------------------------------------------------------------
    // Signed in
    // -----------------------------------------------------------------

    public async Task<ServiceResult<bool>> LogoutAsync()
    {
        var session = await _sessions.GetByIdAsync(_currentUser.SessionId);
        if (session != null && session.RevokedAt == null)
        {
            session.RevokedAt = Now;
            session.RevokedReason = "SignedOut";
            await _unitOfWork.SaveChangesAsync();
        }
        return Success(true);
    }

    public async Task<ServiceResult<LogoutAllResponse>> LogoutAllAsync()
    {
        var now = Now;
        var userId = _currentUser.UserId;
        var active = await _sessions.ListAsync(s => s.UserId == userId && s.RevokedAt == null && s.ExpiresAt > now);
        foreach (var session in active)
        {
            session.RevokedAt = now;
            session.RevokedReason = "SignedOutEverywhere";
        }
        await _unitOfWork.SaveChangesAsync();
        return Success(new LogoutAllResponse { SignedOutDevices = active.Count });
    }

    public async Task<ServiceResult<AccountDto>> GetAccountAsync()
    {
        var user = await _users.GetByIdAsync(_currentUser.UserId);
        if (user == null) return NotFound("Account not found.");
        return Success(await ToDto(user));
    }

    /// <summary>Name and cricket profile. Every squad place linked to them follows.</summary>
    public async Task<ServiceResult<AccountDto>> UpdateAccountAsync(UpdateAccountRequest request)
    {
        var name = request.Name?.Trim() ?? "";
        if (name.Length < NameMinLength || name.Length > NameMaxLength)
            return BadRequest($"Your name must be {NameMinLength} to {NameMaxLength} characters.");
        if (CricketProfiles.Problem(request.Cricket) is { } cricketError)
            return BadRequest(cricketError);

        var user = await _users.GetByIdAsync(_currentUser.UserId);
        if (user == null) return NotFound("Account not found.");

        user.Name = name;
        user.Modified(Now, user.UserId);

        var profile = await _profiles.GetByIdAsync(user.UserId);
        if (request.Cricket != null)
        {
            if (profile == null)
            {
                profile = new UserCricketProfile { UserId = user.UserId };
                _profiles.Add(profile);
            }
            CricketProfiles.Copy(request.Cricket, profile);
        }

        await _links.FollowAccountAsync(user, request.Cricket != null ? profile : null);
        await _unitOfWork.SaveChangesAsync();
        return Success(await ToDto(user));
    }

    // -----------------------------------------------------------------

    private async Task<AccountDto> ToDto(User user)
    {
        var roleName = await _roles.QueryFirstOrDefaultAsync(q => q.Where(r => r.RoleId == user.RoleId).Select(r => r.RoleName), tracking: false);
        return new AccountDto
        {
            UserId = user.UserId,
            Name = user.Name,
            Email = user.EmailId,
            RoleId = user.RoleId,
            RoleName = roleName,
            MemberSince = user.Crd,
            Cricket = await _profiles.GetByIdAsync(user.UserId),
            CanDelete = user.RoleId != RoleIds.Admin
        };
    }

    /// <summary>Trimmed and lower-case, or null when it isn't a plausible address.</summary>
    public static string? NormaliseEmail(string? value)
    {
        var email = value?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(email) || email.Length > 256 || email.Contains(' ')) return null;

        // MailAddress accepts "Name <a@b>" and bare "a@b"; only the bare form, with a dotted domain, is an email here.
        if (!MailAddress.TryCreate(email, out var parsed) || parsed.Address != email) return null;
        var domain = email[(email.LastIndexOf('@') + 1)..];
        return domain.Contains('.') && !domain.StartsWith('.') && !domain.EndsWith('.') ? email : null;
    }
}
