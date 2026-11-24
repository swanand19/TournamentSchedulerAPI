using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using TournamentScheduler.Api.Models.Accounts;

namespace TournamentScheduler.Api.Models;

public class Tournament
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Fixed when the tournament is created. Tournaments created before cricket existed default to
    /// Football, which is what the migration backfills them to.
    /// </summary>
    public Sport Sport { get; set; } = Sport.Football;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsStarted { get; set; }
    public DateTime? StartedAt { get; set; }

    /// <summary>Who created it — the only one who may delete it, and never removable. System (1) for tournaments from before accounts.</summary>
    public int CreatedByUserId { get; set; } = SystemUser.Id;

    /// <summary>
    /// The days it runs, in its own time zone. Required for new tournaments; null on ones from
    /// before dates existed, which is what exempts those from completing by themselves.
    /// </summary>
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }

    /// <summary>IANA id. Every tournament is in India for now; the column is here so others can follow.</summary>
    public string TimeZoneId { get; set; } = DefaultTimeZoneId;

    public const string DefaultTimeZoneId = "Asia/Kolkata";

    public TournamentStatus Status { get; set; } = TournamentStatus.Upcoming;
    public DateTime? CompletedAt { get; set; }
    public int? CompletedByUserId { get; set; }

    /// <summary>What the signed-in person may do here — filled in per request, never stored.</summary>
    [NotMapped]
    public TournamentAccessDto? Access { get; set; }

    /// <summary>
    /// Anyone may watch it, without an account, by its link (6E). Every tournament is public since 2026-11-23 (the user's
    /// choice: a sports event has nothing to hide, and no email or number is ever shown); only an admin turns this off,
    /// with the portal's "Hide from the public" (a takedown, e.g. a junior tournament a school asks to remove).
    /// </summary>
    public bool IsPublic { get; set; } = true;

    /// <summary>
    /// The code in the public link (8 characters, no look-alikes), made with the tournament and kept for good.
    /// Only ever shown through <see cref="Share"/>.
    /// </summary>
    [JsonIgnore]
    public string? PublicCode { get; set; }

    /// <summary>The public link while <see cref="IsPublic"/> — filled in per request, never stored.</summary>
    [NotMapped]
    public Public.PublicShareDto? Share { get; set; }

    /// <summary>The tournament's square picture and its 16:9 banner (HLD P-26), set by owners. Files, never sent as is.</summary>
    [JsonIgnore]
    public string? LogoId { get; set; }

    [JsonIgnore]
    public string? BannerId { get; set; }

    /// <summary>Signed, expiring links to the pictures above — filled in per request, never stored.</summary>
    [NotMapped]
    public string? LogoUrl { get; set; }

    [NotMapped]
    public string? BannerUrl { get; set; }

    /// <summary>
    /// "Teams can enter with a code" (lasting teams, step 3): the code an organiser sends to the admins of teams they want
    /// (9 characters, so the Join field can tell it from team, pool and club codes). Kept when switched off, so switching on
    /// again gives the same code. Shown to owners only, on the entries answer.
    /// </summary>
    [JsonIgnore]
    public string? EntryCode { get; set; }

    /// <summary>Whether the entry code works now. Approving a schedule, setting up a knockout or starting turns it off (C-10).</summary>
    [JsonIgnore]
    public bool EntriesOpen { get; set; }

    /// <summary>
    /// The league this tournament is a season of (2026-11-24), or null for a one-off. Its season number is worked out from
    /// the league's seasons in date order, never stored. Shown through <see cref="League"/>.
    /// </summary>
    [JsonIgnore]
    public int? LeagueId { get; set; }

    [JsonIgnore]
    public Leagues.League? LeagueRow { get; set; }

    /// <summary>"Season 3 of Corporate Premier League" — filled in per request, left out of the JSON for a one-off tournament.</summary>
    [NotMapped]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Leagues.LeagueBadgeDto? League { get; set; }

    [JsonIgnore]
    public bool IsClosed => Status is TournamentStatus.Completed or TournamentStatus.Cancelled;

    public List<SavedSchedule> Schedules { get; set; } = new();
    public List<Team> Teams { get; set; } = new();

    /// <summary>Football fixtures. Populated only when <see cref="Sport"/> is Football.</summary>
    public List<Match> Matches { get; set; } = new();

    /// <summary>Cricket fixtures. Populated only when <see cref="Sport"/> is Cricket.</summary>
    public List<Cricket.CricketMatch> CricketMatches { get; set; } = new();
}
