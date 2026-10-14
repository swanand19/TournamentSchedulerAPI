using TournamentScheduler.Api.Models.Accounts;

namespace TournamentScheduler.Api.Services.Accounts;

/// <summary>An email (lower-case) or a mobile number (E.164), as the server stores and compares them.</summary>
public readonly record struct Contact(ContactKind Kind, string Value)
{
    public bool IsEmail => Kind == ContactKind.Email;
    public OtpChannel Channel => IsEmail ? OtpChannel.Email : OtpChannel.Sms;

    /// <summary>Where a code sent to it arrives, in the words of a message: "the email", "the text message".</summary>
    public string CodeArrivesIn => IsEmail ? "the email" : "the text message";
}

/// <summary>
/// One field takes either an email or a mobile number (phase 6B): anything with an @ is an email,
/// anything else a number. Numbers are stored in E.164 so "98765 43210", "098765 43210" and
/// "+91 98765-43210" are the same person. A number typed without a country code is taken as
/// Indian (<see cref="DefaultCountryCode"/>), where the app is used today.
/// </summary>
public static class Contacts
{
    public const string DefaultCountryCode = "+91";

    public const string BadPhone = "Enter a valid mobile number, like 98765 43210, or one with its country code, like +44 7700 900123.";

    /// <summary>The contact, or the sentence saying what's wrong with it.</summary>
    public static (Contact? Contact, string? Problem) Parse(string? value)
    {
        var text = value?.Trim() ?? "";
        if (text.Length == 0) return (null, "Enter your email or mobile number.");

        if (text.Contains('@'))
        {
            var email = AccountService.NormaliseEmail(text);
            return email == null ? (null, "Enter a valid email address.") : (new Contact(ContactKind.Email, email), null);
        }

        var phone = NormalisePhone(text);
        return phone == null ? (null, BadPhone) : (new Contact(ContactKind.Phone, phone), null);
    }

    /// <summary>E.164 ("+919876543210"), or null when it isn't a plausible mobile number.</summary>
    public static string? NormalisePhone(string? value)
    {
        var text = value?.Trim() ?? "";
        if (text.Length == 0 || text.Length > 30) return null;

        // Spaces, dashes, dots and brackets are how people group digits; a + may lead ("(+44) …").
        var plus = false;
        var digits = new System.Text.StringBuilder();
        foreach (var ch in text)
        {
            if (char.IsAsciiDigit(ch)) digits.Append(ch);
            else if (ch == '+' && !plus && digits.Length == 0) plus = true;
            else if (ch is not (' ' or '-' or '.' or '(' or ')')) return null;
        }
        var d = digits.ToString();

        if (!plus && d.StartsWith("00")) { plus = true; d = d[2..]; }   // 0044… is the international prefix
        if (!plus)
        {
            if (d.Length == 11 && d[0] == '0') d = d[1..];               // 098765 43210, the trunk prefix
            if (d.Length == 12 && d.StartsWith("91")) d = d[2..];        // 91 98765 43210 without the +
            if (d.Length != 10) return null;
            d = DefaultCountryCode[1..] + d;
        }

        // E.164: a country code that doesn't start with 0, at most 15 digits in all.
        if (d.Length < 8 || d.Length > 15 || d[0] == '0') return null;

        // Indian mobiles are ten digits starting 6–9; that catches most typos where we know the rule.
        if (d.StartsWith("91") && (d.Length != 12 || d[2] < '6')) return null;

        return "+" + d;
    }
}
