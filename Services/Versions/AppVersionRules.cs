using System.Text.RegularExpressions;
using TournamentScheduler.Api.Models.Versions;

namespace TournamentScheduler.Api.Services.Versions;

/// <summary>One release as the rules see it: enough to decide, nothing to track.</summary>
public sealed record AppRelease(int AppStartId, AppPlatform Platform, string AppVersion, bool IsForceUpdate, DateTime ReleaseDate, bool IsDisabled, string? Description, string? StoreUrl);

/// <summary>
/// Which app versions may still be used (HLD P-25, LLD B.12.1). Pure: the releases and the clock in,
/// an answer out — the gateway, <c>AUTH_OPTIONS</c> and the portal all ask here, so they never disagree.
///
/// A release counts once its date has passed and while it isn't withdrawn. The newest such release is
/// the latest. An app below a counting release that has the force switch on must update; an app
/// below the latest otherwise is told a new version exists. Versions are compared as numbers
/// (1.10.0 is newer than 1.9.9), and a version missing from the list is compared all the same.
/// </summary>
public static partial class AppVersionRules
{
    /// <summary>Calls an app that must update can still make: the update screen needs the first, and nobody is kept from signing out.</summary>
    public static readonly IReadOnlySet<string> AlwaysAllowed = new HashSet<string>(StringComparer.Ordinal)
    {
        "AUTH_OPTIONS", "HEALTH_CHECK", "AUTH_LOGOUT", "AUTH_LOGOUT_ALL", "ACCOUNT_GET"
    };

    public const int MaxVersionLength = 20;

    [GeneratedRegex(@"^\d{1,5}(\.\d{1,5}){1,3}$")]
    private static partial Regex VersionShape();

    /// <summary>"1.8.1" as a comparable version (missing parts are 0); null when it isn't numbers with dots.</summary>
    public static Version? Parse(string? text)
    {
        var value = text?.Trim();
        if (string.IsNullOrEmpty(value) || value.Length > MaxVersionLength || !VersionShape().IsMatch(value)) return null;
        var parts = value.Split('.').Select(int.Parse).ToList();
        while (parts.Count < 4) parts.Add(0);
        return new Version(parts[0], parts[1], parts[2], parts[3]);
    }

    /// <summary>Which platform a gateway channel is; null for the web app, the portal and tests (never asked to update).</summary>
    public static AppPlatform? PlatformOf(string? channel) => channel?.Trim().ToUpperInvariant() switch
    {
        "MOBILE_ANDROID" or "ANDROID" => AppPlatform.Android,
        "MOBILE_IOS" or "IOS" => AppPlatform.iOS,
        _ => null
    };

    public static string StoreName(AppPlatform platform) => platform == AppPlatform.iOS ? "the App Store" : "Google Play";

    public static string RequiredMessage(AppPlatform platform) =>
        $"This version of the app is no longer supported. Please update it from {StoreName(platform)}.";

    /// <summary>The releases that count now on a platform, newest first.</summary>
    public static List<(AppRelease Release, Version Version)> Counting(IEnumerable<AppRelease> releases, AppPlatform platform, DateTime nowUtc) =>
        releases
            .Where(r => r.Platform == platform && !r.IsDisabled && r.ReleaseDate <= nowUtc)
            .Select(r => (Release: r, Version: Parse(r.AppVersion)))
            .Where(x => x.Version != null)
            .Select(x => (x.Release, x.Version!))
            .OrderByDescending(x => x.Item2)
            .ToList();

    /// <summary>The newest counting release with the force switch on: everything below it must update.</summary>
    public static (AppRelease Release, Version Version)? RequiredFrom(IEnumerable<(AppRelease Release, Version Version)> counting) =>
        counting.Where(x => x.Release.IsForceUpdate).Select(x => ((AppRelease, Version)?)x).FirstOrDefault();

    /// <summary>What an app on <paramref name="appVersion"/> should be told.</summary>
    public static ClientUpdateDto Evaluate(IEnumerable<AppRelease> releases, AppPlatform? platform, string? appVersion, DateTime nowUtc, string? defaultStoreUrl)
    {
        var none = new ClientUpdateDto { Status = ClientUpdateStatus.None };
        if (platform is not { } p) return none;

        var counting = Counting(releases, p, nowUtc);
        if (counting.Count == 0) return none;

        var (latest, latestVersion) = counting[0];
        var answer = new ClientUpdateDto
        {
            LatestVersion = latest.AppVersion,
            Description = latest.Description,
            StoreUrl = string.IsNullOrWhiteSpace(latest.StoreUrl) ? defaultStoreUrl : latest.StoreUrl
        };

        // A build we can't read ("unknown", a dev build) is never locked out: this is a nudge, not a lock.
        if (Parse(appVersion) is not { } mine || mine >= latestVersion) return answer.With(ClientUpdateStatus.None, null);

        if (RequiredFrom(counting) is { } required && mine < required.Version)
            return answer.With(ClientUpdateStatus.Required, RequiredMessage(p));

        return answer.With(ClientUpdateStatus.Suggested, $"Version {latest.AppVersion} is available.");
    }

    /// <summary>Where one release stands among the others on its platform.</summary>
    public static AppStartState StateOf(AppRelease release, IEnumerable<AppRelease> all, DateTime nowUtc)
    {
        if (release.IsDisabled) return AppStartState.Withdrawn;
        if (release.ReleaseDate > nowUtc) return AppStartState.Scheduled;

        var counting = Counting(all, release.Platform, nowUtc);
        var mine = Parse(release.AppVersion);
        if (counting.Count == 0 || mine == null) return AppStartState.Supported;
        if (counting[0].Release.AppStartId == release.AppStartId) return AppStartState.Latest;
        return RequiredFrom(counting) is { } required && mine < required.Version ? AppStartState.MustUpdate : AppStartState.Supported;
    }

    private static ClientUpdateDto With(this ClientUpdateDto dto, ClientUpdateStatus status, string? message)
    {
        dto.Status = status;
        dto.Message = message;
        return dto;
    }
}
