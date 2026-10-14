using System.Net.Mail;
using Microsoft.Extensions.Options;
using TournamentScheduler.Api.Auth;
using TournamentScheduler.Api.Data;
using TournamentScheduler.Api.Data.DataServices;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Models.Cricket;
using TournamentScheduler.Api.Services.Access;

namespace TournamentScheduler.Api.Services.Accounts;

public interface IAccountService
{
    AuthOptionsDto GetOptions();
    Task<ServiceResult<SendOtpResponse>> SendOtpAsync(SendOtpRequest request, ClientInfo client);
    Task<ServiceResult<VerifyOtpResponse>> VerifyOtpAsync(VerifyOtpRequest request, ClientInfo client);
    Task<ServiceResult<bool>> LogoutAsync();
    Task<ServiceResult<LogoutAllResponse>> LogoutAllAsync();
    Task<ServiceResult<AccountDto>> GetAccountAsync();
    Task<ServiceResult<AccountDto>> UpdateAccountAsync(UpdateAccountRequest request);
    Task<ServiceResult<SendOtpResponse>> SendContactCodeAsync(ContactCodeRequest request, ClientInfo client);
    Task<ServiceResult<AccountDto>> VerifyContactAsync(VerifyContactRequest request);
    Task<ServiceResult<AccountDto>> RemoveContactAsync(RemoveContactRequest request);
}

/// <summary>
/// Signing in with a one-time code, and the signed-in person's own account.
///
/// Signing up and signing in are one flow: send a code to an email or mobile number, then prove
/// it. A known one is signed in; a new one is asked for a name (the same code, sent again with it)
/// and then signed in. Asking for a code answers the same whether or not there is an account, so
/// the app can't be used to find out who uses it. Signing in also links any squad places an
/// organiser gave this email or number, and the person's name and cricket profile are copied to
/// every place linked to them.
///
/// An account has an email, a mobile number or both (phase 6B), each proved with a code; the last
/// one can't be removed, and an admin keeps their email for the portal.
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
    private readonly SmsOptions _sms;
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
        IOptions<SmsOptions> sms,
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
        _sms = sms.Value;
        _clock = clock;
        _logger = logger;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public const int NameMinLength = 2;
    public const int NameMaxLength = 100;

    public const string PhoneOff = "Mobile numbers can't be used yet. Use your email address.";

    public AuthOptionsDto GetOptions() => new() { Phone = _sms.IsEnabled, DefaultCountryCode = Contacts.DefaultCountryCode };

    // -----------------------------------------------------------------
    // Step 1: send a code
    // -----------------------------------------------------------------

    public async Task<ServiceResult<SendOtpResponse>> SendOtpAsync(SendOtpRequest request, ClientInfo client)
    {
        var (read, problem) = Read(request.Contact, request.Email);
        if (read is not { } to) return BadRequest(problem!);
        return await _codes.SendAsync(to, OtpPurpose.SignIn, client);
    }

    // -----------------------------------------------------------------
    // Step 2: prove the code (and, for a new email or number, give a name)
    // -----------------------------------------------------------------

    public async Task<ServiceResult<VerifyOtpResponse>> VerifyOtpAsync(VerifyOtpRequest request, ClientInfo client)
    {
        var (read, problem) = Read(request.Contact, request.Email);
        if (read is not { } to) return BadRequest(problem!);

        var (challenge, wrong) = await _codes.CheckAsync(to, OtpPurpose.SignIn, request.Otp);
        if (wrong is { } error) return error;

        var now = Now;
        var user = await FindByAsync(to);
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
                // Right code, new email or number: the app asks for a name and sends the same code again.
                return Success(new VerifyOtpResponse { Outcome = VerifyOtpOutcome.NeedsName, IsNewUser = true });
            }
            if (name.Length < NameMinLength || name.Length > NameMaxLength)
                return BadRequest($"Your name must be {NameMinLength} to {NameMaxLength} characters.");

            user = new User { Name = name, RoleId = RoleIds.EndUser };
            SetContact(user, to, now);
            user.Created(now, SystemUser.Id);
            _users.Add(user);
            try
            {
                await _unitOfWork.SaveChangesAsync();
            }
            catch (DuplicateEntryException)
            {
                return Conflict($"An account with this {Noun(to)} was created a moment ago. Ask for a new code to sign in.");
            }

            // Created by themselves, now that there is an id to say so with.
            user.CrdBy = user.LmdBy = user.UserId;
        }

        if (to.IsEmail) user.EmailVerifiedAt ??= now;
        else user.PhoneVerifiedAt ??= now;
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
    /// Squad places an organiser gave this email or number before the account had it. Done after
    /// the change is saved: a clash here (another request linking the same tournament first) must
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
    // Email and mobile number
    // -----------------------------------------------------------------

    /// <summary>
    /// Sends a code to an email or number the person wants on their account (added, or replacing
    /// the one they have). Unlike sign-in this says when another account has it: the person is
    /// signed in, the send limits apply, and otherwise they'd prove a code only to be refused.
    /// </summary>
    public async Task<ServiceResult<SendOtpResponse>> SendContactCodeAsync(ContactCodeRequest request, ClientInfo client)
    {
        var (read, problem) = Read(request.Contact, null);
        if (read is not { } to) return BadRequest(problem!);

        var user = await _users.GetByIdAsync(_currentUser.UserId);
        if (user == null) return NotFound("Account not found.");
        if (TakenProblem(user, to, await FindByAsync(to)) is { } taken) return taken;

        return await _codes.SendAsync(to, OtpPurpose.AddContact, client);
    }

    public async Task<ServiceResult<AccountDto>> VerifyContactAsync(VerifyContactRequest request)
    {
        var (read, problem) = Read(request.Contact, null);
        if (read is not { } to) return BadRequest(problem!);

        var user = await _users.GetByIdAsync(_currentUser.UserId);
        if (user == null) return NotFound("Account not found.");

        var (challenge, wrong) = await _codes.CheckAsync(to, OtpPurpose.AddContact, request.Otp);
        if (wrong is { } error) return error;

        // Checked again: another account may have taken it since the code was sent.
        if (TakenProblem(user, to, await FindByAsync(to)) is { } taken) return taken;

        var now = Now;
        SetContact(user, to, now);
        user.Modified(now, user.UserId);
        challenge!.ConsumedAt = now;
        await _links.FollowAccountAsync(user, null);
        try
        {
            await _unitOfWork.SaveChangesAsync();
        }
        catch (DuplicateEntryException)
        {
            return Conflict($"This {Noun(to)} was added to another account a moment ago.");
        }

        await LinkWaitingPlaces(user);
        return Success(await ToDto(user));
    }

    public async Task<ServiceResult<AccountDto>> RemoveContactAsync(RemoveContactRequest request)
    {
        var user = await _users.GetByIdAsync(_currentUser.UserId);
        if (user == null) return NotFound("Account not found.");

        if (request.Kind == ContactKind.Email)
        {
            if (user.EmailId == null) return BadRequest("Your account has no email to remove.");
            if (user.RoleId == RoleIds.Admin) return Conflict("Admins keep an email: the admin portal sends its sign-in codes there.");
            if (user.PhoneNumber == null) return Conflict("Add a mobile number first. Your account needs an email or a mobile number to sign in with.");
            user.EmailId = null;
            user.EmailVerifiedAt = null;
        }
        else
        {
            if (user.PhoneNumber == null) return BadRequest("Your account has no mobile number to remove.");
            if (user.EmailId == null) return Conflict("Add an email first. Your account needs an email or a mobile number to sign in with.");
            user.PhoneNumber = null;
            user.PhoneVerifiedAt = null;
        }

        user.Modified(Now, user.UserId);
        await _links.FollowAccountAsync(user, null);
        await _unitOfWork.SaveChangesAsync();
        return Success(await ToDto(user));
    }

    // -----------------------------------------------------------------

    /// <summary>
    /// The contact from the one field (or the portal's older <c>email</c> field, which takes only
    /// an email), refused when it's a mobile number and texts aren't set up.
    /// </summary>
    private (Contact? Contact, string? Problem) Read(string? contact, string? legacyEmail)
    {
        if (string.IsNullOrWhiteSpace(contact) && legacyEmail != null)
        {
            var email = NormaliseEmail(legacyEmail);
            return email == null ? (null, "Enter a valid email address.") : (new Contact(ContactKind.Email, email), null);
        }

        var (to, problem) = Contacts.Parse(contact);
        if (to is { IsEmail: false } && !_sms.IsEnabled) return (null, PhoneOff);
        return (to, problem);
    }

    private Task<User?> FindByAsync(Contact to) =>
        to.IsEmail
            ? _users.FirstOrDefaultAsync(u => u.EmailId == to.Value)
            : _users.FirstOrDefaultAsync(u => u.PhoneNumber == to.Value);

    private ServiceError? TakenProblem(User me, Contact to, User? holder)
    {
        if (holder == null) return null;
        return holder.UserId == me.UserId
            ? BadRequest($"This is already your {Noun(to)}.")
            : Conflict($"This {Noun(to)} is used by another account.");
    }

    private static void SetContact(User user, Contact to, DateTime now)
    {
        if (to.IsEmail)
        {
            user.EmailId = to.Value;
            user.EmailVerifiedAt = now;
        }
        else
        {
            user.PhoneNumber = to.Value;
            user.PhoneVerifiedAt = now;
        }
    }

    private static string Noun(Contact to) => to.IsEmail ? "email" : "mobile number";

    private async Task<AccountDto> ToDto(User user)
    {
        var roleName = await _roles.QueryFirstOrDefaultAsync(q => q.Where(r => r.RoleId == user.RoleId).Select(r => r.RoleName), tracking: false);
        return new AccountDto
        {
            UserId = user.UserId,
            Name = user.Name,
            Email = user.EmailId,
            Phone = user.PhoneNumber,
            RoleId = user.RoleId,
            RoleName = roleName,
            MemberSince = user.Crd,
            Cricket = await _profiles.GetByIdAsync(user.UserId),
            CanDelete = user.RoleId != RoleIds.Admin,
            CanRemoveEmail = user.EmailId != null && user.PhoneNumber != null && user.RoleId != RoleIds.Admin,
            CanRemovePhone = user.PhoneNumber != null && user.EmailId != null
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
