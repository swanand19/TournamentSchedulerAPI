using TournamentScheduler.Api.Models;
using TournamentScheduler.Api.Models.Cricket;

namespace TournamentScheduler.Api.Services.Knockouts;

/// <summary>
/// Who won a finished match, as a knockout slot needs it: the team that goes through. Null while it's
/// being played, and when it ended with nobody through (abandoned level, a tie with nothing to split
/// it) — the owner then puts the right team in the next match by hand.
/// </summary>
public static class KnockoutResults
{
    public static int? WinnerOf(Match match)
    {
        if (match.Status != MatchStatus.Completed) return null;
        if ((match.ForfeitWinnerTeamId ?? match.PenaltyWinnerTeamId) is int decided) return decided;
        if (match.HomeScore == match.AwayScore) return null;
        return match.HomeScore > match.AwayScore ? match.HomeTeamId : match.AwayTeamId;
    }

    public static int? WinnerOf(CricketMatch match) =>
        match.IsOver ? match.WinnerTeamId : null;

    public static int? LoserOf(int? winner, int? home, int? away) =>
        winner == null ? null : winner == home ? away : home;

    /// <summary>"2–1", "2–2 (4–3 pens)", "Lions won (awarded)"; null before a ball is kicked.</summary>
    public static string? ResultLine(Match match)
    {
        if (match.Status == MatchStatus.NotStarted) return null;
        var line = $"{match.HomeScore}–{match.AwayScore}";
        if (match.PenaltyHomeScore is int ph && match.PenaltyAwayScore is int pa) line += $" ({ph}–{pa} pens)";
        return line;
    }

    public static string? ResultLine(CricketMatch match) =>
        match.Status == CricketMatchStatus.NotStarted ? null : match.ResultSummary;
}
