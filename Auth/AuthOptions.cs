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
/// <see cref="AuthOptions.DevCodeFile"/> like email codes, for development and LAN testing.
/// A real provider adds its value here, a sender class in Auth/Otp, and uses the keys below.
/// The keys belong in the server's own appsettings or user secrets, never in the repo.
/// </summary>
public sealed class SmsOptions
{
    public const string Section = "Sms";

    public SmsProvider Provider { get; set; } = SmsProvider.None;

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
    Log
}
