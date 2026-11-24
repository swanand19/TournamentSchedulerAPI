using System.Text.Json.Serialization;
using TournamentScheduler.Api.Models.Accounts;

namespace TournamentScheduler.Api.Models.Notifications;

/// <summary>What a notification is about. The apps pick its icon from this; the words are in the row.</summary>
public enum NotificationKind
{
    ScoringRequested = 0,
    ScoringAllowed = 1,
    ScoringDeclined = 2,
    ScoringTakenOver = 3,
    AddedToTournament = 4,
    InviteAccepted = 5,
    LinkedToTeam = 6,
    AllMatchesPlayed = 7,
    EndDateReminder = 8,
    Auction = 9,
    Draw = 10,

    /// <summary>Lasting teams (2026-11-20): added to one, made its admin, asked to keep a tournament's team.</summary>
    Club = 11,

    /// <summary>Following people (2026-11-23): someone followed you, or someone you follow is playing.</summary>
    Social = 12,

    /// <summary>Leagues (2026-11-24): made one of a league's organisers.</summary>
    League = 13
}

/// <summary>
/// The inbox's chips (2026-11-23): Social, Scoring, Tournaments, Teams. Worked out from the kind, so older rows fall
/// into place without a column; auction and draw news count as Tournaments.
/// </summary>
public static class NotificationCategories
{
    public const string Social = "Social";
    public const string Scoring = "Scoring";
    public const string Tournaments = "Tournaments";
    public const string Teams = "Teams";

    public static readonly string[] All = [Social, Scoring, Tournaments, Teams];

    public static string Of(NotificationKind kind) => kind switch
    {
        NotificationKind.Social => Social,
        NotificationKind.ScoringRequested or NotificationKind.ScoringAllowed or NotificationKind.ScoringDeclined or NotificationKind.ScoringTakenOver => Scoring,
        NotificationKind.LinkedToTeam or NotificationKind.Club => Teams,
        _ => Tournaments
    };

    /// <summary>The kinds in a category, or null for a name that isn't one (then the whole inbox).</summary>
    public static NotificationKind[]? KindsOf(string? category)
    {
        var match = All.FirstOrDefault(c => string.Equals(c, category?.Trim(), StringComparison.OrdinalIgnoreCase));
        return match == null ? null : Enum.GetValues<NotificationKind>().Where(k => Of(k) == match).ToArray();
    }
}

/// <summary>
/// One line in a person's inbox (phase 6D). The words are written when it's made, so a later rename
/// doesn't change what was said. <see cref="TournamentId"/> (and a match) say where tapping it goes;
/// there's no foreign key on purpose — a deleted tournament leaves its notifications behind, and
/// opening one says the tournament is gone.
/// </summary>
public class Notification
{
    public long NotificationId { get; set; }

    public int UserId { get; set; }

    [JsonIgnore]
    public User? User { get; set; }

    public NotificationKind Kind { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;

    public int? TournamentId { get; set; }
    public Sport? Sport { get; set; }
    public int? MatchId { get; set; }

    /// <summary>A team inside the tournament (opens its page), or a lasting team (opens the club's page); 2026-11-20.</summary>
    public int? TeamId { get; set; }

    public int? ClubId { get; set; }

    /// <summary>A club player the news is about (someone joined with the code): opens the live-tournaments question for them.</summary>
    public int? ClubPlayerId { get; set; }

    /// <summary>A person the news is about (someone followed you, 2026-11-23): opens their profile. No key, like the rest.</summary>
    public int? ProfileUserId { get; set; }

    /// <summary>A league the news is about (made an organiser, 2026-11-24): opens its page. No key, like the rest.</summary>
    public int? LeagueId { get; set; }

    /// <summary>
    /// Stops the same news twice — the "all matches played" prompt, a reminder for one end date. Every
    /// recipient's copy carries the same key.
    /// </summary>
    public string? DedupeKey { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }

    /// <summary>
    /// Shown in the inbox. False when its template sends it by email only — the row is then just the
    /// email waiting to go (and still stops the same news twice).
    /// </summary>
    public bool InInbox { get; set; } = true;

    // --- Email, when the template sends one: written when the row is made, sent by the email job ---

    public string? EmailTo { get; set; }
    public string? EmailSubject { get; set; }
    public string? EmailBody { get; set; }
    public DateTime? EmailSentAt { get; set; }
    public int EmailAttempts { get; set; }
}

public class NotificationDto
{
    public long NotificationId { get; set; }
    public NotificationKind Kind { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public int? TournamentId { get; set; }
    public Sport? Sport { get; set; }
    public int? MatchId { get; set; }

    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? TeamId { get; set; }

    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? ClubId { get; set; }

    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? ClubPlayerId { get; set; }

    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? ProfileUserId { get; set; }

    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? LeagueId { get; set; }

    /// <summary>Social, Scoring, Tournaments or Teams: the chip it sits under.</summary>
    public string Category { get; set; } = NotificationCategories.Tournaments;

    public DateTime CreatedAt { get; set; }
    public bool IsRead { get; set; }
}

public class NotificationPageDto
{
    public List<NotificationDto> Items { get; set; } = [];
    public int UnreadCount { get; set; }

    /// <summary>Older ones exist: ask again with <c>before</c> = the last item's id.</summary>
    public bool HasMore { get; set; }

    /// <summary>Unread ones per chip (2026-11-23), whatever category was asked for.</summary>
    public Dictionary<string, int> UnreadByCategory { get; set; } = [];
}

public class UnreadCountDto
{
    public int UnreadCount { get; set; }
}
