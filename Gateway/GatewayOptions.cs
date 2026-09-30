namespace TournamentScheduler.Api.Gateway;

/// <summary>The <c>Gateway</c> section of appsettings.</summary>
public sealed class GatewayOptions
{
    public const string Section = "Gateway";

    /// <summary>The one URL every encrypted request is posted to.</summary>
    public const string Path = "/api/gateway";

    /// <summary>
    /// When true, calling an action on its own URL is refused unless it skips encryption. Off in
    /// Development so Swagger keeps working; on everywhere else.
    /// </summary>
    public bool EnforceEncryption { get; set; } = true;

    /// <summary>
    /// Where the server's private keys live, one <c>{keyId}.pem</c> per key. Outside the repo and
    /// the site folder on purpose, and shared by <c>dotnet run</c> and IIS so both answer to the
    /// same public key. Create one with <c>dotnet run -- gateway-keys new</c>.
    /// </summary>
    public string KeyDirectory { get; set; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "TournamentScheduler", "keys");

    /// <summary>
    /// The key the apps should be using. Null means the newest file in <see cref="KeyDirectory"/>.
    /// Every key in the folder is still accepted, which is what lets a key be rotated without
    /// breaking phones that have not been updated yet.
    /// </summary>
    public string? CurrentKeyId { get; set; }

    /// <summary>Service ids allowed without encryption, on top of those marked [SkipEncryption].</summary>
    public List<string> PlainServiceIds { get; set; } = new();

    /// <summary>
    /// How far a request's timestamp may be from the server's clock. A request older than this is
    /// refused, which bounds how long a captured request could be replayed.
    /// </summary>
    public int MaxClockSkewSeconds { get; set; } = 300;

    /// <summary>Largest encrypted request accepted. Scoring requests are a few hundred bytes.</summary>
    public int MaxRequestBytes { get; set; } = 1_048_576;
}
