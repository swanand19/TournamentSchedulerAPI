using TournamentScheduler.Api.Models.Public;

namespace TournamentScheduler.Api.Services.Access;

/// <summary>
/// Invite links (2026-11-23): a team's join code as an address, "{web app}/join/{code}", so an admin can send it on
/// WhatsApp or anywhere else and whoever opens it joins straight in. The code's length says what it joins (six a
/// tournament's team, eight a lasting team), so there is no separate table: a new code is a new link, and the old one
/// stops at once. A tournament's team stops taking people when the tournament is over, as its typed code does; a team
/// kept from a lasting one shares the lasting team's link, so one real team has one link. The wording lives here so
/// both apps say the same thing.
/// </summary>
public static class InviteLinks
{
    public const string NoLongerWorks = "This invite link no longer works. Ask the team's admin for a new one.";

    /// <summary>The whole link, or null when the server doesn't know where the web app is (the apps share the code then).</summary>
    public static string? Url(PublicOptions options, string? code)
    {
        var site = options.WebAppUrl.Trim().TrimEnd('/');
        return site.Length == 0 || string.IsNullOrEmpty(code) ? null : $"{site}/join/{code}";
    }

    public static InviteLinkDto? ForClub(PublicOptions options, string? code, string clubName) =>
        Url(options, code) is { } url
            ? new InviteLinkDto { Url = url, Note = $"Anyone with this link joins {clubName} and its upcoming tournaments. It works until you make a new link." }
            : null;

    public static InviteLinkDto? ForTeam(PublicOptions options, string? code, string teamName, string tournamentName) =>
        Url(options, code) is { } url
            ? new InviteLinkDto { Url = url, Note = $"Anyone with this link joins {teamName}. It stops working when {tournamentName} is over, or when you make a new link." }
            : null;

    /// <summary>An organiser looking at a lasting team's side: the link is its admins' to share, not theirs.</summary>
    public static InviteLinkDto SharedByClub(string clubName) =>
        new() { Note = $"{clubName} is a lasting team, so its own admins share its invite link." };

    public static string TournamentOver(string tournamentName) => $"{tournamentName} is over, so this invite link no longer works.";
}
