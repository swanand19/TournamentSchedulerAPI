using TournamentScheduler.Api.Models;
using TournamentScheduler.Api.Models.Watch;

namespace TournamentScheduler.Api.Services.Watch;

/// <summary>
/// A football match as it happened, in the events' own order: goals (with the score after them),
/// cards, substitutions, the periods, stoppages and the shootout. The scorer's timeline draws the
/// same events; this is the watcher's version, worded on the server so every app says the same.
/// </summary>
internal static class FootballActivity
{
    public static List<ActivityItemDto> Build(Match match, IReadOnlyList<MatchEvent> events, IReadOnlyDictionary<int, string> names)
    {
        var feed = new ActivityFeed();
        int home = 0, away = 0, halves = 0, halfEnds = 0;
        var yellows = new Dictionary<int, int>();

        string Name(int? id) => id is int x && names.TryGetValue(x, out var n) ? n : "Unknown player";
        string Team(int? id) => id == match.HomeTeamId ? match.HomeTeamName : id == match.AwayTeamId ? match.AwayTeamName : "";
        string Score() => $"{match.HomeTeamName} {home}–{away} {match.AwayTeamName}";

        foreach (var e in events.OrderBy(e => e.Id))
        {
            var minute = e.StoppageMinute is int s && s > 0 ? $"{e.MinuteOfMatch}+{s}'" : $"{e.MinuteOfMatch}'";
            switch (e.EventType)
            {
                case MatchEventType.Goal:
                    if (e.TeamId == match.HomeTeamId) home++;
                    else away++;
                    feed.Add(ActivityKind.Goal, e.PlayerId is null ? "Goal" : $"Goal: {Name(e.PlayerId)}",
                        detail: e.RelatedPlayerId is null ? Team(e.TeamId) : $"{Team(e.TeamId)} · Assist: {Name(e.RelatedPlayerId)}",
                        marker: minute, chip: $"{home}–{away}", teamId: e.TeamId);
                    break;

                case MatchEventType.YellowCard:
                    var count = e.PlayerId is int p ? yellows[p] = yellows.GetValueOrDefault(p) + 1 : 1;
                    if (count >= 2)
                        feed.Add(ActivityKind.SecondYellow, $"Second yellow: {Name(e.PlayerId)}", detail: $"{Team(e.TeamId)} · Sent off", marker: minute, teamId: e.TeamId);
                    else
                        feed.Add(ActivityKind.YellowCard, $"Yellow card: {Name(e.PlayerId)}", detail: Team(e.TeamId), marker: minute, teamId: e.TeamId);
                    break;

                case MatchEventType.RedCard:
                    feed.Add(ActivityKind.RedCard, $"Red card: {Name(e.PlayerId)}", detail: $"{Team(e.TeamId)} · Sent off", marker: minute, teamId: e.TeamId);
                    break;

                case MatchEventType.SubstitutionIn:
                    // PlayerId came off, RelatedPlayerId went on (as the scorer's timeline reads them).
                    feed.Add(ActivityKind.Substitution, $"Substitution: {Team(e.TeamId)}",
                        detail: $"On: {Name(e.RelatedPlayerId)} · Off: {Name(e.PlayerId)}", marker: minute, teamId: e.TeamId);
                    break;

                case MatchEventType.HalfStart:
                    halves++;
                    feed.Add(ActivityKind.Period, halves switch
                    {
                        1 => "Kick-off",
                        2 => "Second half under way",
                        3 => "Extra time under way",
                        _ => "Second half of extra time under way"
                    }, marker: minute);
                    break;

                case MatchEventType.HalfEnd:
                    halfEnds++;
                    feed.Add(ActivityKind.Period, halfEnds switch
                    {
                        1 => "Half-time",
                        2 => "End of the second half",
                        3 => "End of the first half of extra time",
                        _ => "End of extra time"
                    }, detail: Score(), marker: minute);
                    break;

                case MatchEventType.ExtraTimeStart:
                    feed.Add(ActivityKind.Period, "Extra time to be played", detail: Score(), marker: minute);
                    break;

                case MatchEventType.ExtraTimeAdded:
                    feed.Add(ActivityKind.Clock, "Stoppage time added", marker: minute);
                    break;

                case MatchEventType.ClockPaused:
                    feed.Add(ActivityKind.Clock, "Play stopped", marker: minute);
                    break;

                case MatchEventType.ClockResumed:
                    feed.Add(ActivityKind.Clock, "Play resumed", marker: minute);
                    break;

                case MatchEventType.PenaltyShootoutStarted:
                    feed.Add(ActivityKind.Period, "Penalty shootout", detail: $"Level at {home}–{away}");
                    break;

                case MatchEventType.PenaltyKick:
                    feed.Add(ActivityKind.Penalty,
                        $"{(e.PlayerId is null ? "Penalty" : Name(e.PlayerId))} {(e.PenaltyScored == true ? "scores" : "misses")}",
                        detail: Team(e.TeamId), marker: "Pen",
                        chip: $"{e.PenaltyHomeScoreAfter ?? 0}–{e.PenaltyAwayScoreAfter ?? 0}", teamId: e.TeamId);
                    break;

                case MatchEventType.MatchCompleted:
                    feed.Add(ActivityKind.Result, $"Full time: {Score()}", detail: ShootoutLine(match), marker: minute);
                    break;

                case MatchEventType.MatchAbandoned:
                    feed.Add(ActivityKind.Result, "Match abandoned",
                        detail: e.TeamId is null ? Score() : $"Awarded to {Team(e.TeamId)}", marker: minute, teamId: e.TeamId);
                    break;

                // SubstitutionOut is the other half of SubstitutionIn: already told.
            }
        }

        return feed.Items;
    }

    private static string? ShootoutLine(Match match)
    {
        if (match.PenaltyWinnerTeamId is not int winner) return null;
        var name = winner == match.HomeTeamId ? match.HomeTeamName : match.AwayTeamName;
        return match.PenaltyHomeScore is int h && match.PenaltyAwayScore is int a
            ? $"{name} win {Math.Max(h, a)}–{Math.Min(h, a)} on penalties"
            : $"{name} win on penalties";
    }
}
