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
    EndDateReminder = 8
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
    public DateTime CreatedAt { get; set; }
    public bool IsRead { get; set; }
}

public class NotificationPageDto
{
    public List<NotificationDto> Items { get; set; } = [];
    public int UnreadCount { get; set; }

    /// <summary>Older ones exist: ask again with <c>before</c> = the last item's id.</summary>
    public bool HasMore { get; set; }
}

public class UnreadCountDto
{
    public int UnreadCount { get; set; }
}
