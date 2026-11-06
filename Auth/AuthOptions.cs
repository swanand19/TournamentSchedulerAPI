namespace TournamentScheduler.Api.Auth;

/// <summary>The <c>Auth</c> section of appsettings: how long sessions last and how codes are sent.</summary>
public sealed class AuthOptions
{
    public const string Section = "Auth";

    /// <summary>A phone stays signed in this long after its last use.</summary>
    public int SessionDays { get; set; } = 30;

    /// <summary>
    /// Browsers get less: the web app keeps its token in browser storage, which any script on the
    /// page can read, unlike the phone's keychain.
    /// </summary>
    public int WebSessionDays { get; set; } = 7;

    /// <summary>
    /// How often a session's last-seen time (and so its expiry) is moved forward. Not on every
    /// request: the live screens poll every few seconds.
    /// </summary>
    public int SessionTouchMinutes { get; set; } = 5;

    /// <summary>
    /// The admin portal stays signed in this long after its last use — a working day, not weeks:
    /// it can block people and change what everyone may do.
    /// </summary>
    public int PortalSessionHours { get; set; } = 12;

    /// <summary>Wrong portal passwords in a row before the portal sign-in locks, and for how long.</summary>
    public int PasswordMaxAttempts { get; set; } = 5;
    public int PasswordLockMinutes { get; set; } = 15;

    /// <summary>How often the in-memory permissions are reloaded, so a change made in SQL applies within this long.</summary>
    public int PermissionRefreshSeconds { get; set; } = 60;

    public int OtpLength { get; set; } = 6;
    public int OtpLifetimeSeconds { get; set; } = 300;
    public int OtpMaxAttempts { get; set; } = 5;

    /// <summary>Codes one email may be sent: one a minute, this many an hour.</summary>
    public int OtpResendSeconds { get; set; } = 60;
    public int OtpPerEmailPerHour { get; set; } = 5;

    /// <summary>Codes one mobile number may be texted an hour (also one a minute). Fewer than email: each text costs money.</summary>
    public int OtpPerPhonePerHour { get; set; } = 3;

    /// <summary>An invite to someone without an account (phase 6C) waits this long for them to sign up.</summary>
    public int InviteDays { get; set; } = 30;

    /// <summary>Across all emails, from one device or one network address.</summary>
    public int OtpPerDevicePerHour { get; set; } = 10;
    public int OtpPerIpPerHour { get; set; } = 30;

    /// <summary>
    /// <c>Smtp</c> sends real emails (the <c>Smtp</c> section). <c>Log</c> writes the code to the API
    /// log and to <see cref="DevCodeFile"/> instead — for development, or a LAN test server before
    /// email is set up. Never leave <c>Log</c> on for real users.
    /// </summary>
    public OtpDelivery OtpDelivery { get; set; } = OtpDelivery.Smtp;

    /// <summary>Where <c>Log</c> delivery writes codes, one line each.</summary>
    public string DevCodeFile { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "TournamentScheduler", "logs", "otp-codes.txt");
}

public enum OtpDelivery
{
    Smtp,
    Log
}

/// <summary>The <c>Smtp</c> section. The password belongs in the server's own appsettings or user secrets, never in the repo.</summary>
public sealed class SmtpOptions
{
    public const string Section = "Smtp";

    public string? Host { get; set; }
    public int Port { get; set; } = 587;

    /// <summary>STARTTLS on 587 (Gmail, Brevo, most providers).</summary>
    public bool EnableSsl { get; set; } = true;

    public string? UserName { get; set; }
    public string? Password { get; set; }
    public string? FromAddress { get; set; }
    public string FromName { get; set; } = "Tournament Scheduler";
    public int TimeoutSeconds { get; set; } = 15;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(FromAddress);
}

/// <summary>
/// The <c>Sms</c> section: how sign-in codes reach a mobile number (phase 6B). <c>None</c> (the
/// default) switches phone numbers off in both apps; <c>Log</c> writes texted codes to the log and
/// <see cref="AuthOptions.DevCodeFile"/> like email codes, for development and LAN testing;
/// <c>WhatsApp</c> sends codes and invites through Meta's WhatsApp Cloud API (the <c>WhatsApp</c>
/// section). The keys belong in the server's own appsettings or user secrets, never in the repo.
/// </summary>
public sealed class SmsOptions
{
    public const string Section = "Sms";

    public SmsProvider Provider { get; set; } = SmsProvider.None;

    /// <summary>What a message to a number is called in what people read: "the WhatsApp message", "the text message".</summary>
    public string PhoneMessage => Provider == SmsProvider.WhatsApp ? "WhatsApp message" : "text message";

    /// <summary>The provider's API key.</summary>
    public string? ApiKey { get; set; }

    /// <summary>The registered sender name the text arrives from (six letters in India).</summary>
    public string? SenderId { get; set; }

    /// <summary>India's DLT rules: every text must match a registered template, quoted by its id.</summary>
    public string? TemplateId { get; set; }

    public bool IsEnabled => Provider != SmsProvider.None;
}

public enum SmsProvider
{
    None,
    Log,
    WhatsApp
}

/// <summary>
/// The <c>WhatsApp</c> section, used when <c>Sms:Provider</c> is <c>WhatsApp</c>. Meta only lets a
/// business start a conversation with a template it has approved, so each kind of message names
/// its template here, and the wording lives at Meta (Messaging/WhatsApp.md has the texts to submit).
/// The access token belongs in the server's own appsettings or user secrets, never in the repo.
/// </summary>
public sealed class WhatsAppOptions
{
    public const string Section = "WhatsApp";

    public string BaseUrl { get; set; } = "https://graph.facebook.com";
    public string ApiVersion { get; set; } = "v23.0";

    /// <summary>The sending number's id in Meta's WhatsApp Manager (not the number itself).</summary>
    public string? PhoneNumberId { get; set; }

    /// <summary>A system user's permanent token with whatsapp_business_messaging.</summary>
    public string? AccessToken { get; set; }

    /// <summary>The language every template was approved in.</summary>
    public string Language { get; set; } = "en";

    /// <summary>
    /// The authentication template every code uses — sign-in, adding a number, deleting the account.
    /// Meta fixes its wording ("123456 is your verification code."), so one serves all three.
    /// </summary>
    public string CodeTemplate { get; set; } = "sign_in_code";

    /// <summary>The utility template for an invite to own or score a tournament.</summary>
    public string InviteTemplate { get; set; } = "tournament_invite";

    /// <summary>
    /// The invite's placeholders in the order of the template's {{1}}, {{2}}, … — names from the
    /// TournamentInvite template (InviterName, Role, TournamentName, Sport, RoleAction, ExpiresOn).
    /// Empty means <see cref="DefaultInviteParameters"/> (a default here would be added to, not replaced, by the binder).
    /// </summary>
    public string[]? InviteParameters { get; set; }

    public static readonly string[] DefaultInviteParameters = ["InviterName", "Role", "TournamentName", "ExpiresOn"];

    public IReadOnlyList<string> InviteOrder => InviteParameters is { Length: > 0 } p ? p : DefaultInviteParameters;

    public int TimeoutSeconds { get; set; } = 15;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(PhoneNumberId) && !string.IsNullOrWhiteSpace(AccessToken);
}
