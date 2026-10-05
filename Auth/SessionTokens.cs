using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace TournamentScheduler.Api.Auth;

/// <summary>
/// Session tokens and one-time codes: made from the operating system's secure random source, and
/// stored only as fingerprints (SHA-256, as hex).
///
/// A plain SHA-256 is enough for a session token — it is 32 random bytes, so there is nothing to
/// guess. A 6-digit code could be brute-forced from its hash, but it lives five minutes and allows
/// five tries; hashing it only keeps codes out of sight of anyone browsing the table.
/// </summary>
public static class SessionTokens
{
    /// <summary>A new token (43 characters, base64url) and the fingerprint to store for it.</summary>
    public static (string Token, string Hash) NewToken()
    {
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        return (token, Hash(token));
    }

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>A code of <paramref name="length"/> digits, every value equally likely.</summary>
    public static string NewCode(int length)
    {
        var digits = new char[length];
        for (var i = 0; i < length; i++) digits[i] = (char)('0' + RandomNumberGenerator.GetInt32(10));
        return new string(digits);
    }

    /// <summary>Bound to the address it was sent to, so a code is only ever worth anything for that email.</summary>
    public static string HashCode(string target, string code) => Hash($"{target}|{code}");

    /// <summary>Compares in constant time, so the time taken says nothing about how close a guess was.</summary>
    public static bool Matches(string expectedHash, string actualHash) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expectedHash), Encoding.ASCII.GetBytes(actualHash));
}
