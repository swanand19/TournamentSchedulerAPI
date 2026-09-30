using System.Buffers.Binary;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TournamentScheduler.Api.Gateway;

/// <summary>
/// The encryption on both legs of a gateway call, as JWE (RFC 7516) in compact serialisation.
///
/// <para><b>Request</b> — <c>alg: ECDH-ES, enc: A256GCM</c> (RFC 7518 §4.6). The app makes a
/// throwaway P-256 key pair, agrees a secret with the server's public key, and derives a one-time
/// AES-256 key from it with the Concat KDF. Only the holder of the server's private key can derive
/// the same key, so only this server can read the request. The protected header is the AES-GCM
/// associated data, so it cannot be altered either.</para>
///
/// <para><b>Response</b> — <c>alg: dir, enc: A256GCM</c> with that same one-time key and a fresh IV.
/// Only the app that made the request knows the key, so only it can read the answer.</para>
///
/// <para>Everything here is built into .NET; the apps implement the same thing with the audited
/// @noble libraries, and the shared test vectors in the tests project keep the two in step.</para>
/// </summary>
public static class GatewayCrypto
{
    public const string RequestAlgorithm = "ECDH-ES";
    public const string ResponseAlgorithm = "dir";
    public const string ContentEncryption = "A256GCM";

    public const int ContentKeyBytes = 32;
    public const int IvBytes = 12;
    public const int TagBytes = 16;
    private const int CoordinateBytes = 32;

    /// <summary>A decrypted request: its plaintext, the key that answers it, and its protected header.</summary>
    public sealed record DecryptedRequest(byte[] Plaintext, byte[] ContentKey, JsonObject ProtectedHeader);

    // -----------------------------------------------------------------
    // Server side
    // -----------------------------------------------------------------

    /// <summary>Opens a request token. Throws <see cref="GatewayCryptoException"/> saying why it can't.</summary>
    public static DecryptedRequest DecryptRequest(string compact, IGatewayKeyStore keys)
    {
        var parts = SplitCompact(compact);
        if (parts[1].Length != 0)
            throw new GatewayCryptoException(GatewayCryptoError.Malformed, "ECDH-ES carries no encrypted key.");

        var header = ParseHeader(parts[0]);
        if (Text(header, "alg") != RequestAlgorithm || Text(header, "enc") != ContentEncryption)
            throw new GatewayCryptoException(GatewayCryptoError.Unsupported, "Only ECDH-ES with A256GCM is accepted.");

        var keyId = Text(header, "kid")
            ?? throw new GatewayCryptoException(GatewayCryptoError.Malformed, "The token names no key.");
        var key = keys.Find(keyId)
            ?? throw new GatewayCryptoException(GatewayCryptoError.UnknownKey, $"No server key '{keyId}'.");

        var (x, y) = ReadEphemeralKey(header);
        var contentKey = DeriveContentKey(key.PrivateKey, x, y, Bytes(header, "apu"), Bytes(header, "apv"));

        try
        {
            var plaintext = Open(parts, contentKey);
            return new DecryptedRequest(plaintext, contentKey, header);
        }
        catch
        {
            CryptographicOperations.ZeroMemory(contentKey);
            throw;
        }
    }

    /// <summary>Seals a response with the key its request arrived under.</summary>
    public static string EncryptResponse(byte[] plaintext, byte[] contentKey, string? requestUUID, byte[]? iv = null)
    {
        var header = new JsonObject
        {
            ["alg"] = ResponseAlgorithm,
            ["enc"] = ContentEncryption,
        };
        if (requestUUID != null) header["requestUUID"] = requestUUID;

        return Seal(header, contentKey, plaintext, iv);
    }

    // -----------------------------------------------------------------
    // Client side — what the apps do, in C#, for the tests and the shared vectors
    // -----------------------------------------------------------------

    /// <summary>
    /// Builds a request token for <paramref name="serverPublicKey"/> (65-byte uncompressed P-256 point).
    /// <paramref name="ephemeral"/> and <paramref name="iv"/> are only fixed by tests; normally both are random.
    /// </summary>
    public static (string Compact, byte[] ContentKey) EncryptRequest(
        byte[] serverPublicKey, string keyId, IReadOnlyDictionary<string, string> claims, byte[] plaintext,
        ECParameters? ephemeral = null, byte[]? iv = null)
    {
        using var server = ImportPublicKey(serverPublicKey);
        using var ephemeralKey = ephemeral is { } fixedKey
            ? ECDiffieHellman.Create(fixedKey)
            : ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var ephemeralPublic = ephemeralKey.ExportParameters(false).Q;

        var header = new JsonObject
        {
            ["alg"] = RequestAlgorithm,
            ["enc"] = ContentEncryption,
            ["kid"] = keyId,
            ["epk"] = new JsonObject
            {
                ["kty"] = "EC",
                ["crv"] = "P-256",
                ["x"] = Base64Url.EncodeToString(ephemeralPublic.X!),
                ["y"] = Base64Url.EncodeToString(ephemeralPublic.Y!),
            },
        };
        foreach (var (name, value) in claims) header[name] = value;

        var secret = ephemeralKey.DeriveRawSecretAgreement(server.PublicKey);
        var contentKey = ConcatKdf(secret, ContentEncryption, [], []);
        CryptographicOperations.ZeroMemory(secret);

        return (Seal(header, contentKey, plaintext, iv), contentKey);
    }

    /// <summary>Opens a response token with the key its request was sent under.</summary>
    public static byte[] DecryptResponse(string compact, byte[] contentKey)
    {
        var parts = SplitCompact(compact);
        var header = ParseHeader(parts[0]);
        if (Text(header, "alg") != ResponseAlgorithm || Text(header, "enc") != ContentEncryption)
            throw new GatewayCryptoException(GatewayCryptoError.Unsupported, "Only dir with A256GCM is accepted.");
        return Open(parts, contentKey);
    }

    // -----------------------------------------------------------------
    // Keys
    // -----------------------------------------------------------------

    /// <summary>The public half as the apps hold it: the 65-byte uncompressed point, 0x04 || X || Y.</summary>
    public static byte[] ExportPublicKey(ECDiffieHellman key)
    {
        var q = key.ExportParameters(false).Q;
        var point = new byte[1 + 2 * CoordinateBytes];
        point[0] = 0x04;
        q.X!.CopyTo(point, 1);
        q.Y!.CopyTo(point, 1 + CoordinateBytes);
        return point;
    }

    public static ECDiffieHellman ImportPublicKey(byte[] uncompressedPoint)
    {
        if (uncompressedPoint.Length != 1 + 2 * CoordinateBytes || uncompressedPoint[0] != 0x04)
            throw new GatewayCryptoException(GatewayCryptoError.Malformed, "A public key is a 65-byte uncompressed P-256 point.");

        return ImportPoint(uncompressedPoint[1..(1 + CoordinateBytes)], uncompressedPoint[(1 + CoordinateBytes)..]);
    }

    private static ECDiffieHellman ImportPoint(byte[] x, byte[] y)
    {
        // Checked here rather than left to the platform: a point off the curve is how an
        // invalid-curve attack probes a private key, and how the import refuses one differs by OS
        // (Windows throws PlatformNotSupportedException, which would otherwise surface as a 500).
        if (!IsOnP256(x, y))
            throw new GatewayCryptoException(GatewayCryptoError.Malformed, "The ephemeral key is not a valid P-256 point.");

        try
        {
            return ECDiffieHellman.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = x, Y = y }
            });
        }
        catch (Exception e) when (e is CryptographicException or PlatformNotSupportedException)
        {
            throw new GatewayCryptoException(GatewayCryptoError.Malformed, "The ephemeral key is not a valid P-256 point.", e);
        }
    }

    private static readonly System.Numerics.BigInteger P256Prime = Unsigned("FFFFFFFF00000001000000000000000000000000FFFFFFFFFFFFFFFFFFFFFFFF");
    private static readonly System.Numerics.BigInteger P256B = Unsigned("5AC635D8AA3A93E7B3EBBD55769886BC651D06B0CC53B0F63BCE3C3E27D2604B");

    /// <summary>y² ≡ x³ − 3x + b (mod p), with both coordinates in the field (SEC 1 §3.2.2.1).</summary>
    internal static bool IsOnP256(byte[] x, byte[] y)
    {
        var px = new System.Numerics.BigInteger(x, isUnsigned: true, isBigEndian: true);
        var py = new System.Numerics.BigInteger(y, isUnsigned: true, isBigEndian: true);
        if (px >= P256Prime || py >= P256Prime) return false;

        var left = System.Numerics.BigInteger.ModPow(py, 2, P256Prime);
        var right = (System.Numerics.BigInteger.ModPow(px, 3, P256Prime) - 3 * px + P256B) % P256Prime;
        if (right.Sign < 0) right += P256Prime;
        return left == right;
    }

    private static System.Numerics.BigInteger Unsigned(string hex) =>
        new(Convert.FromHexString(hex), isUnsigned: true, isBigEndian: true);

    // -----------------------------------------------------------------
    // JWE mechanics
    // -----------------------------------------------------------------

    private static byte[] DeriveContentKey(ECDiffieHellman privateKey, byte[] x, byte[] y, byte[] apu, byte[] apv)
    {
        using var peer = ImportPoint(x, y);
        var secret = privateKey.DeriveRawSecretAgreement(peer.PublicKey);
        try
        {
            return ConcatKdf(secret, ContentEncryption, apu, apv);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    /// <summary>
    /// NIST SP 800-56A Concat KDF as JWE uses it (RFC 7518 §4.6.2). For direct key agreement the
    /// AlgorithmID is the "enc" value, and one SHA-256 round gives the 256 bits A256GCM needs.
    /// <paramref name="keyBits"/> is only ever not 256 in the test against RFC 7518 Appendix C.
    /// </summary>
    internal static byte[] ConcatKdf(byte[] sharedSecret, string algorithmId, byte[] apu, byte[] apv, int keyBits = ContentKeyBytes * 8)
    {
        if (keyBits is <= 0 or > 256 || keyBits % 8 != 0)
            throw new ArgumentOutOfRangeException(nameof(keyBits), "One SHA-256 round yields at most 256 bits.");

        var algorithm = Encoding.ASCII.GetBytes(algorithmId);
        var input = new byte[4 + sharedSecret.Length + 4 + algorithm.Length + 4 + apu.Length + 4 + apv.Length + 4];
        var span = input.AsSpan();

        BinaryPrimitives.WriteUInt32BigEndian(span, 1);                           // round counter
        span = span[4..];
        sharedSecret.CopyTo(span);
        span = span[sharedSecret.Length..];
        span = WriteLengthPrefixed(span, algorithm);
        span = WriteLengthPrefixed(span, apu);
        span = WriteLengthPrefixed(span, apv);
        BinaryPrimitives.WriteUInt32BigEndian(span, (uint)keyBits);               // SuppPubInfo: key length in bits

        var hash = SHA256.HashData(input);
        CryptographicOperations.ZeroMemory(input);
        return hash[..(keyBits / 8)];
    }

    private static Span<byte> WriteLengthPrefixed(Span<byte> span, byte[] value)
    {
        BinaryPrimitives.WriteUInt32BigEndian(span, (uint)value.Length);
        value.CopyTo(span[4..]);
        return span[(4 + value.Length)..];
    }

    private static string Seal(JsonObject header, byte[] contentKey, byte[] plaintext, byte[]? iv)
    {
        iv ??= RandomNumberGenerator.GetBytes(IvBytes);
        if (iv.Length != IvBytes) throw new ArgumentException("A256GCM uses a 96-bit IV.", nameof(iv));

        var encodedHeader = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(header.ToJsonString()));
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagBytes];

        using (var aes = new AesGcm(contentKey, TagBytes))
            aes.Encrypt(iv, plaintext, ciphertext, tag, Encoding.ASCII.GetBytes(encodedHeader));

        return string.Join('.',
            encodedHeader,
            "",
            Base64Url.EncodeToString(iv),
            Base64Url.EncodeToString(ciphertext),
            Base64Url.EncodeToString(tag));
    }

    private static byte[] Open(string[] parts, byte[] contentKey)
    {
        var iv = Decode(parts[2]);
        var ciphertext = Decode(parts[3]);
        var tag = Decode(parts[4]);
        if (iv.Length != IvBytes || tag.Length != TagBytes)
            throw new GatewayCryptoException(GatewayCryptoError.Malformed, "Wrong IV or tag length for A256GCM.");

        var plaintext = new byte[ciphertext.Length];
        try
        {
            using var aes = new AesGcm(contentKey, TagBytes);
            aes.Decrypt(iv, ciphertext, tag, plaintext, Encoding.ASCII.GetBytes(parts[0]));
            return plaintext;
        }
        catch (CryptographicException e)
        {
            // Wrong key, or any byte of the header, IV, ciphertext or tag changed on the way.
            throw new GatewayCryptoException(GatewayCryptoError.Tampered, "The token failed its integrity check.", e);
        }
    }

    private static string[] SplitCompact(string compact)
    {
        var parts = compact.Split('.');
        if (parts.Length != 5)
            throw new GatewayCryptoException(GatewayCryptoError.Malformed, "A compact JWE has five parts.");
        return parts;
    }

    private static JsonObject ParseHeader(string encoded)
    {
        try
        {
            if (JsonNode.Parse(Decode(encoded)) is not JsonObject header)
                throw new GatewayCryptoException(GatewayCryptoError.Malformed, "The protected header is not a JSON object.");

            // No compression (it leaks length information) and no extensions this code doesn't know.
            if (header.ContainsKey("zip") || header.ContainsKey("crit"))
                throw new GatewayCryptoException(GatewayCryptoError.Unsupported, "zip and crit are not accepted.");
            return header;
        }
        catch (JsonException e)
        {
            throw new GatewayCryptoException(GatewayCryptoError.Malformed, "The protected header is not valid JSON.", e);
        }
    }

    private static (byte[] X, byte[] Y) ReadEphemeralKey(JsonObject header)
    {
        if (header["epk"] is not JsonObject epk || Text(epk, "kty") != "EC" || Text(epk, "crv") != "P-256")
            throw new GatewayCryptoException(GatewayCryptoError.Malformed, "The ephemeral key must be an EC P-256 JWK.");

        var x = Bytes(epk, "x");
        var y = Bytes(epk, "y");
        if (x.Length != CoordinateBytes || y.Length != CoordinateBytes)
            throw new GatewayCryptoException(GatewayCryptoError.Malformed, "The ephemeral key's coordinates must be 32 bytes.");
        return (x, y);
    }

    internal static string? Text(JsonObject obj, string name) =>
        obj[name] is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;

    private static byte[] Bytes(JsonObject obj, string name) =>
        Text(obj, name) is { } s ? Decode(s) : [];

    private static byte[] Decode(string base64Url)
    {
        try
        {
            return Base64Url.DecodeFromChars(base64Url);
        }
        catch (FormatException e)
        {
            throw new GatewayCryptoException(GatewayCryptoError.Malformed, "A token part is not base64url.", e);
        }
    }
}

public enum GatewayCryptoError
{
    Malformed,
    Unsupported,
    UnknownKey,
    Tampered
}

/// <summary>Why a token could not be opened. The message is for logs; callers get a plain sentence.</summary>
public sealed class GatewayCryptoException(GatewayCryptoError error, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public GatewayCryptoError Error { get; } = error;
}
