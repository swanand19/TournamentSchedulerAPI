using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;
using TournamentScheduler.Api.Logging;
using TournamentScheduler.Api.Models.Accounts;

namespace TournamentScheduler.Api.Auth.Otp;

/// <summary>A code to deliver. <see cref="Code"/> is the secret: it goes in the message and nowhere else.</summary>
public sealed record OtpMessage(string To, string Code, int ValidMinutes, OtpPurpose Purpose = OtpPurpose.SignIn)
{
    /// <summary>What the code is for, in the words of the email and the log.</summary>
    public string What => Purpose == OtpPurpose.DeleteAccount ? "account deletion" : "sign-in";
}

/// <summary>
/// Delivers one-time codes. Email today (<see cref="SmtpOtpSender"/>); phone later — the account
/// service doesn't care which. Throws when delivery fails, so the caller can say so.
/// </summary>
public interface IOtpSender
{
    Task SendAsync(OtpMessage message, CancellationToken cancellationToken = default);
}

/// <summary>
/// Real email over SMTP (the <c>Smtp</c> section). Each send is a RemoteLogs row with the address
/// and subject — never the code.
/// </summary>
public sealed class SmtpOtpSender(IOptions<SmtpOptions> options, RemoteCallRecorder remote) : IOtpSender
{
    private readonly SmtpOptions _smtp = options.Value;

    public async Task SendAsync(OtpMessage message, CancellationToken cancellationToken = default)
    {
        if (!_smtp.IsConfigured)
            throw new InvalidOperationException("Email isn't set up on the server (the Smtp section of appsettings is empty).");

        var deleting = message.Purpose == OtpPurpose.DeleteAccount;
        var subjectAfterCode = deleting ? "is your code to delete your Tournament Scheduler account" : "is your Tournament Scheduler code";
        var call = remote.Start("SMTP", "SEND", $"{_smtp.Host}:{_smtp.Port}", new { to = message.To, subject = $"<code> {subjectAfterCode}" });

        using var mail = new MailMessage
        {
            From = new MailAddress(_smtp.FromAddress!, _smtp.FromName),
            Subject = $"{message.Code} {subjectAfterCode}",
            Body = deleting
                ? $"""
                    Your code to delete your account is {message.Code}

                    It works for {message.ValidMinutes} minutes. Deleting can't be undone. If you didn't ask for it,
                    don't share the code with anyone, and sign out of all devices from the Account screen.

                    Tournament Scheduler
                    """
                : $"""
                    Your sign-in code is {message.Code}

                    It works for {message.ValidMinutes} minutes. If you didn't ask for it, you can ignore this email —
                    nobody can sign in without the code.

                    Tournament Scheduler
                    """
        };
        mail.To.Add(message.To);

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

/// <summary>
/// For development and a LAN test server before email is set up: the code goes to the API log and
/// to a text file (<see cref="AuthOptions.DevCodeFile"/>) instead of an inbox. Anyone who can read
/// that file can sign in as anyone, so this must never be on for real users.
/// </summary>
public sealed class LogOtpSender(IOptions<AuthOptions> options, TimeProvider clock, ILogger<LogOtpSender> logger) : IOtpSender
{
    private static readonly Lock Gate = new();

    public Task SendAsync(OtpMessage message, CancellationToken cancellationToken = default)
    {
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
            logger.LogWarning("Couldn't write the sign-in code to {File}: {Reason}", file, e.Message);
        }

        return Task.CompletedTask;
    }
}
