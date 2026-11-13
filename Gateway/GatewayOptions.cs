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
    public string KeyDirectory { get; set; } = Hosting.AppPaths.Under("keys");

    /// <summary>
    /// Keys given as settings instead of files: key id → PKCS#8 PEM text. This is how Azure supplies them —
    /// each is an App Service setting <c>Gateway__Keys__{keyId}</c> holding a Key Vault reference, so the
    /// private key lives only in Key Vault and no file is ever written. Merged with the folder's keys; a
    /// setting wins over a file with the same id.
    /// </summary>
    public Dictionary<string, string> Keys { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// The key the apps should be using. Null means the newest key (folder or <see cref="Keys"/>).
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
