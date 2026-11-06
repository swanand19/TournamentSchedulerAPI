using TournamentScheduler.Api.Models.Accounts;

namespace TournamentScheduler.Api.Models.Versions;

/// <summary>The phone platforms a release is published for. The web app and portal never appear: they always run the newest code.</summary>
public enum AppPlatform
{
    Android = 0,
    iOS = 1
}

/// <summary>
/// One released app version on one platform (HLD P-25). One backend serves every version, so this
/// isn't routing: it says which version is the latest, and whether everyone on an older one must
/// update. Admins keep it on the portal's "App versions" page.
/// </summary>
public class AppStart : AuditedEntity
{
    public int AppStartId { get; set; }

    public AppPlatform Platform { get; set; }

    /// <summary>As the store shows it: "1.8.1".</summary>
    public string AppVersion { get; set; } = string.Empty;

    /// <summary>
    /// The backend release it shipped with. Recorded only, never routed on — kept so a backend per
    /// version could be added later (P-25) without changing the apps.
    /// </summary>
    public string? ApiVersion { get; set; }

    /// <summary>"What's new", shown on the update banner and screen.</summary>
    public string? Description { get; set; }

    /// <summary>Once this release is out, everyone on an older version on this platform must update.</summary>
    public bool IsForceUpdate { get; set; }

    /// <summary>Not offered (and its force switch not applied) before this moment, UTC.</summary>
    public DateTime ReleaseDate { get; set; }

    /// <summary>Where to update from; the platform's store page from settings when empty.</summary>
    public string? StoreUrl { get; set; }
}

public enum ClientUpdateStatus
{
    /// <summary>The newest version, newer than the newest (a test build), a web or portal page, or nothing on the list yet.</summary>
    None,

    /// <summary>A newer version is out: a banner the person can dismiss.</summary>
    Suggested,

    /// <summary>A newer version that everyone below it must have is out: the app can't be used until it's updated.</summary>
    Required
}

/// <summary>What <c>AUTH_OPTIONS</c> tells an app about its own version.</summary>
public sealed class ClientUpdateDto
{
    public ClientUpdateStatus Status { get; set; }
    public string? LatestVersion { get; set; }
    public string? Description { get; set; }
    public string? StoreUrl { get; set; }

    /// <summary>The sentence the update screen shows.</summary>
    public string? Message { get; set; }
}

/// <summary>Where a release stands, as the portal's list shows it.</summary>
public enum AppStartState
{
    /// <summary>Entered ahead of its release date.</summary>
    Scheduled,

    /// <summary>The newest released version on its platform.</summary>
    Latest,

    /// <summary>Older than the latest; people on it see the "new version" banner.</summary>
    Supported,

    /// <summary>A newer release requires an update: people on it can't use the app.</summary>
    MustUpdate,

    /// <summary>Withdrawn (a pulled build): ignored everywhere.</summary>
    Withdrawn
}

public sealed class AppStartDto
{
    public int AppStartId { get; set; }
    public AppPlatform Platform { get; set; }
    public string AppVersion { get; set; } = string.Empty;
    public string? ApiVersion { get; set; }
    public string? Description { get; set; }
    public bool IsForceUpdate { get; set; }
    public DateTime ReleaseDate { get; set; }
    public string? StoreUrl { get; set; }
    public bool IsDisabled { get; set; }
    public AppStartState State { get; set; }

    /// <summary>Devices signed in on this version that were used in the last 30 days.</summary>
    public int Devices { get; set; }

    public string? ChangedBy { get; set; }
    public DateTime ChangedAt { get; set; }
}

/// <summary>Devices on one version (on the list or not), for the portal's "how many would this block" sums.</summary>
public sealed class VersionInUseDto
{
    public AppPlatform Platform { get; set; }
    public string AppVersion { get; set; } = string.Empty;
    public int Devices { get; set; }
}

public sealed class PlatformSummaryDto
{
    public AppPlatform Platform { get; set; }
    public string? LatestVersion { get; set; }

    /// <summary>The newest release with the force switch on: everything below it must update.</summary>
    public string? RequiredFrom { get; set; }

    /// <summary>Devices used in the last 30 days on a version below <see cref="RequiredFrom"/>.</summary>
    public int DevicesBlocked { get; set; }

    /// <summary>Devices used in the last 30 days on a version older than the latest but still allowed.</summary>
    public int DevicesBehind { get; set; }
}

/// <summary>The whole page: every release, newest first, plus a summary per platform and what's in use.</summary>
public sealed class AppVersionListDto
{
    public List<AppStartDto> Items { get; set; } = [];
    public List<PlatformSummaryDto> Platforms { get; set; } = [];
    public List<VersionInUseDto> InUse { get; set; } = [];

    /// <summary>How far back device counts look.</summary>
    public int ActiveDays { get; set; }

    /// <summary>The store pages used when a release has no link of its own.</summary>
    public Dictionary<string, string> DefaultStoreUrls { get; set; } = [];
}

public sealed class SaveAppStartRequest
{
    public AppPlatform? Platform { get; set; }
    public string? AppVersion { get; set; }
    public string? ApiVersion { get; set; }
    public string? Description { get; set; }
    public bool IsForceUpdate { get; set; }

    /// <summary>UTC. Required.</summary>
    public DateTime? ReleaseDate { get; set; }

    public string? StoreUrl { get; set; }
}

public sealed class SetAppStartDisabledRequest
{
    public bool Disabled { get; set; }
}
