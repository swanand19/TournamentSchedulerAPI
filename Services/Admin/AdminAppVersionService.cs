using Microsoft.Extensions.Options;
using TournamentScheduler.Api.Auth;
using TournamentScheduler.Api.Data;
using TournamentScheduler.Api.Data.DataServices;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Models.Versions;
using TournamentScheduler.Api.Services.Versions;

namespace TournamentScheduler.Api.Services.Admin;

public interface IAdminAppVersionService
{
    Task<ServiceResult<AppVersionListDto>> ListAsync();
    Task<ServiceResult<AppVersionListDto>> CreateAsync(SaveAppStartRequest request);
    Task<ServiceResult<AppVersionListDto>> UpdateAsync(int appStartId, SaveAppStartRequest request);
    Task<ServiceResult<AppVersionListDto>> SetDisabledAsync(int appStartId, bool disabled);
}

/// <summary>
/// The portal's "App versions" page (HLD P-25): every release of the phone app on every platform in
/// one list, newest first, with where each stands and how many devices still use it — so an admin
/// can see who a forced update would lock out before switching it on. Every change answers with the
/// whole list, because one release's force switch changes where the others stand.
/// </summary>
public class AdminAppVersionService(
    IDataService<AppStart> releases,
    IDataService<UserSession> sessions,
    IDataService<User> users,
    IUnitOfWork unitOfWork,
    AppVersionCache cache,
    IOptions<AppVersionOptions> options,
    ICurrentUser currentUser,
    TimeProvider clock) : ServiceBase, IAdminAppVersionService
{
    /// <summary>Devices count when used this recently: older ones have probably been replaced or uninstalled.</summary>
    public const int ActiveDays = 30;

    public const int DescriptionMax = 500;
    public const int ApiVersionMax = 20;
    public const int StoreUrlMax = 500;

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<ServiceResult<AppVersionListDto>> ListAsync() => Success(await BuildAsync());

    public async Task<ServiceResult<AppVersionListDto>> CreateAsync(SaveAppStartRequest request)
    {
        if (await ProblemAsync(request, null) is { } problem) return problem;

        var row = new AppStart();
        Apply(row, request);
        row.Created(Now, currentUser.UserId);
        releases.Add(row);
        return await SaveAsync();
    }

    public async Task<ServiceResult<AppVersionListDto>> UpdateAsync(int appStartId, SaveAppStartRequest request)
    {
        var row = await releases.GetByIdAsync(appStartId);
        if (row == null) return NotFound("That app version isn't on the list any more.");
        if (await ProblemAsync(request, appStartId) is { } problem) return problem;

        Apply(row, request);
        row.Modified(Now, currentUser.UserId);
        return await SaveAsync();
    }

    public async Task<ServiceResult<AppVersionListDto>> SetDisabledAsync(int appStartId, bool disabled)
    {
        var row = await releases.GetByIdAsync(appStartId);
        if (row == null) return NotFound("That app version isn't on the list any more.");
        if (row.IsDisabled == disabled) return Success(await BuildAsync());

        if (disabled)
        {
            // Withdrawing the only released version would leave the platform with no latest at all.
            var others = await releases.QueryAsync(q => q
                .Where(a => a.Platform == row.Platform && a.AppStartId != row.AppStartId && !a.IsDisabled)
                .Select(a => a.ReleaseDate), tracking: false);
            if (row.ReleaseDate <= Now && !others.Any(d => d <= Now))
                return Conflict($"{row.Platform} {row.AppVersion} is the only released version. Add the next release before withdrawing it.");
        }

        row.IsDisabled = disabled;
        row.Modified(Now, currentUser.UserId);
        return await SaveAsync();
    }

    // -----------------------------------------------------------------

    private async Task<ServiceResult<AppVersionListDto>> SaveAsync()
    {
        try
        {
            await unitOfWork.SaveChangesAsync();
        }
        catch (DuplicateEntryException)
        {
            return Conflict("That version is already on the list for this platform.");
        }
        cache.Invalidate();
        return Success(await BuildAsync());
    }

    /// <summary>The rule a new or edited release breaks, or null. Messages are shown in the form as they are.</summary>
    private async Task<ServiceError?> ProblemAsync(SaveAppStartRequest request, int? editing)
    {
        if (request.Platform is not { } platform || !Enum.IsDefined(platform)) return BadRequest("Choose Android or iOS.");

        var version = request.AppVersion?.Trim();
        if (string.IsNullOrEmpty(version)) return BadRequest("Enter the version as the store shows it, like 1.8.1.");
        if (AppVersionRules.Parse(version) == null) return BadRequest("Write the version as numbers separated by dots, like 1.8.1.");

        if (request.ReleaseDate == null) return BadRequest("Choose the release date.");
        if (request.ApiVersion?.Trim() is { Length: > ApiVersionMax }) return BadRequest($"The API version can be at most {ApiVersionMax} characters.");
        if (request.Description?.Trim() is { Length: > DescriptionMax }) return BadRequest($"\"What's new\" can be at most {DescriptionMax} characters.");

        var url = request.StoreUrl?.Trim();
        if (!string.IsNullOrEmpty(url)
            && (url.Length > StoreUrlMax || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")))
            return BadRequest("The store link must be a full web address starting with https://.");

        // "1.8" and "1.8.0" are the same release: compare by number, not by text.
        var parsed = AppVersionRules.Parse(version);
        var samePlatform = await releases.QueryAsync(q => q
            .Where(a => a.Platform == platform && a.AppStartId != (editing ?? 0))
            .Select(a => a.AppVersion), tracking: false);
        if (samePlatform.Any(v => AppVersionRules.Parse(v) == parsed))
            return Conflict($"{platform} {version} is already on the list.");

        return null;
    }

    private static void Apply(AppStart row, SaveAppStartRequest request)
    {
        row.Platform = request.Platform!.Value;
        row.AppVersion = request.AppVersion!.Trim();
        row.ApiVersion = Clean(request.ApiVersion);
        row.Description = Clean(request.Description);
        row.IsForceUpdate = request.IsForceUpdate;
        row.ReleaseDate = DateTime.SpecifyKind(request.ReleaseDate!.Value.ToUniversalTime(), DateTimeKind.Utc);
        row.StoreUrl = Clean(request.StoreUrl);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task<AppVersionListDto> BuildAsync()
    {
        var now = Now;
        var rows = await releases.ListAsync(tracking: false);
        var all = rows.Select(r => new AppRelease(r.AppStartId, r.Platform, r.AppVersion, r.IsForceUpdate, r.ReleaseDate, r.IsDisabled, r.Description, r.StoreUrl)).ToList();
        var inUse = await InUseAsync(now);
        var names = await NamesOf(rows.Select(r => r.LmdBy));

        var items = rows
            .Select(r =>
            {
                var release = all.First(a => a.AppStartId == r.AppStartId);
                var parsed = AppVersionRules.Parse(r.AppVersion);
                return new AppStartDto
                {
                    AppStartId = r.AppStartId,
                    Platform = r.Platform,
                    AppVersion = r.AppVersion,
                    ApiVersion = r.ApiVersion,
                    Description = r.Description,
                    IsForceUpdate = r.IsForceUpdate,
                    ReleaseDate = r.ReleaseDate,
                    StoreUrl = r.StoreUrl,
                    IsDisabled = r.IsDisabled,
                    State = AppVersionRules.StateOf(release, all, now),
                    Devices = inUse.Where(u => u.Platform == r.Platform && AppVersionRules.Parse(u.AppVersion) == parsed).Sum(u => u.Devices),
                    ChangedBy = names.GetValueOrDefault(r.LmdBy),
                    ChangedAt = r.Lmd
                };
            })
            // Newest release first, the newer version first on the same day.
            .OrderByDescending(i => i.ReleaseDate.Date)
            .ThenByDescending(i => AppVersionRules.Parse(i.AppVersion))
            .ThenBy(i => i.Platform)
            .ToList();

        var platforms = Enum.GetValues<AppPlatform>().Select(p =>
        {
            var counting = AppVersionRules.Counting(all, p, now);
            var required = AppVersionRules.RequiredFrom(counting);
            var latest = counting.Count > 0 ? counting[0].Version : null;
            var mine = inUse.Where(u => u.Platform == p).Select(u => (u.Devices, Version: AppVersionRules.Parse(u.AppVersion))).ToList();
            var blocked = required == null ? 0 : mine.Where(u => u.Version != null && u.Version < required.Value.Version).Sum(u => u.Devices);
            var behind = latest == null ? 0 : mine.Where(u => u.Version != null && u.Version < latest).Sum(u => u.Devices) - blocked;
            return new PlatformSummaryDto
            {
                Platform = p,
                LatestVersion = counting.Count > 0 ? counting[0].Release.AppVersion : null,
                RequiredFrom = required?.Release.AppVersion,
                DevicesBlocked = blocked,
                DevicesBehind = behind
            };
        }).ToList();

        var defaults = Enum.GetValues<AppPlatform>()
            .Select(p => (Key: p.ToString(), Url: options.Value.StoreUrlFor(p)))
            .Where(x => x.Url != null)
            .ToDictionary(x => x.Key, x => x.Url!);

        return new AppVersionListDto { Items = items, Platforms = platforms, InUse = inUse, ActiveDays = ActiveDays, DefaultStoreUrls = defaults };
    }

    /// <summary>Devices per platform and version: sessions still signed in, used in the last <see cref="ActiveDays"/> days, one per device.</summary>
    private async Task<List<VersionInUseDto>> InUseAsync(DateTime now)
    {
        var since = now.AddDays(-ActiveDays);
        var live = await sessions.QueryAsync(q => q
            .Where(s => s.RevokedAt == null && s.ExpiresAt > now && s.LastSeenAt >= since && s.AppVersion != null && !s.IsPortal)
            .Select(s => new { s.Channel, s.AppVersion, Device = s.DeviceId ?? s.SessionId.ToString() }), tracking: false);

        return live
            .Select(s => (Platform: AppVersionRules.PlatformOf(s.Channel), s.AppVersion, s.Device))
            .Where(s => s.Platform != null)
            .GroupBy(s => (Platform: s.Platform!.Value, Version: s.AppVersion!))
            .Select(g => new VersionInUseDto { Platform = g.Key.Platform, AppVersion = g.Key.Version, Devices = g.Select(s => s.Device).Distinct().Count() })
            .OrderBy(v => v.Platform)
            .ThenByDescending(v => AppVersionRules.Parse(v.AppVersion))
            .ToList();
    }

    private async Task<Dictionary<int, string>> NamesOf(IEnumerable<int> userIds)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0) return [];
        var found = await users.QueryAsync(q => q.Where(u => ids.Contains(u.UserId)).Select(u => new { u.UserId, u.Name }), tracking: false);
        return found.ToDictionary(u => u.UserId, u => u.Name);
    }
}
