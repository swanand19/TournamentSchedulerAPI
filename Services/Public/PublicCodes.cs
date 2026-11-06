using System.Security.Cryptography;
using TournamentScheduler.Api.Services.Access;

namespace TournamentScheduler.Api.Services.Public;

/// <summary>
/// The code in a tournament's public link: eight characters from the join codes' alphabet (no 0/O,
/// no 1/I), about a trillion codes, so links can't be guessed by walking them.
/// </summary>
public static class PublicCodes
{
    public const int Length = 8;

    public static string New()
    {
        Span<char> code = stackalloc char[Length];
        for (var i = 0; i < Length; i++)
            code[i] = JoinCodes.Alphabet[RandomNumberGenerator.GetInt32(JoinCodes.Alphabet.Length)];
        return new string(code);
    }

    /// <summary>
    /// The code from what someone pasted: the code itself ("abcd 2345") or the whole link
    /// (".../live/ABCD2345?x"). Null when it can't be one.
    /// </summary>
    public static string? Normalise(string? typed)
    {
        var text = (typed ?? "").Trim();
        var live = text.LastIndexOf("/live/", StringComparison.OrdinalIgnoreCase);
        if (live >= 0) text = text[(live + "/live/".Length)..];
        var end = text.IndexOfAny(['/', '?', '#']);
        if (end >= 0) text = text[..end];

        var code = new string(text.Where(ch => !char.IsWhiteSpace(ch) && ch != '-').ToArray()).ToUpperInvariant();
        return code.Length == Length && code.All(JoinCodes.Alphabet.Contains) ? code : null;
    }
}
