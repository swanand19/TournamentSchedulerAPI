using System.Buffers.Text;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace TournamentScheduler.Api.Gateway;

/// <summary>One of the server's P-256 key pairs. The private half never leaves the server.</summary>
public sealed class GatewayKey : IDisposable
{
    public GatewayKey(string id, ECDiffieHellman privateKey)
    {
        Id = id;
        PrivateKey = privateKey;
        PublicKey = GatewayCrypto.ExportPublicKey(privateKey);
    }

    public string Id { get; }
    public ECDiffieHellman PrivateKey { get; }

    /// <summary>65-byte uncompressed point — what the apps are configured with.</summary>
    public byte[] PublicKey { get; }

    public string PublicKeyBase64Url => Base64Url.EncodeToString(PublicKey);

    public void Dispose() => PrivateKey.Dispose();
}

public interface IGatewayKeyStore
{
    /// <summary>The key the apps should be encrypting for, or null when there are none yet.</summary>
    GatewayKey? Current { get; }

    /// <summary>Any key the server still holds — the current one or one being rotated out.</summary>
    GatewayKey? Find(string keyId);
}

/// <summary>
/// Keys read from <see cref="GatewayOptions.KeyDirectory"/> at start-up: each <c>{keyId}.pem</c> is a
/// PKCS#8 P-256 private key. Adding or retiring a key takes an API restart (Publish-IisSite.ps1 does
/// one). A folder that is missing or empty is not an error — the API still starts, and the gateway
/// answers 503 until a key is created.
/// </summary>
public sealed class FileGatewayKeyStore : IGatewayKeyStore, IDisposable
{
    private readonly Dictionary<string, GatewayKey> _keys;

    public FileGatewayKeyStore(IOptions<GatewayOptions> options, ILogger<FileGatewayKeyStore> logger)
    {
        var settings = options.Value;
        _keys = GatewayKeyFiles.Load(settings.KeyDirectory, logger);
        Current = GatewayKeyFiles.PickCurrent(_keys, settings.CurrentKeyId);

        if (Current == null)
            logger.LogWarning("No gateway key in {KeyDirectory}. Encrypted requests will be refused until one is created with 'dotnet run -- gateway-keys new'.", settings.KeyDirectory);
        else
            logger.LogInformation("Gateway keys loaded: {KeyIds}; apps should use {CurrentKeyId}.", string.Join(", ", _keys.Keys), Current.Id);
    }

    public GatewayKey? Current { get; }

    public GatewayKey? Find(string keyId) => _keys.GetValueOrDefault(keyId);

    public void Dispose()
    {
        foreach (var key in _keys.Values) key.Dispose();
    }
}

/// <summary>Reading and writing the key folder, shared by the store and the gateway-keys command.</summary>
public static class GatewayKeyFiles
{
    public const string Extension = ".pem";

    public static Dictionary<string, GatewayKey> Load(string directory, ILogger? logger = null)
    {
        var keys = new Dictionary<string, GatewayKey>(StringComparer.Ordinal);
        if (!Directory.Exists(directory)) return keys;

        List<string> files;
        try
        {
            files = Directory.EnumerateFiles(directory, "*" + Extension).ToList();
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException)
        {
            // Not fatal: the API still starts (so /api/health answers) and the gateway says why it
            // can't decrypt. Under IIS this is the app pool identity lacking read access to the folder.
            var identity = OperatingSystem.IsWindows()
                ? System.Security.Principal.WindowsIdentity.GetCurrent().Name   // e.g. IIS APPPOOL\TournamentSchedulerApi
                : Environment.UserName;
            logger?.LogError(e,
                "The gateway key folder {KeyDirectory} can't be read by {Identity}. Grant read access, then restart the site: {Fix}",
                directory, identity, $"icacls \"{directory}\" /grant \"{identity}:(OI)(CI)R\"");
            return keys;
        }

        foreach (var file in files)
        {
            var id = Path.GetFileNameWithoutExtension(file);
            try
            {
                var key = ECDiffieHellman.Create();
                key.ImportFromPem(File.ReadAllText(file));
                if (key.KeySize != 256)
                {
                    key.Dispose();
                    logger?.LogError("Gateway key {File} is not a P-256 key and was ignored.", file);
                    continue;
                }
                keys[id] = new GatewayKey(id, key);
            }
            catch (Exception e) when (e is CryptographicException or ArgumentException or IOException or UnauthorizedAccessException)
            {
                // One unreadable file must not take the others down with it.
                logger?.LogError(e, "Gateway key {File} could not be read and was ignored.", file);
            }
        }

        return keys;
    }

    /// <summary>The configured key if it exists, else the newest (key ids sort by the date they were made).</summary>
    public static GatewayKey? PickCurrent(IReadOnlyDictionary<string, GatewayKey> keys, string? configuredId)
    {
        if (configuredId != null && keys.TryGetValue(configuredId, out var configured)) return configured;
        return keys.Values.OrderBy(k => k.Id, StringComparer.Ordinal).LastOrDefault();
    }

    /// <summary>Creates a new key pair in <paramref name="directory"/>, named after today's date.</summary>
    public static GatewayKey Create(string directory)
    {
        CreateDirectory(directory);

        var id = DateTime.UtcNow.ToString("yyyyMMdd");
        for (var n = 2; File.Exists(Path.Combine(directory, id + Extension)); n++)
            id = $"{DateTime.UtcNow:yyyyMMdd}-{n}";

        var key = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        File.WriteAllText(Path.Combine(directory, id + Extension), key.ExportPkcs8PrivateKeyPem());
        return new GatewayKey(id, key);
    }

    /// <summary>
    /// On Windows the folder is created readable only by SYSTEM, Administrators and the person
    /// running the command — not every local user, as ProgramData would otherwise allow.
    /// Setup-IisSite.ps1 then grants the IIS app pool read access.
    /// </summary>
    private static void CreateDirectory(string directory)
    {
        if (Directory.Exists(directory)) return;

        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            return;
        }

        var security = new System.Security.AccessControl.DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        var inherit = System.Security.AccessControl.InheritanceFlags.ContainerInherit | System.Security.AccessControl.InheritanceFlags.ObjectInherit;

        void Allow(System.Security.Principal.IdentityReference who) =>
            security.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                who, System.Security.AccessControl.FileSystemRights.FullControl, inherit,
                System.Security.AccessControl.PropagationFlags.None, System.Security.AccessControl.AccessControlType.Allow));

        Allow(new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.LocalSystemSid, null));
        Allow(new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.BuiltinAdministratorsSid, null));
        Allow(System.Security.Principal.WindowsIdentity.GetCurrent().User!);

        if (Path.GetDirectoryName(Path.GetFullPath(directory)) is { } parent) Directory.CreateDirectory(parent);
        new DirectoryInfo(directory).Create(security);
    }
}
