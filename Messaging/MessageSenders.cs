using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;
using TournamentScheduler.Api.Auth;
using TournamentScheduler.Api.Logging;
using TournamentScheduler.Api.Models.Accounts;

namespace TournamentScheduler.Api.Messaging;

// Messages that aren't sign-in codes: invites (phase 6C) and end-date reminders (6D). They go
// the same two ways as codes — email by Auth:OtpDelivery, text by Sms:Provider — but carry no
// secret, so the Log senders write the whole message.

/// <summary>
/// One message to one person. <see cref="Subject"/> is for email only; a text is just
/// <see cref="Body"/>. <see cref="Kind"/> names it in the logs ("Invite").
/// </summary>
public sealed record OutgoingMessage(OtpChannel Channel, string To, string Subject, string Body, string Kind);

/// <summary>Delivers a message by its channel. Throws when it can't, so the caller can say so.</summary>
public interface IMessageSender
{
    Task SendAsync(OutgoingMessage message, CancellationToken cancellationToken = default);
}

/// <summary>Email over SMTP or to the log (<c>Auth:OtpDelivery</c>); texts to the log or a provider (<c>Sms:Provider</c>).</summary>
public sealed class MessageDispatcher(SmtpMailer smtp, IOptions<AuthOptions> auth, IOptions<SmsOptions> sms, ILogger<MessageDispatcher> logger) : IMessageSender
{
    public Task SendAsync(OutgoingMessage message, CancellationToken cancellationToken = default)
    {
        if (message.Channel == OtpChannel.Sms)
        {
            if (sms.Value.Provider != SmsProvider.Log)
                throw new InvalidOperationException("Text messages aren't set up on the server (the Sms section of appsettings).");
            logger.LogWarning("{Kind} for {Phone} (Sms:Provider is Log — no text message was sent): {Body}", message.Kind, message.To, message.Body);
            return Task.CompletedTask;
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
