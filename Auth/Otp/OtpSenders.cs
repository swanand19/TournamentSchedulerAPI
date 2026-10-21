using Microsoft.Extensions.Options;
using TournamentScheduler.Api.Messaging;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Models.Messaging;

namespace TournamentScheduler.Api.Auth.Otp;

/// <summary>A code to deliver. <see cref="Code"/> is the secret: it goes in the message and nowhere else.</summary>
public sealed record OtpMessage(string To, string Code, int ValidMinutes, OtpPurpose Purpose = OtpPurpose.SignIn, OtpChannel Channel = OtpChannel.Email)
{
    /// <summary>What the code is for, in the words of the message and the log.</summary>
    public string What => Purpose switch
    {
        OtpPurpose.DeleteAccount => "account deletion",
        OtpPurpose.AddContact => Channel == OtpChannel.Sms ? "adding this number" : "adding this email",
        _ => "sign-in"
    };

    /// <summary>The template the message is worded by.</summary>
    public NotificationTemplateCode TemplateCode => Purpose switch
    {
        OtpPurpose.DeleteAccount => NotificationTemplateCode.DeleteAccountCode,
        OtpPurpose.AddContact => NotificationTemplateCode.AddContactCode,
        _ => NotificationTemplateCode.SignInCode
    };

    /// <summary>The template's placeholders, with <paramref name="code"/> standing in for the code (the logs get "&lt;code&gt;").</summary>
    public IReadOnlyDictionary<string, string> Values(string code) =>
        new Dictionary<string, string> { ["Code"] = code, ["ValidMinutes"] = ValidMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture) };
}

/// <summary>
/// Delivers one-time codes. The account services don't care how: <see cref="OtpDispatcher"/> picks
/// email or a text message from the message's channel. Throws when delivery fails, so the caller
/// can say so.
/// </summary>
public interface IOtpSender
{
    Task SendAsync(OtpMessage message, CancellationToken cancellationToken = default);
}

/// <summary>
/// Routes each code to its channel: emails by <c>Auth:OtpDelivery</c> (SMTP or the log), texts by
/// <c>Sms:Provider</c>. Choosing an SMS provider later means one sender class for it and one line
/// here; nothing else changes.
/// </summary>
public sealed class OtpDispatcher(IServiceProvider services, IOptions<AuthOptions> auth, IOptions<SmsOptions> sms) : IOtpSender
{
    public Task SendAsync(OtpMessage message, CancellationToken cancellationToken = default)
    {
        IOtpSender sender = message.Channel switch
        {
            OtpChannel.Sms => sms.Value.Provider switch
            {
                SmsProvider.Log => services.GetRequiredService<LogOtpSender>(),
                _ => throw new InvalidOperationException("Text messages aren't set up on the server (the Sms section of appsettings).")
            },
            _ => auth.Value.OtpDelivery == OtpDelivery.Log
                ? services.GetRequiredService<LogOtpSender>()
                : services.GetRequiredService<SmtpOtpSender>()
        };
        return sender.SendAsync(message, cancellationToken);
    }
}

/// <summary>
/// Real email over SMTP (the <c>Smtp</c> section), worded by the code's template (editable in the
/// admin portal). Each send is a RemoteLogs row with the address and subject — never the code.
/// </summary>
public sealed class SmtpOtpSender(SmtpMailer mailer, NotificationTemplateStore templates) : IOtpSender
{
    public async Task SendAsync(OtpMessage message, CancellationToken cancellationToken = default)
    {
        var template = await templates.GetAsync(message.TemplateCode, cancellationToken);
        var mail = TemplateText.Email(template, message.Values(message.Code));
        var logged = TemplateText.Email(template, message.Values("<code>")).Title;
        await mailer.SendAsync(message.To, mail.Title, mail.Body, logged, cancellationToken);
    }
}
/// <summary>
/// For development and a LAN test server before email or SMS is set up: the code goes to the API
/// log and to a text file (<see cref="AuthOptions.DevCodeFile"/>) instead of an inbox or a phone.
/// Anyone who can read that file can sign in as anyone, so this must never be on for real users.
/// </summary>
public sealed class LogOtpSender(IOptions<AuthOptions> options, TimeProvider clock, NotificationTemplateStore templates, ILogger<LogOtpSender> logger) : IOtpSender
{
    private static readonly Lock Gate = new();

    public async Task SendAsync(OtpMessage message, CancellationToken cancellationToken = default)
    {
        if (message.Channel == OtpChannel.Sms)
        {
            // The text its template would send, so a wording change can be checked before a provider exists.
            var text = TemplateText.Sms(await templates.GetAsync(message.TemplateCode, cancellationToken), message.Values(message.Code));
            logger.LogWarning("Code for {What}, {Phone}: {Code} (Sms:Provider is Log — no text message was sent). The text would read: {Text}", message.What, message.To, message.Code, text);
        }
        else
            logger.LogWarning("Code for {What}, {Email}: {Code} (Auth:OtpDelivery is Log — no email was sent).", message.What, message.To, message.Code);

        var file = options.Value.DevCodeFile;
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.AppendAllText(file, $"{clock.GetUtcNow():yyyy-MM-dd HH:mm:ss}Z  {message.To}  {message.Code}  {message.What}{Environment.NewLine}");
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("Couldn't write the code to {File}: {Reason}", file, e.Message);
        }
    }
}
