namespace TournamentScheduler.Api.Models.Accounts;

public class SendOtpRequest
{
    /// <summary>An email or a mobile number; the server works out which (phase 6B).</summary>
    public string? Contact { get; set; }

    /// <summary>
    /// The field before phase 6B, still sent by the admin portal: an email only. Used when
    /// <see cref="Contact"/> is empty.
    /// </summary>
    public string? Email { get; set; }
}

/// <summary>The same answer whether or not the contact has an account, so nobody can probe who uses the app.</summary>
public class SendOtpResponse
{
    /// <summary>The email or number the code went to, as the server stores it.</summary>
    public string SentTo { get; set; } = string.Empty;

    /// <summary>Email or Sms.</summary>
    public OtpChannel Channel { get; set; }

    /// <summary>The email, for the admin portal (null when the code was texted).</summary>
    public string? Email { get; set; }

    public int ExpiresInSeconds { get; set; }
    public int ResendAfterSeconds { get; set; }
}

public class VerifyOtpRequest
{
    /// <summary>The same email or number the code was sent to.</summary>
    public string? Contact { get; set; }

    /// <summary>The field before phase 6B: an email only. Used when <see cref="Contact"/> is empty.</summary>
    public string? Email { get; set; }

    public string Otp { get; set; } = string.Empty;

    /// <summary>Only for a new account; asked for after the code proves the email or number is theirs.</summary>
    public string? Name { get; set; }
}

public enum VerifyOtpOutcome
{
    /// <summary>Signed in: the session is in the response.</summary>
    SignedIn,

    /// <summary>The code is right but the email or number is new: send the same code again with a name.</summary>
    NeedsName
}

public class VerifyOtpResponse
{
    public VerifyOtpOutcome Outcome { get; set; }
    public bool IsNewUser { get; set; }

    /// <summary>Kept by the app and sent sealed in every request. Masked in the logs.</summary>
    public string? SessionToken { get; set; }

    /// <summary>Not secret: goes in the clear request header.</summary>
    public Guid? SessionId { get; set; }

    public DateTime? ExpiresAt { get; set; }
    public AccountDto? User { get; set; }
}

/// <summary>What a signed-out app needs to draw its sign-in screen.</summary>
public class AuthOptionsDto
{
    /// <summary>Codes can be texted, so the apps offer mobile numbers (sign-in, account, players). Off until SMS is set up.</summary>
    public bool Phone { get; set; }

    /// <summary>Added to a number typed without one.</summary>
    public string DefaultCountryCode { get; set; } = string.Empty;
}

public class AccountDto
{
    public int UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }

    /// <summary>E.164, e.g. "+919876543210".</summary>
    public string? Phone { get; set; }

    public int RoleId { get; set; }
    public string? RoleName { get; set; }
    public DateTime MemberSince { get; set; }

    /// <summary>Their own cricket profile, copied to every squad place linked to them. Null until they fill it in.</summary>
    public UserCricketProfile? Cricket { get; set; }

    /// <summary>Admin accounts are removed by another admin, not from the app.</summary>
    public bool CanDelete { get; set; }

    /// <summary>
    /// An account keeps at least one way to sign in, and an admin keeps their email (the portal
    /// sends its codes there).
    /// </summary>
    public bool CanRemoveEmail { get; set; }
    public bool CanRemovePhone { get; set; }
}

public class UpdateAccountRequest
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Null leaves the cricket profile as it is.</summary>
    public TournamentScheduler.Api.Models.CricketProfileInput? Cricket { get; set; }
}

/// <summary>An email or mobile number to add to the account, or to replace the one it has.</summary>
public class ContactCodeRequest
{
    public string Contact { get; set; } = string.Empty;
}

public class VerifyContactRequest
{
    public string Contact { get; set; } = string.Empty;
    public string Otp { get; set; } = string.Empty;
}

public enum ContactKind
{
    Email,
    Phone
}

public class RemoveContactRequest
{
    public ContactKind Kind { get; set; }
}

public class LogoutAllResponse
{
    public int SignedOutDevices { get; set; }
}
