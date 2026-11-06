using System.Threading.RateLimiting;
using TournamentScheduler.Api.Models.Public;

namespace TournamentScheduler.Api.Http;

/// <summary>
/// The limit on the public pages (6E): anyone may call them without an account, so each address
/// gets <see cref="PublicOptions.RequestsPerMinute"/> a minute (a fixed window). Over it: 429 in
/// the usual envelope ("Too many requests. Wait a moment and try again."). Only the public
/// services carry the policy; the signed-in apps are never limited by it.
/// </summary>
public static class PublicRateLimit
{
    public const string Policy = "public";

    public static IServiceCollection AddPublicRateLimit(this IServiceCollection services, IConfiguration configuration)
    {
        var perMinute = Math.Max(1, configuration.GetSection(PublicOptions.Section).Get<PublicOptions>()?.RequestsPerMinute ?? new PublicOptions().RequestsPerMinute);
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(Policy, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
        return services;
    }
}
