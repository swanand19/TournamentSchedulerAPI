using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;
using TournamentScheduler.Api.Auth;
using TournamentScheduler.Api.Logging;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Models.Messaging;

namespace TournamentScheduler.Api.Messaging;

// Messages that aren't sign-in codes: invites (phase 6C) and end-date reminders (6D). They go
// the same two ways as codes — email by Auth:OtpDelivery, a number by Sms:Provider — but carry no
// secret, so the Log senders write the whole message. On WhatsApp a message goes only if its template
// row names an approved Meta template (the admin portal's Messages page): Meta refuses anything else.

/// <summary>
/// The Meta template a message goes out as on WhatsApp, with its {{1}}, {{2}}, … already filled in
/// from the message's values, in the order its template row gives.
/// </summary>
public sealed record WhatsAppTemplateMessage(string Name, string Language, IReadOnlyList<string> Values)
{
    /// <summary>The row's WhatsApp template, or null when it has none (the message isn't sent on WhatsApp).</summary>
    public static WhatsAppTemplateMessage? From(NotificationTemplate row, IReadOnlyDictionary<string, string> values) =>
        string.IsNullOrWhiteSpace(row.WhatsAppTemplateName)
            ? null
            : new(row.WhatsAppTemplateName, row.WhatsAppLanguage ?? NotificationTemplateCatalog.CodeWhatsAppLanguage,
                NotificationTemplateCatalog.WhatsAppParametersOf(row.WhatsAppParameters).Select(name => values.GetValueOrDefault(name) ?? "").ToList());
}

/// <summary>
/// A message to a number that has no WhatsApp template yet — say an invite before Meta approved its
/// template. Nothing is sent to Meta; the caller says it couldn't be sent.
/// </summary>
public sealed class WhatsAppTemplateMissingException(string kind)
    : Exception($"{kind} messages have no WhatsApp template yet: name an approved one on the admin portal's Messages page.");

/// <summary>
/// One message to one person. <see cref="Subject"/> is for email only; a text is just
/// <see cref="Body"/>. <see cref="Kind"/> names it in the logs ("Invite"). <see cref="WhatsApp"/> is
/// what goes to a number when numbers are sent by WhatsApp.
/// </summary>
public sealed record OutgoingMessage(OtpChannel Channel, string To, string Subject, string Body, string Kind,
    WhatsAppTemplateMessage? WhatsApp = null);

/// <summary>Delivers a message by its channel. Throws when it can't, so the caller can say so.</summary>
public interface IMessageSender
{
    Task SendAsync(OutgoingMessage message, CancellationToken cancellationToken = default);
}

/// <summary>Email over SMTP or to the log (<c>Auth:OtpDelivery</c>); numbers to the log or WhatsApp (<c>Sms:Provider</c>).</summary>
public sealed class MessageDispatcher(
    SmtpMailer smtp, WhatsAppClient whatsApp, IOptions<AuthOptions> auth, IOptions<SmsOptions> sms,
    ILogger<MessageDispatcher> logger) : IMessageSender
{
    public const string InviteKind = "Invite";

    public Task SendAsync(OutgoingMessage message, CancellationToken cancellationToken = default)
    {
        if (message.Channel == OtpChannel.Sms)
        {
            switch (sms.Value.Provider)
            {
                case SmsProvider.Log:
                    logger.LogWarning("{Kind} for {Phone} (Sms:Provider is Log — no text message was sent): {Body}", message.Kind, message.To, message.Body);
                    return Task.CompletedTask;
                case SmsProvider.WhatsApp:
                    if (message.WhatsApp is not { } template) throw new WhatsAppTemplateMissingException(message.Kind);
                    return whatsApp.SendTemplateAsync(message.To, template.Name, template.Language, template.Values, null, template.Values, cancellationToken);
                default:
                    throw new InvalidOperationException("Messages to mobile numbers aren't set up on the server (the Sms section of appsettings).");
            }
        }

        if (auth.Value.OtpDelivery == OtpDelivery.Log)
        {
            logger.LogWarning("{Kind} for {Email} (Auth:OtpDelivery is Log — no email was sent): {Subject}", message.Kind, message.To, message.Subject);
            return Task.CompletedTask;
        }
        return smtp.SendAsync(message.To, message.Subject, message.Body, message.Subject, cancellationToken);
    }
}

/// <summary>
/// Sends one email through the <c>Smtp</c> section, recording the call in RemoteLogs with
/// <c>loggedSubject</c> — which, for a code, has the code taken out.
/// </summary>
public sealed class SmtpMailer(IOptions<SmtpOptions> options, RemoteCallRecorder remote)
{
    private readonly SmtpOptions _smtp = options.Value;

    public async Task SendAsync(string to, string subject, string body, string loggedSubject, CancellationToken cancellationToken = default)
    {
        if (!_smtp.IsConfigured)
            throw new InvalidOperationException("Email isn't set up on the server (the Smtp section of appsettings is empty).");

        var call = remote.Start("SMTP", "SEND", $"{_smtp.Host}:{_smtp.Port}", new { to, subject = loggedSubject });

        using var mail = new MailMessage
        {
            From = new MailAddress(_smtp.FromAddress!, _smtp.FromName),
            Subject = subject,
            Body = body
        };
        mail.To.Add(to);

        using var client = new SmtpClient(_smtp.Host, _smtp.Port)
        {
            EnableSsl = _smtp.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = _smtp.TimeoutSeconds * 1000,
            Credentials = string.IsNullOrEmpty(_smtp.UserName) ? null : new NetworkCredential(_smtp.UserName, _smtp.Password)
        };

        try
        {
            await client.SendMailAsync(mail, cancellationToken);
            call.Succeeded(250, new { accepted = true });
        }
        catch (Exception e) when (e is SmtpException or InvalidOperationException or IOException)
        {
            call.Failed(e);
            throw;
        }
    }
}
