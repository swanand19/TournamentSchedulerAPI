using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace TournamentScheduler.Api.Logging;

/// <summary>
/// Makes a body safe and small enough to store: credential fields (password, pin, otp, token…)
/// become "***" wherever they appear in the JSON, and anything past the size cap is cut.
///
/// Only credentials are masked; the rest stays in plain text on purpose, since that is what makes
/// the logs useful. A password must never be stored even hashed — a hash an app sends to log in
/// works exactly like the password itself.
/// </summary>
public sealed class LogRedactor
{
    public const string Mask = "***";

    private readonly HashSet<string> _fields;
    private readonly int _maxChars;

    public LogRedactor(IOptions<LogStoreOptions> options)
    {
        _fields = options.Value.MaskedFields.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _maxChars = Math.Max(1_000, options.Value.MaxBodyChars);
    }

    public string? Body(byte[]? utf8) => utf8 is { Length: > 0 } ? Body(Encoding.UTF8.GetString(utf8)) : null;

    /// <summary>Masks credentials and caps the length. Null or empty stays null.</summary>
    public string? Body(string? text) => string.IsNullOrEmpty(text) ? null : Cap(MaskCredentials(text));

    /// <summary>Caps the length only — for ciphertext, where there is nothing to mask.</summary>
    public string? Cap(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        if (text.Length <= _maxChars) return text;
        return text[.._maxChars] + $"…[truncated {text.Length - _maxChars} chars]";
    }

    private string MaskCredentials(string text)
    {
        // Parsing every body just to find nothing would be waste: most never mention a credential.
        if (!_fields.Any(f => text.Contains(f, StringComparison.OrdinalIgnoreCase))) return text;

        try
        {
            var node = JsonNode.Parse(text);
            return Walk(node) ? node!.ToJsonString() : text;
        }
        catch (JsonException)
        {
            return text; // not JSON: stored as it came
        }
    }

    /// <summary>True when something was masked.</summary>
    private bool Walk(JsonNode? node)
    {
        var changed = false;
        switch (node)
        {
            case JsonObject obj:
                foreach (var name in obj.Select(p => p.Key).ToList())
                {
                    if (_fields.Contains(name) && obj[name] is not null)
                    {
                        obj[name] = Mask;
                        changed = true;
                    }
                    else
                    {
                        changed |= Walk(obj[name]);
                    }
                }
                break;
            case JsonArray array:
                foreach (var item in array) changed |= Walk(item);
                break;
        }
        return changed;
    }
}
