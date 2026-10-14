using Microsoft.Extensions.Options;
using TournamentScheduler.Api.Auth;
using TournamentScheduler.Api.Auth.Otp;
using TournamentScheduler.Api.Data;
using TournamentScheduler.Api.Data.DataServices;
using TournamentScheduler.Api.Models.Accounts;

namespace TournamentScheduler.Api.Services.Accounts;

public interface IOtpCodeService
{
    /// <summary>Makes, stores (hashed) and sends a code for <paramref name="purpose"/>, within the send limits.</summary>
    Task<ServiceResult<SendOtpResponse>> SendAsync(Contact to, OtpPurpose purpose, ClientInfo client);

    /// <summary>
    /// Checks a code typed for <paramref name="purpose"/>. Right: the waiting challenge, for the
    /// caller to mark used when it acts on it. Wrong: why, with the tries left (the try is counted).
    /// </summary>
    Task<(OtpChallenge? Challenge, ServiceError? Error)> CheckAsync(Contact to, OtpPurpose purpose, string? otp);
}

/// <summary>
/// One-time codes, whatever they confirm (signing in, deleting the account, adding an email or
/// number) and however they travel (email, text message). The rules are in Auth/README.md: 6 digits,
/// 5 minutes, 5 tries, one use, a new code cancels the last one, and send limits per email or
/// number, device and network address.
/// </summary>
public class OtpCodeService : ServiceBase, IOtpCodeService
{
    private readonly IDataService<OtpChallenge> _challenges;
    private readonly IOtpSender _sender;
    private readonly IUnitOfWork _unitOfWork;
    private readonly AuthOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<OtpCodeService> _logger;

    public OtpCodeService(
        IDataService<OtpChallenge> challenges,
        IOtpSender sender,
        IUnitOfWork unitOfWork,
        IOptions<AuthOptions> options,
        TimeProvider clock,
        ILogger<OtpCodeService> logger)
    {
        _challenges = challenges;
        _sender = sender;
        _unitOfWork = unitOfWork;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public async Task<ServiceResult<SendOtpResponse>> SendAsync(Contact to, OtpPurpose purpose, ClientInfo client)
    {
        var now = Now;
        var hourAgo = now.AddHours(-1);
        var target = to.Value;

        var recentForTarget = await _challenges.QueryAsync(q => q
            .Where(c => c.Target == target && c.Crd > hourAgo)
            .Select(c => c.Crd), tracking: false);

        var lastSent = recentForTarget.Count > 0 ? recentForTarget.Max() : (DateTime?)null;
        if (lastSent != null && now - lastSent.Value < TimeSpan.FromSeconds(_options.OtpResendSeconds))
        {
            var wait = (int)Math.Ceiling((TimeSpan.FromSeconds(_options.OtpResendSeconds) - (now - lastSent.Value)).TotalSeconds);
            return TooManyRequests($"A code was just sent. You can ask for another in {wait} seconds.");
        }
        if (recentForTarget.Count >= (to.IsEmail ? _options.OtpPerEmailPerHour : _options.OtpPerPhonePerHour))
            return TooManyRequests($"Too many codes have been sent to this {(to.IsEmail ? "email" : "number")}. Try again in an hour.");

        if (client.DeviceId != null
            && await _challenges.CountAsync(c => c.DeviceId == client.DeviceId && c.Crd > hourAgo) >= _options.OtpPerDevicePerHour)
            return TooManyRequests("Too many codes have been asked for from this device. Try again in an hour.");

        if (client.Ip != null
            && await _challenges.CountAsync(c => c.ClientIp == client.Ip && c.Crd > hourAgo) >= _options.OtpPerIpPerHour)
            return TooManyRequests("Too many codes have been asked for from this network. Try again in an hour.");

        // A new code cancels any earlier one for the same thing still waiting.
        var waiting = await _challenges.ListAsync(c => c.Target == target && c.Purpose == purpose && c.ConsumedAt == null && c.ExpiresAt > now);
        foreach (var old in waiting) old.ExpiresAt = now;

        var code = SessionTokens.NewCode(_options.OtpLength);
        var challenge = new OtpChallenge
        {
            Purpose = purpose,
            Channel = to.Channel,
            Target = target,
            CodeHash = SessionTokens.HashCode(target, code),
            ExpiresAt = now.AddSeconds(_options.OtpLifetimeSeconds),
            DeviceId = client.DeviceId is { Length: > 100 } d ? d[..100] : client.DeviceId,
            ClientIp = client.Ip,
            Crd = now
        };
        _challenges.Add(challenge);
        await _unitOfWork.SaveChangesAsync();

        try
        {
            await _sender.SendAsync(new OtpMessage(target, code, Math.Max(1, _options.OtpLifetimeSeconds / 60), purpose, to.Channel));
        }
        catch (Exception e)
        {
            // Not sent, so it mustn't count against the limits either.
            if (purpose == OtpPurpose.DeleteAccount) _logger.LogError(e, "Couldn't send an account deletion code.");
            else if (purpose == OtpPurpose.AddContact) _logger.LogError(e, "Couldn't send a code to add an email or number.");
            else _logger.LogError(e, "Couldn't send a sign-in code.");
            _challenges.Remove(challenge);
            await _unitOfWork.SaveChangesAsync();
            return Unavailable(to.IsEmail
                ? "We couldn't send the email just now. Try again in a minute."
                : "We couldn't send the text message just now. Try again in a minute.");
        }

        return Success(new SendOtpResponse
        {
            SentTo = target,
            Channel = to.Channel,
            Email = to.IsEmail ? target : null,
            ExpiresInSeconds = _options.OtpLifetimeSeconds,
            ResendAfterSeconds = _options.OtpResendSeconds
        });
    }

    public async Task<(OtpChallenge? Challenge, ServiceError? Error)> CheckAsync(Contact to, OtpPurpose purpose, string? otp)
    {
        var code = new string((otp ?? "").Where(ch => !char.IsWhiteSpace(ch)).ToArray());
        if (code.Length != _options.OtpLength || !code.All(char.IsAsciiDigit))
            return (null, BadRequest($"Enter the {_options.OtpLength}-digit code from {to.CodeArrivesIn}."));

        var now = Now;
        var target = to.Value;
        var challenges = await _challenges.ListAsync(
            c => c.Target == target && c.Purpose == purpose && c.ConsumedAt == null,
            q => q.OrderByDescending(c => c.Crd));
        var challenge = challenges.FirstOrDefault();

        if (challenge == null || challenge.ExpiresAt <= now)
            return (null, BadRequest("This code has expired. Ask for a new one."));
        if (challenge.Attempts >= _options.OtpMaxAttempts)
            return (null, BadRequest("Too many wrong tries. Ask for a new code."));

        if (!SessionTokens.Matches(challenge.CodeHash, SessionTokens.HashCode(target, code)))
        {
            challenge.Attempts++;
            await _unitOfWork.SaveChangesAsync();

            var left = _options.OtpMaxAttempts - challenge.Attempts;
            return (null, BadRequest(left > 0
                ? $"That code isn't right. {left} {(left == 1 ? "try" : "tries")} left."
                : "That code isn't right, and it can't be used again. Ask for a new one."));
        }

        return (challenge, null);
    }
}
