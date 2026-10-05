namespace TournamentScheduler.Api.Models.Accounts;

public class SendOtpRequest
{
    public string Email { get; set; } = string.Empty;
}

/// <summary>The same answer whether or not the email has an account, so nobody can probe who uses the app.</summary>
public class SendOtpResponse
{
    public string Email { get; set; } = string.Empty;
    public int ExpiresInSeconds { get; set; }
    public int ResendAfterSeconds { get; set; }
}

public class VerifyOtpRequest
{
    public string Email { get; set; } = string.Empty;
    public string Otp { get; set; } = string.Empty;

    /// <summary>Only for a new account; asked for after the code proves the email is theirs.</summary>
    public string? Name { get; set; }
}

public enum VerifyOtpOutcome
{
    /// <summary>Signed in: the session is in the response.</summary>
    SignedIn,

    /// <summary>The code is right but the email is new: send the same code again with a name.</summary>
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

public class AccountDto
{
    public int UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public int RoleId { get; set; }
    public string? RoleName { get; set; }
    public DateTime MemberSince { get; set; }

    /// <summary>Their own cricket profile, copied to every squad place linked to them. Null until they fill it in.</summary>
    public UserCricketProfile? Cricket { get; set; }

    /// <summary>Admin accounts are removed by another admin, not from the app.</summary>
    public bool CanDelete { get; set; }
}

public class UpdateAccountRequest
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Null leaves the cricket profile as it is.</summary>
    public TournamentScheduler.Api.Models.CricketProfileInput? Cricket { get; set; }
}

public class LogoutAllResponse
{
    public int SignedOutDevices { get; set; }
}
