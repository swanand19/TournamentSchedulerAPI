namespace TournamentScheduler.Api.Models;

/// <summary>
/// Which sport a tournament is played under. Chosen once when the tournament is created and never
/// mixed within one: teams, players, groups and schedule generation are shared between the sports,
/// but everything from the match onwards — rules, live scoring, stats — branches on this.
/// </summary>
public enum Sport
{
    Football = 0,
    Cricket = 1
}
