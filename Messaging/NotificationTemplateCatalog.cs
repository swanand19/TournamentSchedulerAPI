using System.Text.RegularExpressions;
using TournamentScheduler.Api.Models.Messaging;
using TournamentScheduler.Api.Models.Notifications;

namespace TournamentScheduler.Api.Messaging;

/// <summary>A placeholder a template may use: <c>{{Name}}</c>.</summary>
public sealed record TemplatePlaceholder(string Name, string Description, string Sample, bool Required = false);

/// <summary>Which channels a template can use, and which are fixed.</summary>
public enum TemplateFamily
{
    /// <summary>Codes and invites: email or text, whichever the person gave. Both locked on.</summary>
    Direct,

    /// <summary>Notifications: the inbox, email and push, each switchable. Never a text (cost, and DLT).</summary>
    Notification
}

/// <summary>One template's fixed facts and built-in wording.</summary>
public sealed record TemplateDefinition(
    NotificationTemplateCode Code,
    string Name,
    string Group,
    string Description,
    TemplateFamily Family,
    TemplatePlaceholder[] Placeholders,
    NotificationTemplate Default,
    NotificationKind? Kind = null);

/// <summary>
/// The built-in wording of every message, which placeholders each may use, and the rules a saved
/// template must keep (phase 6D). The database row is what's sent; this is what a row starts from
/// and goes back to on reset, and what's used if a row is missing or was broken by hand in SQL.
/// </summary>
public static partial class NotificationTemplateCatalog
{
    public const int SubjectMax = 200;
    public const int EmailBodyMax = 4000;
    public const int SmsBodyMax = 1000;
    public const int TitleMax = 200;
    public const int BodyMax = 1000;
    public const int PushTitleMax = 100;
    public const int PushBodyMax = 500;

    private const string Sign = "KickCricket";
    private const string OpenTheApp = "Open KickCricket to see it.";

    [GeneratedRegex(@"\{\{\s*([A-Za-z]+)\s*\}\}")]
    private static partial Regex PlaceholderPattern();

    // --- Placeholders shared by several templates -------------------------

    private static readonly TemplatePlaceholder Code = new("Code", "The one-time code", "482913", Required: true);
    private static readonly TemplatePlaceholder ValidMinutes = new("ValidMinutes", "How many minutes the code works for", "5");
    private static readonly TemplatePlaceholder Tournament = new("TournamentName", "The tournament", "Summer Cup");
    private static readonly TemplatePlaceholder Actor = new("ActorName", "Who did it", "Priya Shah");
    private static readonly TemplatePlaceholder MatchName = new("Match", "The match, \"Home v Away\"", "Lions v Tigers");
    private static readonly TemplatePlaceholder Role = new("Role", "\"an owner\" or \"a scorer\"", "a scorer");
    private static readonly TemplatePlaceholder ContactKind = new("ContactKind", "\"email\" or \"number\"", "email");
    private static readonly TemplatePlaceholder Team = new("TeamName", "The team", "Lions");
    private static readonly TemplatePlaceholder PlayerName = new("PlayerName", "The player", "Rohan Gokhale");
    private static readonly TemplatePlaceholder PlayerNames = new("PlayerNames", "Who is playing, \"Rohan Gokhale\" or \"Neha Joshi and Rohit Sawant\"", "Rohan Gokhale");
    private static readonly TemplatePlaceholder IsAre = new("IsAre", "\"is\" for one player, \"are\" for more", "is");
    private static readonly TemplatePlaceholder EndsWhen = new("EndsWhen", "\"ends today\" or \"ended yesterday\"", "ends today");
    private static readonly TemplatePlaceholder ClosesWhen = new("ClosesWhen", "\"at the end of tomorrow\" or \"at midnight tonight\"", "at the end of tomorrow");

    // --- The templates -----------------------------------------------------

    public static readonly IReadOnlyList<TemplateDefinition> All =
    [
        CodeTemplate(NotificationTemplateCode.SignInCode, "Sign-in code",
            "The code someone types to sign up or sign in (the app, the website and the admin portal).",
            "{{Code}} is your KickCricket code",
            "Your sign-in code is {{Code}}\n\nIt works for {{ValidMinutes}} minutes. If you didn't ask for it, you can ignore this email —\nnobody can sign in without the code.",
            "{{Code}} is your KickCricket sign-in code. It works for {{ValidMinutes}} minutes. Don't share it with anyone."),
        CodeTemplate(NotificationTemplateCode.AddContactCode, "Code to add an email or number",
            "Sent to an email or mobile number someone adds to their account, to prove it's theirs.",
            "{{Code}} is your code to add this email to KickCricket",
            "Your code to add this email to your account is {{Code}}\n\nIt works for {{ValidMinutes}} minutes. If you didn't ask for it, you can ignore this email —\nnothing changes without the code.",
            "{{Code}} is your code to add this number to KickCricket. It works for {{ValidMinutes}} minutes."),
        CodeTemplate(NotificationTemplateCode.DeleteAccountCode, "Code to delete an account",
            "Sent when someone asks to delete their account.",
            "{{Code}} is your code to delete your KickCricket account",
            "Your code to delete your account is {{Code}}\n\nIt works for {{ValidMinutes}} minutes. Deleting can't be undone. If you didn't ask for it,\ndon't share the code with anyone, and sign out of all devices from the Account screen.",
            "{{Code}} is your code to delete your KickCricket account. It works for {{ValidMinutes}} minutes. Deleting can't be undone."),

        new(NotificationTemplateCode.TournamentInvite, "Tournament invite", "Invites",
            "Sent when an owner adds an owner or scorer by an email or number that has no account yet.",
            TemplateFamily.Direct,
            [
                new("InviterName", "The owner who sent it", "Swanand"),
                Tournament,
                new("Sport", "\"football\" or \"cricket\"", "cricket"),
                Role,
                new("RoleAction", "\"help run\" (owner) or \"score\" (scorer)", "score"),
                new("Contact", "The email or number invited", "ravi@example.com"),
                new("ExpiresOn", "When the invite stops working", "15 Nov 2026")
            ],
            new NotificationTemplate
            {
                IsEmail = true,
                EmailSubject = "{{InviterName}} invited you to {{TournamentName}} on KickCricket",
                EmailBody = "{{InviterName}} has added you as {{Role}} of {{TournamentName}}, a {{Sport}} tournament on KickCricket.\n\n"
                            + "To join, open the app (or the website) and sign in with this email address, {{Contact}}. You'll\n"
                            + "get a one-time code by email — there's no password. You'll be {{Role}} as soon as you've signed in.\n\n"
                            + "The invite works until {{ExpiresOn}}. If you weren't expecting it, you can ignore this email.\n\n" + Sign,
                IsSms = true,
                SmsBody = "{{InviterName}} invited you to {{RoleAction}} {{TournamentName}} ({{Sport}}) on KickCricket. "
                          + "Sign in to the app with this number to join. The invite works until {{ExpiresOn}}."
            }),

        Notice(NotificationTemplateCode.ScoringRequested, NotificationKind.ScoringRequested, "Scoring requested", "Scoring",
            "To whoever is scoring a match, when someone asks to take over.", [Actor, MatchName, Tournament],
            "{{ActorName}} wants to score {{Match}}",
            "In {{TournamentName}}. Open the match to allow or decline. The request lasts 2 minutes."),
        Notice(NotificationTemplateCode.ScoringAllowed, NotificationKind.ScoringAllowed, "Scoring request allowed", "Scoring",
            "To whoever asked to score, when the scorer allows it.", [Actor, MatchName, Tournament],
            "{{ActorName}} let you score {{Match}}",
            "In {{TournamentName}}. You're scoring it now."),
        Notice(NotificationTemplateCode.ScoringDeclined, NotificationKind.ScoringDeclined, "Scoring request declined", "Scoring",
            "To whoever asked to score, when the scorer declines.", [Actor, MatchName, Tournament],
            "{{ActorName}} is still scoring {{Match}}",
            "In {{TournamentName}}. They declined your request to take over."),
        Notice(NotificationTemplateCode.ScoringTakenOver, NotificationKind.ScoringTakenOver, "Scoring taken over", "Scoring",
            "To whoever was scoring, when an owner takes over without asking.", [Actor, MatchName, Tournament],
            "{{ActorName}} took over scoring {{Match}}",
            "In {{TournamentName}}. Owners can take over without asking. If you need it back, ask to score from the match."),
        Notice(NotificationTemplateCode.ScoringPassedOn, NotificationKind.ScoringTakenOver, "Scoring passed on", "Scoring",
            "To whoever was scoring, when their app was away 5 minutes and someone asked to score.", [Actor, MatchName, Tournament],
            "{{ActorName}} is scoring {{Match}} now",
            "In {{TournamentName}}. Your app hadn't been on this match for 5 minutes, so scoring passed to them when they asked."),

        Notice(NotificationTemplateCode.AddedAsOwner, NotificationKind.AddedToTournament, "Made an owner", "People and teams",
            "To someone an owner adds (or promotes) as an owner.", [Actor, Tournament],
            "{{ActorName}} made you an owner of {{TournamentName}}",
            "You can run the tournament now: teams, schedule, people and scoring."),
        Notice(NotificationTemplateCode.AddedAsScorer, NotificationKind.AddedToTournament, "Made a scorer", "People and teams",
            "To someone an owner adds as a scorer.", [Actor, Tournament],
            "{{ActorName}} made you a scorer of {{TournamentName}}",
            "You can set up and score its matches now."),
        Notice(NotificationTemplateCode.InviteJoined, NotificationKind.AddedToTournament, "Invite taken up (the newcomer)", "People and teams",
            "To someone who signs up (or adds an email or number) that had an invite waiting.", [Role, Tournament, ContactKind],
            "You're {{Role}} of {{TournamentName}}",
            "You were invited by your {{ContactKind}}. The tournament is on your home screen now."),
        Notice(NotificationTemplateCode.InviteAccepted, NotificationKind.InviteAccepted, "Invite taken up (the inviter)", "People and teams",
            "To the owner who sent an invite, when it's taken up.", [new("PersonName", "Who joined", "Ravi Kumar"), Tournament, ContactKind, Role],
            "{{PersonName}} joined {{TournamentName}}",
            "They signed in with the {{ContactKind}} you invited, and they're {{Role}} now."),
        Notice(NotificationTemplateCode.MadeTeamAdmin, NotificationKind.AddedToTournament, "Made a team admin", "People and teams",
            "To someone an owner names to run a team's squad.", [Actor, Team, Tournament],
            "{{ActorName}} made you an admin of {{TeamName}}",
            "In {{TournamentName}}. You can add and remove its players and choose its captain now."),
        Notice(NotificationTemplateCode.AddedToTeam, NotificationKind.LinkedToTeam, "Added to a team", "People and teams",
            "To someone whose account an owner gives a squad place (by their email or number).", [Actor, Team, Tournament],
            "{{ActorName}} added you to {{TeamName}}",
            "In {{TournamentName}}. Your stats from its matches count on your profile."),
        Notice(NotificationTemplateCode.JoinedTeamOnSignUp, NotificationKind.LinkedToTeam, "In a team on signing up", "People and teams",
            "To someone who signs up with an email or number an owner had given a squad place.", [Team, Tournament, ContactKind],
            "You're in {{TeamName}}",
            "In {{TournamentName}}. The organiser added your {{ContactKind}} to the squad, so your stats from its matches count on your profile."),

        Notice(NotificationTemplateCode.AllMatchesPlayed, NotificationKind.AllMatchesPlayed, "All matches played", "Tournaments",
            "To the owners (except whoever finished it) when a tournament's last match ends. Once per tournament.", [Tournament],
            "All matches in {{TournamentName}} are played",
            "Mark the tournament complete? Its table and stats are final once you do. If nobody does, it completes by itself after its end date."),
        Notice(NotificationTemplateCode.EndDateMatchesLeft, NotificationKind.EndDateReminder, "End date — matches left", "Tournaments",
            "To the owners from 8 a.m. on a live tournament's end date, when matches are still to play.",
            [Tournament, EndsWhen, ClosesWhen, new("MatchesLeft", "\"1 match\", \"3 matches\"", "3 matches")],
            "{{TournamentName}} {{EndsWhen}}",
            "Still to play: {{MatchesLeft}}. It completes by itself {{ClosesWhen}}; to play on, move its end date in People & settings.",
            email: true),
        Notice(NotificationTemplateCode.EndDateAllPlayed, NotificationKind.EndDateReminder, "End date — all played", "Tournaments",
            "To the owners from 8 a.m. on a live tournament's end date, when every match is played.", [Tournament, EndsWhen, ClosesWhen],
            "{{TournamentName}} {{EndsWhen}}",
            "All its matches are played. Mark it complete, or it completes by itself {{ClosesWhen}}.",
            email: true),
        Notice(NotificationTemplateCode.EndDateNotStarted, NotificationKind.EndDateReminder, "End date — never started", "Tournaments",
            "To the owners from 8 a.m. on the end date of a tournament that hasn't started.", [Tournament, EndsWhen, ClosesWhen],
            "{{TournamentName}} {{EndsWhen}}",
            "It hasn't started. Start it, or move its dates in People & settings — otherwise it's cancelled {{ClosesWhen}}.",
            email: true),

        Notice(NotificationTemplateCode.AuctionStarted, NotificationKind.Auction, "Auction started", "Auctions",
            "To the players in the pool and the teams' bidders when the auctioneer starts the player auction.",
            [Tournament, new("YourPart", "\"You bid for Lions.\" or what a pool player should do", "You bid for Lions.")],
            "The auction for {{TournamentName}} has started",
            "{{YourPart}} Open the tournament to follow it live."),
        Notice(NotificationTemplateCode.AuctionBought, NotificationKind.Auction, "Bought at the auction", "Auctions",
            "To a player in the pool when a team buys them.", [Team, new("Price", "What they went for, with the unit", "1,250 pts"), Tournament],
            "You were bought by {{TeamName}}",
            "For {{Price}}, in {{TournamentName}}'s auction. You'll be in their squad once the organiser confirms the teams."),

        Notice(NotificationTemplateCode.DrawStarted, NotificationKind.Draw, "Team draw started", "Auctions",
            "To the players in the pool, the teams' players, the owners and the scorers when the team draw starts (not a quick draw).",
            [Tournament, new("YourPart", "What a pool player or anyone else should do", "You're in the pool: watch which team you're drawn for.")],
            "The team draw for {{TournamentName}} has started",
            "{{YourPart}} Open the tournament to watch it live."),
        Notice(NotificationTemplateCode.DrawPicked, NotificationKind.Draw, "Drawn for a team", "Auctions",
            "To a player in the pool when their name lands in a team in the draw.", [Team, Tournament],
            "You were drawn for {{TeamName}}",
            "In {{TournamentName}}'s team draw. You'll be in their squad once the organiser confirms the teams."),
        Notice(NotificationTemplateCode.AuctionBidderReplaced, NotificationKind.Auction, "No longer bidding", "Auctions",
            "To a team's bidder when the auctioneer replaces them during the auction (a phone that died, for example).",
            [Team, Tournament, new("NewBidder", "Who bids for the team now", "the auctioneer, from the room")],
            "You no longer bid for {{TeamName}}",
            "In {{TournamentName}}'s auction, {{NewBidder}} bids for {{TeamName}} now. You can still watch it live."),
        Notice(NotificationTemplateCode.AuctionBidderNow, NotificationKind.Auction, "Now bidding", "Auctions",
            "To the new bidder when the auctioneer replaces a team's bidder during the auction.",
            [Team, Tournament],
            "You bid for {{TeamName}} now",
            "In {{TournamentName}}'s auction. Open the tournament to bid when the auction resumes."),

        // Lasting teams (2026-11-20). People only ever read "team" (C-21), so TeamName holds the lasting team's name.
        Notice(NotificationTemplateCode.AddedToClub, NotificationKind.Club, "Added to a lasting team", "Teams",
            "To someone a team's admin adds to the team by their email or number (or whose team was kept with them in it).",
            [Actor, Team],
            "{{ActorName}} added you to {{TeamName}}",
            "It's on your Teams tab now. When {{TeamName}} enters a tournament, you can be in its squad."),
        Notice(NotificationTemplateCode.MadeClubAdmin, NotificationKind.Club, "Made a lasting team's admin", "Teams",
            "To someone made an admin of a lasting team.", [Actor, Team],
            "{{ActorName}} made you an admin of {{TeamName}}",
            "You can change its players and admins now, and enter it into tournaments."),
        Notice(NotificationTemplateCode.KeepTeam, NotificationKind.Club, "Keep the team?", "Teams",
            "To a team's admins when its tournament is completed, if the team isn't kept yet. Once per team.", [Team, Tournament],
            "Keep {{TeamName}} for your next tournament?",
            "{{TournamentName}} is over. Keep {{TeamName}} and the same players come along when you enter another tournament."),
        Notice(NotificationTemplateCode.EntryRequested, NotificationKind.Club, "A team asks to enter", "Teams",
            "To a tournament's owners when a lasting team's admin sends an entry with the tournament's entry code.", [Actor, Team, Tournament],
            "{{TeamName}} asks to enter {{TournamentName}}",
            "{{ActorName}} sent the entry. Open the tournament's teams to see the squad, then accept or decline it."),
        Notice(NotificationTemplateCode.EntryAccepted, NotificationKind.Club, "Entry accepted", "Teams",
            "To a lasting team's admins when the organiser accepts its entry.", [Team, Tournament],
            "{{TeamName}} is in {{TournamentName}}",
            "The organiser accepted the entry. The squad is in the tournament now, and you can change it there."),
        Notice(NotificationTemplateCode.EntryDeclined, NotificationKind.Club, "Entry declined", "Teams",
            "To a lasting team's admins when the organiser declines its entry.", [Team, Tournament],
            "{{TournamentName}} declined {{TeamName}}'s entry",
            "The organiser didn't take the entry this time."),
        Notice(NotificationTemplateCode.EntryCancelled, NotificationKind.Club, "Entries closed", "Teams",
            "To a lasting team's admins when a tournament's entries close before the organiser answered its entry.", [Team, Tournament],
            "Entries to {{TournamentName}} have closed",
            "The organiser made the schedule before answering {{TeamName}}'s entry, so it was cancelled. Ask them if there's still room."),
        Notice(NotificationTemplateCode.ClubPlayerJoined, NotificationKind.Club, "A player joined", "Teams",
            "To a lasting team's admins when someone joins it with its code.", [PlayerName, Team],
            "{{PlayerName}} joined {{TeamName}}",
            "They're in the squads of tournaments still to come. Tap to choose whether they play in the ones being played now."),
        Notice(NotificationTemplateCode.Followed, NotificationKind.Social, "A new follower", "Social",
            "To someone when another person follows them.", [Actor],
            "{{ActorName}} started following you",
            "Tap to see their profile, and follow back if you like."),
        Notice(NotificationTemplateCode.FollowedPlaying, NotificationKind.Social, "Someone you follow is playing", "Social",
            "To followers when a match they can watch starts with someone they follow in it (at set-up in cricket, at kick-off in football).",
            [PlayerNames, IsAre, MatchName, Tournament],
            "{{PlayerNames}} {{IsAre}} playing",
            "{{Match}} · {{TournamentName}}"),

        // Leagues (2026-11-24).
        Notice(NotificationTemplateCode.MadeLeagueOrganiser, NotificationKind.League, "Made a league's organiser", "Leagues",
            "To someone made one of a league's organisers.", [Actor, new("LeagueName", "The league", "Corporate Premier League")],
            "{{ActorName}} made you an organiser of {{LeagueName}}",
            "You can start its next season, tick earlier tournaments in as seasons, and change the league.")
    ];

    private static readonly Dictionary<NotificationTemplateCode, TemplateDefinition> ByCode = All.ToDictionary(d => d.Code);

    public static TemplateDefinition Get(NotificationTemplateCode code) => ByCode[code];

    private static TemplateDefinition CodeTemplate(NotificationTemplateCode code, string name, string description, string subject, string body, string sms) =>
        new(code, name, "Codes", description, TemplateFamily.Direct, [Code, ValidMinutes],
            new NotificationTemplate
            {
                IsEmail = true,
                EmailSubject = subject,
                EmailBody = body + "\n\n" + Sign,
                IsSms = true,
                SmsBody = sms,
                // Meta's wording ("123456 is your verification code."), so one approved template serves all three.
                WhatsAppTemplateName = CodeWhatsAppTemplate,
                WhatsAppLanguage = CodeWhatsAppLanguage
            });

    /// <summary>
    /// The authentication template approved in KickCricket's WhatsApp account (2026-11-25), which a new
    /// database starts with. Everywhere else the name comes from the row, edited in the portal.
    /// </summary>
    public const string CodeWhatsAppTemplate = "otp_template_kickcricket";
    public const string CodeWhatsAppLanguage = "en";

    public const int WhatsAppNameMax = 512;
    public const int WhatsAppLanguageMax = 15;
    public const int WhatsAppParametersMax = 20;

    [GeneratedRegex("^[a-z0-9_]+$")]
    private static partial Regex WhatsAppNamePattern();

    // Meta's language codes: "en", "en_US", "pt_BR", "zh_CN", …
    [GeneratedRegex("^[a-z]{2,3}(_[A-Z]{2,4})?$")]
    private static partial Regex WhatsAppLanguagePattern();

    /// <summary>A code message: its WhatsApp template is an authentication one, which takes only the code.</summary>
    public static bool IsCodeMessage(TemplateDefinition d) => d.Placeholders.Any(p => p.Required && p.Name == Code.Name);

    /// <summary>The stored "A,B,C" as a list; empty when there are none.</summary>
    public static List<string> WhatsAppParametersOf(string? stored) =>
        string.IsNullOrWhiteSpace(stored) ? [] : stored.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();

    /// <summary>The list as stored, or null when there are none.</summary>
    public static string? StoredWhatsAppParameters(IReadOnlyCollection<string>? parameters) =>
        parameters is { Count: > 0 } ? string.Join(",", parameters.Select(p => p.Trim())) : null;

    /// <summary>A notification: the inbox on, push on (for when it exists), email only where asked.</summary>
    private static TemplateDefinition Notice(NotificationTemplateCode code, NotificationKind kind, string name, string group, string description,
        TemplatePlaceholder[] placeholders, string title, string body, bool email = false) =>
        new(code, name, group, description, TemplateFamily.Notification, placeholders,
            new NotificationTemplate
            {
                IsNotification = true,
                NotificationTitle = title,
                NotificationBody = body,
                IsEmail = email,
                EmailSubject = title,
                EmailBody = $"{body}\n\n{OpenTheApp}\n\n{Sign}",
                IsPush = true
            },
            kind);

    /// <summary>A fresh row holding the built-in wording.</summary>
    public static NotificationTemplate DefaultRow(NotificationTemplateCode code)
    {
        var d = Get(code).Default;
        var row = new NotificationTemplate { Code = code };
        CopyTexts(d, row);
        // Not in CopyTexts: a reset copies the wording back but keeps what's registered with Meta.
        row.WhatsAppTemplateName = d.WhatsAppTemplateName;
        row.WhatsAppLanguage = d.WhatsAppLanguage;
        row.WhatsAppParameters = d.WhatsAppParameters;
        return row;
    }

    public static void CopyTexts(NotificationTemplate from, NotificationTemplate to)
    {
        to.IsEmail = from.IsEmail;
        to.EmailSubject = from.EmailSubject;
        to.EmailBody = from.EmailBody;
        to.IsSms = from.IsSms;
        to.SmsBody = from.SmsBody;
        to.SmsDltTemplateId = from.SmsDltTemplateId;
        to.IsNotification = from.IsNotification;
        to.NotificationTitle = from.NotificationTitle;
        to.NotificationBody = from.NotificationBody;
        to.IsPush = from.IsPush;
        to.PushTitle = from.PushTitle;
        to.PushBody = from.PushBody;
    }

    /// <summary>Whether a row says the same as the built-in wording (the DLT id doesn't count).</summary>
    public static bool IsDefault(NotificationTemplate row)
    {
        var d = Get(row.Code).Default;
        return row.IsEmail == d.IsEmail && row.EmailSubject == d.EmailSubject && row.EmailBody == d.EmailBody
               && row.IsSms == d.IsSms && row.SmsBody == d.SmsBody
               && row.IsNotification == d.IsNotification && row.NotificationTitle == d.NotificationTitle && row.NotificationBody == d.NotificationBody
               && row.IsPush == d.IsPush && Same(row.PushTitle, d.PushTitle) && Same(row.PushBody, d.PushBody);
    }

    private static bool Same(string? a, string? b) => string.IsNullOrEmpty(a) ? string.IsNullOrEmpty(b) : a == b;

    /// <summary>Fills in <c>{{Placeholders}}</c>. One not given (or misspelt in SQL) is left as written.</summary>
    public static string Render(string? text, IReadOnlyDictionary<string, string> values) =>
        PlaceholderPattern().Replace(text ?? "", m => values.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);

    // --- What a saved template must keep --------------------------------------

    /// <summary>The rule a template breaks, as a sentence for the portal, or null when it's fine.</summary>
    public static string? Problem(NotificationTemplateCode code, SaveTemplateRequest t)
    {
        var d = Get(code);
        if (d.Family == TemplateFamily.Direct)
        {
            if (!t.IsEmail || !t.IsSms)
                return "This message always goes by email or text, whichever the person gave, so neither can be switched off.";
            if (t.IsNotification || t.IsPush)
                return "This message goes only by email or text.";
        }
        else if (t.IsSms)
        {
            return "Notifications don't go by text: texts cost money and each wording has to be registered (DLT). Use the inbox, email or push.";
        }

        var texts = new List<(string Label, string? Text, int Max, bool Needed)>();
        if (d.Family == TemplateFamily.Direct || t.IsEmail)
        {
            texts.Add(("Email subject", t.EmailSubject, SubjectMax, true));
            texts.Add(("Email body", t.EmailBody, EmailBodyMax, true));
        }
        if (d.Family == TemplateFamily.Direct)
            texts.Add(("Text message", t.SmsBody, SmsBodyMax, true));
        if (t.IsNotification)
        {
            texts.Add(("Inbox title", t.NotificationTitle, TitleMax, true));
            texts.Add(("Inbox text", t.NotificationBody, BodyMax, true));
        }
        if (t.IsPush)
        {
            texts.Add(("Push title", t.PushTitle, PushTitleMax, false));
            texts.Add(("Push text", t.PushBody, PushBodyMax, false));
        }
        if (t.SmsDltTemplateId is { Length: > 50 })
            return "The DLT template id can be at most 50 characters.";
        if (WhatsAppProblem(d, t) is { } whatsApp)
            return whatsApp;

        var allowed = d.Placeholders.Select(p => p.Name).ToHashSet();
        foreach (var (label, text, max, needed) in texts)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                if (needed) return $"{label} can't be empty.";
                continue;
            }
            if (text.Length > max) return $"{label} can be at most {max} characters.";

            var unknown = PlaceholderPattern().Matches(text).Select(m => m.Groups[1].Value).FirstOrDefault(n => !allowed.Contains(n));
            if (unknown != null)
                return $"{label} uses {{{{{unknown}}}}}, which this message doesn't have. It can use: {string.Join(", ", d.Placeholders.Select(p => $"{{{{{p.Name}}}}}"))}.";
            if (PlaceholderPattern().Replace(text, "").Contains("{{") || PlaceholderPattern().Replace(text, "").Contains("}}"))
                return $"{label} has a {{{{ or }}}} that isn't a whole placeholder.";

            // The code is the point of a code message; a body without it would lock people out.
            foreach (var required in d.Placeholders.Where(p => p.Required))
                if (label != "Email subject" && !text.Contains($"{{{{{required.Name}}}}}"))
                    return $"{label} must include {{{{{required.Name}}}}}.";
        }
        return null;
    }

    /// <summary>
    /// The WhatsApp section's rules. A code message must keep its template: with numbers on WhatsApp,
    /// emptying it would stop everyone signing in by number. Names and languages are checked against
    /// Meta's own formats, so a typo is refused here rather than by Meta at the next sign-in.
    /// </summary>
    private static string? WhatsAppProblem(TemplateDefinition d, SaveTemplateRequest t)
    {
        var name = t.WhatsAppTemplateName?.Trim();
        var language = t.WhatsAppLanguage?.Trim();
        var parameters = t.WhatsAppParameters ?? [];

        if (d.Family != TemplateFamily.Direct)
            return string.IsNullOrEmpty(name) && string.IsNullOrEmpty(language) && parameters.Count == 0
                ? null
                : "Notifications don't go on WhatsApp: Meta wants an approved template for each kind of message. Use the inbox, email or push.";

        if (string.IsNullOrEmpty(name))
        {
            if (IsCodeMessage(d))
                return "Codes to mobile numbers go on WhatsApp, so this message needs the name of its approved WhatsApp template.";
            return string.IsNullOrEmpty(language) && parameters.Count == 0
                ? null
                : "Give the WhatsApp template's name too, or clear its language and values.";
        }
        if (name.Length > WhatsAppNameMax)
            return $"The WhatsApp template name can be at most {WhatsAppNameMax} characters.";
        if (!WhatsAppNamePattern().IsMatch(name))
            return "A WhatsApp template name has only lowercase letters, digits and _ — copy it from WhatsApp Manager.";
        if (string.IsNullOrEmpty(language))
            return "Give the language the WhatsApp template was approved in, as Meta writes it (en, en_US, …).";
        if (language.Length > WhatsAppLanguageMax || !WhatsAppLanguagePattern().IsMatch(language))
            return $"\"{language}\" isn't a WhatsApp language code. Copy it from WhatsApp Manager: en, en_US, hi, …";

        if (IsCodeMessage(d))
            return parameters.Count == 0
                ? null
                : "A code's WhatsApp template always takes just the code, so it has no values to choose.";
        if (parameters.Count > WhatsAppParametersMax)
            return $"A WhatsApp template here can have at most {WhatsAppParametersMax} values.";
        var allowed = d.Placeholders.Select(p => p.Name).ToHashSet();
        var unknown = parameters.FirstOrDefault(p => !allowed.Contains(p.Trim()));
        if (unknown != null)
            return $"\"{unknown}\" isn't one of this message's values. It can use: {string.Join(", ", d.Placeholders.Select(p => p.Name))}.";
        return null;
    }
}
