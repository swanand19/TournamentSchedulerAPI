using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Models.Profiles;

namespace TournamentScheduler.Api.Models.Public;

// Public live scores (phase 6E): an owner's switch lets anyone with the tournament's link watch it
// without an account; signed-in people can follow it.

/// <summary>
/// A signed-in person following a public tournament: it shows on their Home and they may view it
/// (watch, tables, stats) for as long as it stays public. Nothing more — following isn't a role.
/// </summary>
public class TournamentFollow
{
    public int TournamentFollowId { get; set; }
    public int TournamentId { get; set; }
    public int UserId { get; set; }
    public DateTime Crd { get; set; }
}

/// <summary>Settings for the public pages (<c>Public</c> section).</summary>
public class PublicOptions
{
    public const string Section = "Public";

    /// <summary>
    /// Where the web app is served, e.g. "https://scores.example.com". Links are this plus
    /// "/live/{code}". Empty: the apps show the code, and the web app uses its own address.
    /// </summary>
    public string WebAppUrl { get; set; } = "";

    /// <summary>Public requests allowed per address per minute, so a crowd can't overwhelm the database.</summary>
    public int RequestsPerMinute { get; set; } = 120;

    /// <summary>How long a public match answer is reused: a crowd watching one match costs one read, not one each.</summary>
    public int CacheSeconds { get; set; } = 2;
}

/// <summary>The tournament's public link, on its own answer while it is public.</summary>
public class PublicShareDto
{
    public string Code { get; set; } = string.Empty;

    /// <summary>The whole link, when the server knows where the web app is (<see cref="PublicOptions.WebAppUrl"/>).</summary>
    public string? Url { get; set; }
}

public class SetPublicRequest
{
    public bool IsPublic { get; set; }
}

public class FollowRequest
{
    /// <summary>The code, or the whole link it is in.</summary>
    public string? Code { get; set; }
}

/// <summary>The tournament just followed, for the app to open.</summary>
public class FollowedDto
{
    public int TournamentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public Sport Sport { get; set; }
    public bool IsStarted { get; set; }
}

/// <summary>A public tournament found by name or code (FOLLOW_SEARCH): enough to tell two of the same name apart.</summary>
public class PublicSearchResultDto
{
    public int TournamentId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Sport Sport { get; set; }
    public TournamentStatus Status { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public int Teams { get; set; }

    /// <summary>They follow it already.</summary>
    public bool IsFollowing { get; set; }

    /// <summary>They run, score or play in it: it's on their Home already.</summary>
    public bool IsMine { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public int CreatedByUserId { get; set; }
}

/// <summary>A public tournament's header: what anyone with the link sees first.</summary>
public class PublicTournamentDto
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Sport Sport { get; set; }
    public TournamentStatus Status { get; set; }
    public bool IsStarted { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public bool HasKnockouts { get; set; }
}

/// <summary>A player as people without an account see them: the name and the top-level sport cards only (decided 2026-10-13).</summary>
public class PublicPlayerDto
{
    public string Name { get; set; } = string.Empty;
    public List<SportCardDto> Sports { get; set; } = [];
}
