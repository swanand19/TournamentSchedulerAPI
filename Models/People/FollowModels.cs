using System.Text.Json.Serialization;
using TournamentScheduler.Api.Models.Accounts;

namespace TournamentScheduler.Api.Models.People;

/// <summary>
/// One person following another (2026-11-23, item 6). It works at once, with no request (the user's choice): profiles
/// are already open to anyone signed in, so following only adds notifications. The followed person may remove a
/// follower, who isn't told. Both keys are accounts; neither cascades (two paths into Users), and account deletion
/// removes the rows itself.
/// </summary>
public class UserFollow
{
    public int FollowerUserId { get; set; }

    [JsonIgnore]
    public User? Follower { get; set; }

    public int FollowedUserId { get; set; }

    [JsonIgnore]
    public User? Followed { get; set; }

    public DateTime Crd { get; set; }
}

/// <summary>A person in a search or a followers list: who they are, where they play, and how they and the viewer follow.</summary>
public class PersonRowDto
{
    public int UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? PhotoUrl { get; set; }

    /// <summary>"Cricket · Karve Nagar Falcons": the sport and team of their latest squad place. Null with none.</summary>
    public string? Line { get; set; }

    /// <summary>The viewer follows them.</summary>
    public bool IsFollowing { get; set; }

    /// <summary>They follow the viewer ("Follows you", "Follow back").</summary>
    public bool FollowsMe { get; set; }

    public bool IsMe { get; set; }
}

/// <summary>Someone's followers or the people they follow, newest first.</summary>
public class PersonListDto
{
    public int UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Followers { get; set; }
    public int Following { get; set; }

    /// <summary>The list is the viewer's own: their followers may be removed.</summary>
    public bool IsMine { get; set; }

    public List<PersonRowDto> Items { get; set; } = [];
}

/// <summary>On a profile (signed in only): the counts, and how the viewer and this person follow each other.</summary>
public class FollowSummaryDto
{
    public int Followers { get; set; }
    public int Following { get; set; }
    public bool IsFollowing { get; set; }
    public bool FollowsMe { get; set; }
}

/// <summary>A match this person is playing now that the viewer may watch (a public tournament, or one they're in).</summary>
public class PlayingNowDto
{
    public int TournamentId { get; set; }
    public string TournamentName { get; set; } = string.Empty;
    public Sport Sport { get; set; }
    public int MatchId { get; set; }

    /// <summary>"Falcons v Kothrud Kings".</summary>
    public string Match { get; set; } = string.Empty;
}

/// <summary>After following or unfollowing: the person's counts as they are now.</summary>
public class FollowResultDto
{
    public int UserId { get; set; }

    [JsonPropertyName("follow")]
    public FollowSummaryDto Follow { get; set; } = new();
}
