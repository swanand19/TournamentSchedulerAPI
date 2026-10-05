using System.Text.Json.Serialization;

namespace TournamentScheduler.Api.Models.Accounts;

// Accounts, in the team's table conventions: every table carries Crd/CrdBy (created), Lmd/LmdBy
// (last modified) and IsDisabled. CrdBy/LmdBy are plain user ids without foreign keys, as audit
// columns usually are. The full design is in docs/accounts-and-access-plan.md.

/// <summary>Who created and last changed a row, and whether it is switched off.</summary>
public abstract class AuditedEntity
{
    public DateTime Crd { get; set; }
    public int CrdBy { get; set; }
    public DateTime Lmd { get; set; }
    public int LmdBy { get; set; }
    public bool IsDisabled { get; set; }

    /// <summary>Stamps a new row: created and last changed now, by <paramref name="userId"/>.</summary>
    public void Created(DateTime nowUtc, int userId)
    {
        Crd = Lmd = nowUtc;
        CrdBy = LmdBy = userId;
    }

    public void Modified(DateTime nowUtc, int userId)
    {
        Lmd = nowUtc;
        LmdBy = userId;
    }
}

/// <summary>
/// What kind of account someone has. Two are seeded with fixed ids the code relies on (<see cref="RoleIds"/>);
/// more can be added later from the admin portal, each getting exactly the permissions ticked for it.
/// </summary>
public class Role : AuditedEntity
{
    public int RoleId { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public string? Description { get; set; }
}

/// <summary>The seeded roles. Always compare ids, never names — names can be edited.</summary>
public static class RoleIds
{
    public const int Admin = 1;
    public const int EndUser = 2;
}

/// <summary>
/// One person, whatever they do: organise, score or play. Signs in with a one-time code sent to
/// their email; there is no password.
///
/// Blocked (<see cref="AuditedEntity.IsDisabled"/>) and deleted (<see cref="DeletedOn"/>) are different on
/// purpose: a blocked account keeps its email so it can't sign up again; a deleted one is
/// anonymised and its email freed.
/// </summary>
public class User : AuditedEntity
{
    public int UserId { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Lower-case. Unique among accounts that have one; null once an account is deleted.</summary>
    public string? EmailId { get; set; }

    /// <summary>For phone sign-in later. Not used yet.</summary>
    public string? PhoneNumber { get; set; }

    /// <summary>When the email was first proved with a code — the moment the account became usable.</summary>
    public DateTime? EmailVerifiedAt { get; set; }

    public int RoleId { get; set; }

    [JsonIgnore]
    public Role? Role { get; set; }

    public DateTime? LastLoginAt { get; set; }
    public DateTime? DeletedOn { get; set; }
}

/// <summary>
/// The account the system itself acts as: it owns the tournaments created before accounts existed,
/// and is CrdBy on seeded rows. It has no email, so nobody can sign in as it.
/// </summary>
public static class SystemUser
{
    public const int Id = 1;
}

/// <summary>
/// One signed-in device. The app holds a random token; only its SHA-256 fingerprint is stored, so a
/// copy of this table can't be used to sign in. Revoking the row signs the device out on its next
/// request.
/// </summary>
public class UserSession
{
    /// <summary>Not secret: sent in the clear request header so the logs can group a device's requests.</summary>
    public Guid SessionId { get; set; }

    public int UserId { get; set; }

    [JsonIgnore]
    public User? User { get; set; }

    /// <summary>SHA-256 of the token the app holds, as hex.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public string? Channel { get; set; }
    public string? DeviceId { get; set; }
    public DateTime Crd { get; set; }
    public DateTime LastSeenAt { get; set; }

    /// <summary>Moves forward with use (sliding); a device unused this long must sign in again.</summary>
    public DateTime ExpiresAt { get; set; }

    public DateTime? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }
}

public enum OtpPurpose
{
    SignIn = 0,

    /// <summary>Confirms "Delete my account": the same email code, kept apart so one can't stand in for the other.</summary>
    DeleteAccount = 1
}

public enum OtpChannel
{
    Email = 0
}

/// <summary>
/// A one-time code that was sent. Only its hash is kept; it lives five minutes, allows five tries,
/// and asking for a new one cancels it.
/// </summary>
public class OtpChallenge
{
    public long OtpChallengeId { get; set; }
    public OtpPurpose Purpose { get; set; }
    public OtpChannel Channel { get; set; }

    /// <summary>Where it was sent: the lower-case email.</summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>SHA-256 of the email and code, as hex.</summary>
    public string CodeHash { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
    public string? DeviceId { get; set; }
    public string? ClientIp { get; set; }
    public DateTime Crd { get; set; }
}
