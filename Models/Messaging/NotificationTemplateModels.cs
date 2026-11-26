using TournamentScheduler.Api.Models.Accounts;

namespace TournamentScheduler.Api.Models.Messaging;

/// <summary>
/// Every message the system sends, by what it's for. The code is the key the API looks the wording
/// up by; renaming one is a breaking change for the rows already saved.
/// </summary>
public enum NotificationTemplateCode
{
    // One-time codes: by email or text, whichever the person signs in with.
    SignInCode,
    AddContactCode,
    DeleteAccountCode,

    // Owners and scorers without an account yet.
    TournamentInvite,

    // Scoring handovers.
    ScoringRequested,
    ScoringAllowed,
    ScoringDeclined,
    ScoringTakenOver,
    ScoringPassedOn,

    // People and teams.
    AddedAsOwner,
    AddedAsScorer,
    InviteJoined,
    InviteAccepted,
    AddedToTeam,
    JoinedTeamOnSignUp,

    // The tournament itself.
    AllMatchesPlayed,
    EndDateMatchesLeft,
    EndDateAllPlayed,
    EndDateNotStarted,

    // The player auction (2026-11-17).
    AuctionStarted,
    AuctionBought,

    // The team draw (2026-11-17).
    DrawStarted,
    DrawPicked,
    AuctionBidderReplaced,
    AuctionBidderNow,

    // Team admins (2026-11-20).
    MadeTeamAdmin,

    // Lasting teams (2026-11-20, step 2 of docs/reusable-teams-plan.md). People see "team"; the code says club.
    AddedToClub,
    MadeClubAdmin,
    KeepTeam,

    // Entering tournaments with a code, and club changes reaching squads (2026-11-21, steps 3 and 4).
    EntryRequested,
    EntryAccepted,
    EntryDeclined,
    EntryCancelled,
    ClubPlayerJoined,
    Followed,
    FollowedPlaying,

    // Leagues (2026-11-24).
    MadeLeagueOrganiser
}

/// <summary>
/// The wording of one kind of message, per channel, editable in the admin portal (phase 6D). Texts
/// use <c>{{Placeholder}}</c>s, filled in when the message is made; which ones a template may use is
/// fixed in <c>Messaging/NotificationTemplateCatalog</c>, which also holds the built-in wording a row
/// starts from and goes back to on reset. A row is created for every code at start-up.
///
/// The <c>Is…</c> switches say which channels it goes out on. Codes and invites always go by email
/// or text — whichever the person gave — so those two are locked on; the inbox and push are for
/// notifications only. Push is stored for when it's switched on (it needs the app's development build).
/// </summary>
public class NotificationTemplate : AuditedEntity
{
    public int NotificationTemplateId { get; set; }
    public NotificationTemplateCode Code { get; set; }

    public bool IsEmail { get; set; }
    public string? EmailSubject { get; set; }
    public string? EmailBody { get; set; }

    public bool IsSms { get; set; }
    public string? SmsBody { get; set; }

    /// <summary>
    /// The id India's DLT registry gave this wording. Texts must match their registered template
    /// exactly, so once a provider is connected a changed SMS body needs registering again.
    /// </summary>
    public string? SmsDltTemplateId { get; set; }

    /// <summary>
    /// The Meta template this message goes out as when numbers are sent by WhatsApp (<c>Sms:Provider</c>).
    /// Meta only lets a business start a chat with a template it approved in advance, so the wording lives
    /// at Meta and this row only points to it. Empty: this message isn't sent on WhatsApp. Like the DLT id
    /// it's a registration, not wording, so a reset keeps it. Lowercase letters, digits and _ (Meta's rule).
    /// </summary>
    public string? WhatsAppTemplateName { get; set; }

    /// <summary>The language the Meta template was approved in, exactly as Meta writes it: <c>en</c> is not <c>en_US</c>.</summary>
    public string? WhatsAppLanguage { get; set; }

    /// <summary>
    /// Which of this message's placeholders fill the Meta template's {{1}}, {{2}}, … in order, comma-separated
    /// ("InviterName,Role,TournamentName,ExpiresOn"). Unused for the code messages: an authentication template
    /// always takes just the code, in its body and again in its Copy code button.
    /// </summary>
    public string? WhatsAppParameters { get; set; }

    /// <summary>The in-app inbox.</summary>
    public bool IsNotification { get; set; }
    public string? NotificationTitle { get; set; }
    public string? NotificationBody { get; set; }

    /// <summary>Phone push. Empty texts mean "the same as the inbox".</summary>
    public bool IsPush { get; set; }
    public string? PushTitle { get; set; }
    public string? PushBody { get; set; }
}

public enum TemplateChannel
{
    Email,
    /// <summary>A message to a mobile number in words: what the log writes (testing), or an SMS provider later.</summary>
    Sms,
    Notification,
    Push,
    /// <summary>A message to a mobile number as an approved Meta template (2026-11-26).</summary>
    WhatsApp
}

public class TemplatePlaceholderDto
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>An example value, for the portal's preview.</summary>
    public string Sample { get; set; } = string.Empty;

    /// <summary>Must appear in every text on its channels (a code template's <c>{{Code}}</c>).</summary>
    public bool Required { get; set; }
}

public class TemplateChannelDto
{
    public TemplateChannel Channel { get; set; }

    /// <summary>The template can use this channel at all.</summary>
    public bool Supported { get; set; }

    /// <summary>Its switch can't be changed; <see cref="Note"/> says why.</summary>
    public bool Locked { get; set; }
    public string? Note { get; set; }

    /// <summary>
    /// For the two ways to a mobile number: whether this server sends numbers this way now
    /// (<c>Sms:Provider</c> — WhatsApp for real, Log for testing). Null for the other channels.
    /// </summary>
    public bool? InUseHere { get; set; }
}

public class AdminTemplateDto
{
    public NotificationTemplateCode Code { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;

    /// <summary>When it's sent, and to whom.</summary>
    public string Description { get; set; } = string.Empty;

    public List<TemplateChannelDto> Channels { get; set; } = [];
    public List<TemplatePlaceholderDto> Placeholders { get; set; } = [];

    /// <summary>The wording differs from the built-in one.</summary>
    public bool IsCustomised { get; set; }
    public string? ChangedBy { get; set; }
    public DateTime? ChangedAt { get; set; }

    public bool IsEmail { get; set; }
    public string? EmailSubject { get; set; }
    public string? EmailBody { get; set; }
    public bool IsSms { get; set; }
    public string? SmsBody { get; set; }
    public string? SmsDltTemplateId { get; set; }
    public string? WhatsAppTemplateName { get; set; }
    public string? WhatsAppLanguage { get; set; }
    public List<string> WhatsAppParameters { get; set; } = [];
    public bool IsNotification { get; set; }
    public string? NotificationTitle { get; set; }
    public string? NotificationBody { get; set; }
    public bool IsPush { get; set; }
    public string? PushTitle { get; set; }
    public string? PushBody { get; set; }
}

/// <summary>
/// The whole template as the portal's editor holds it. Texts on unsupported channels are ignored. The
/// WhatsApp fields are left as they are when omitted (null), so an editor that doesn't know them can't
/// wipe them; an empty name or list clears them.
/// </summary>
public class SaveTemplateRequest
{
    public bool IsEmail { get; set; }
    public string? EmailSubject { get; set; }
    public string? EmailBody { get; set; }
    public bool IsSms { get; set; }
    public string? SmsBody { get; set; }
    public string? SmsDltTemplateId { get; set; }
    public string? WhatsAppTemplateName { get; set; }
    public string? WhatsAppLanguage { get; set; }
    public List<string>? WhatsAppParameters { get; set; }
    public bool IsNotification { get; set; }
    public string? NotificationTitle { get; set; }
    public string? NotificationBody { get; set; }
    public bool IsPush { get; set; }
    public string? PushTitle { get; set; }
    public string? PushBody { get; set; }
}
