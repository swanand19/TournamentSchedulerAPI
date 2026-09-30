namespace TournamentScheduler.Api.Logging;

/// <summary>
/// The <c>LogStore</c> section of appsettings. (Not "Logging": that section belongs to .NET's own
/// log levels.) The database itself is the <c>LogConnection</c> connection string.
/// </summary>
public sealed class LogStoreOptions
{
    public const string Section = "LogStore";
    public const string ConnectionStringName = "LogConnection";

    /// <summary>Off means nothing is queued or written — requests run exactly as without logging.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Rows older than this are purged, hourly. Two days while the server is a laptop.</summary>
    public int RetentionHours { get; set; } = 48;

    public int PurgeIntervalMinutes { get; set; } = 60;

    /// <summary>
    /// Store response bodies of reads (GET) too. Off by default: the live screens poll every few
    /// seconds, and full bodies would fill the database with copies of the same match state.
    /// Switch on while chasing a bug in what a screen was sent.
    /// </summary>
    public bool LogReadResponseBodies { get; set; }

    /// <summary>Longer bodies are cut, with a note of how much was dropped.</summary>
    public int MaxBodyChars { get; set; } = 65_536;

    /// <summary>
    /// JSON properties whose values are replaced with "***" wherever they appear, case-insensitive.
    /// Credentials only — everything else is kept in plain text on purpose.
    /// </summary>
    public List<string> MaskedFields { get; set; } =
    [
        "password", "newPassword", "oldPassword", "confirmPassword", "pin", "mpin", "otp",
        "token", "accessToken", "refreshToken", "secret", "apiKey", "authorization", "cvv"
    ];

    /// <summary>Entries waiting to be written. Beyond this, new entries are dropped and counted.</summary>
    public int QueueCapacity { get; set; } = 10_000;

    public int BatchSize { get; set; } = 100;
    public int FlushIntervalMs { get; set; } = 1_000;

    /// <summary>After the database fails, entries go to the fallback file for this long before it is tried again.</summary>
    public int RetryAfterSeconds { get; set; } = 30;

    /// <summary>Where entries go when the logs database can't take them: one JSON-lines file per day.</summary>
    public string FallbackDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "TournamentScheduler", "logs");
}
