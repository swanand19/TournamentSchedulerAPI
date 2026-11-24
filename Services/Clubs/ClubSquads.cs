using TournamentScheduler.Api.Data.DataServices;
using TournamentScheduler.Api.Data.Queries;
using TournamentScheduler.Api.Models;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Models.Auctions;
using TournamentScheduler.Api.Models.Clubs;
using TournamentScheduler.Api.Models.Cricket;

namespace TournamentScheduler.Api.Services.Clubs;

/// <summary>
/// A club's squads in tournaments (docs/reusable-teams-plan.md sections 7 and 8.2): who of the club may take a place in a
/// tournament, making and leaving out places, and what a club change does to its squads — past never changes, upcoming
/// squads and waiting entries follow by themselves (C-5, C-20), live ones are asked about in one sheet (C-6). Stages
/// changes only; the club and entry services save. Scoring, stats and schedules only ever see the ordinary
/// <see cref="Team"/> and <see cref="Player"/> rows made here.
/// </summary>
public sealed class ClubSquads(
    IDataService<Team> teams,
    IDataService<Player> players,
    IDataService<PoolPlayer> pool,
    IDataService<TournamentEntry> entries,
    IDataService<TournamentEntryPlayer> entryPlayers,
    IDataService<MatchPlayer> footballLineUps,
    IDataService<CricketMatchPlayer> cricketLineUps,
    TimeProvider clock)
{
    /// <summary>A club squad in a tournament not yet over.</summary>
    public sealed record Squad(int TeamId, string TeamName, int TournamentId, string TournamentName, TournamentStatus Status, Sport Sport);

    /// <summary>
    /// Why each of these club players can't take a place in the tournament ("Plays for Tigers in this tournament"), by
    /// club player id; missing when they can. One place per person per tournament holds both ways with the player pool.
    /// <paramref name="squadTeamId"/> is the club's own squad there, if it has one.
    /// </summary>
    public async Task<Dictionary<int, string>> ReasonsAsync(int tournamentId, IReadOnlyCollection<ClubPlayer> people, int? squadTeamId)
    {
        var reasons = new Dictionary<int, string>();
        if (people.Count == 0) return reasons;

        var users = people.Where(p => p.UserId != null).Select(p => p.UserId!.Value).Distinct().ToList();
        var emails = people.Where(p => p.UserId == null && p.EmailId != null).Select(p => p.EmailId!).Distinct().ToList();
        var phones = people.Where(p => p.UserId == null && p.PhoneNumber != null).Select(p => p.PhoneNumber!).Distinct().ToList();
        var ids = people.Select(p => p.ClubPlayerId).Where(id => id > 0).ToList();

        var places = await players.QueryAsync(q => q
            .Where(p => p.TournamentId == tournamentId
                        && ((p.UserId != null && users.Contains(p.UserId.Value))
                            || (p.UserId == null && p.EmailId != null && emails.Contains(p.EmailId))
                            || (p.UserId == null && p.PhoneNumber != null && phones.Contains(p.PhoneNumber))
                            || (p.ClubPlayerId != null && ids.Contains(p.ClubPlayerId.Value))))
            .Select(p => new { p.UserId, p.EmailId, p.PhoneNumber, p.ClubPlayerId, p.TeamId, TeamName = p.Team!.Name, p.LeftOn }), tracking: false);
        var pooled = users.Count == 0 ? [] : (await pool.QueryAsync(q => q
            .Where(p => p.TournamentId == tournamentId && !p.IsWithdrawn && p.PlayerId == null && p.UserId != null && users.Contains(p.UserId.Value))
            .Select(p => p.UserId!.Value), tracking: false)).ToHashSet();

        foreach (var person in people)
        {
            var place = places.FirstOrDefault(p =>
                (person.ClubPlayerId > 0 && p.ClubPlayerId == person.ClubPlayerId && p.TeamId == squadTeamId)
                || (person.UserId != null && p.UserId == person.UserId)
                || (person.UserId == null && person.EmailId != null && p.EmailId == person.EmailId)
                || (person.UserId == null && person.PhoneNumber != null && p.PhoneNumber == person.PhoneNumber));
            if (place != null)
            {
                reasons[person.ClubPlayerId] = place.TeamId == squadTeamId
                    ? place.LeftOn == null ? "Already in the squad" : "Left the squad. Bring them back from the tournament's team page"
                    : $"Plays for {place.TeamName} in this tournament";
                continue;
            }
            if (person.UserId is { } u && pooled.Contains(u)) reasons[person.ClubPlayerId] = "In this tournament's player pool";
        }
        return reasons;
    }

    /// <summary>A squad place copied from a club player (a linked one's name and profile already mirror their account).</summary>
    public Player NewPlace(ClubPlayer from, int teamId, int tournamentId, Sport sport)
    {
        var place = new Player
        {
            Name = from.Name,
            JerseyNumber = from.JerseyNumber,
            Position = sport == Sport.Football ? from.Position : null,
            TeamId = teamId,
            TournamentId = tournamentId,
            UserId = from.UserId,
            EmailId = from.EmailId,
            PhoneNumber = from.PhoneNumber,
            Cricket = sport == Sport.Cricket && from.Cricket != null ? CricketProfiles.ForPlayer(from.Cricket) : null
        };
        if (from.ClubPlayerId > 0) place.ClubPlayerId = from.ClubPlayerId;
        else place.ClubPlayer = from; // added in this same save
        players.Add(place);
        return place;
    }

    /// <summary>
    /// Takes a place out of its squad: someone who has played stays on the scorecards (<see cref="Player.LeftOn"/>),
    /// anyone else is deleted, as <c>PLAYER_DELETE</c> does.
    /// </summary>
    public async Task LeaveOutAsync(Player place, Team? team)
    {
        if (place.LeftOn != null) return;
        if (await footballLineUps.AnyAsync(m => m.PlayerId == place.Id) || await cricketLineUps.AnyAsync(m => m.PlayerId == place.Id))
            place.LeftOn = clock.GetUtcNow().UtcDateTime;
        else
            players.Remove(place);
        if (team != null && team.DefaultCaptainPlayerId == place.Id) team.DefaultCaptainPlayerId = null;
    }

    /// <summary>The club's squads in tournaments that are still to come or being played.</summary>
    public Task<List<Squad>> OpenSquadsAsync(int clubId) =>
        teams.QueryAsync(q => q
            .Where(t => t.ClubId == clubId
                        && (t.Tournament!.Status == TournamentStatus.Upcoming || t.Tournament.Status == TournamentStatus.Live))
            .OrderBy(t => t.Tournament!.StartDate).ThenBy(t => t.TournamentId)
            .Select(t => new Squad(t.Id, t.Name, t.TournamentId, t.Tournament!.Name, t.Tournament.Status, t.Tournament.Sport)), tracking: false);

    /// <summary>
    /// A player joined the club (C-5): every upcoming squad and waiting entry takes them now; the live tournaments come
    /// back as the question for the admin (null when there are none). The club player must be saved already (it has an id).
    /// </summary>
    public async Task<ClubSquadQuestionDto?> AfterAddedAsync(Club club, ClubPlayer person)
    {
        var question = new ClubSquadQuestionDto { Change = "add", PlayerName = person.Name };
        foreach (var squad in await OpenSquadsAsync(club.ClubId))
        {
            var reason = (await ReasonsAsync(squad.TournamentId, [person], squad.TeamId)).GetValueOrDefault(person.ClubPlayerId);
            if (squad.Status == TournamentStatus.Upcoming)
            {
                if (reason != null) continue;
                NewPlace(person, squad.TeamId, squad.TournamentId, club.Sport);
                question.Done.Add(squad.TournamentName);
            }
            else
            {
                question.Live.Add(new ClubSquadChoiceDto
                {
                    TournamentId = squad.TournamentId,
                    TournamentName = squad.TournamentName,
                    TeamId = squad.TeamId,
                    TeamName = squad.TeamName,
                    Reason = reason
                });
            }
        }

        foreach (var entry in await entries.ListAsync(e => e.ClubId == club.ClubId && e.Status == EntryStatus.Waiting, tracking: false))
        {
            if ((await ReasonsAsync(entry.TournamentId, [person], null)).ContainsKey(person.ClubPlayerId)) continue;
            entryPlayers.Add(new TournamentEntryPlayer { TournamentEntryId = entry.TournamentEntryId, ClubPlayerId = person.ClubPlayerId });
        }
        return question.Live.Count == 0 ? null : question;
    }

    /// <summary>
    /// A player is leaving the club (C-20): upcoming squads and waiting entries drop them now; their places in live
    /// tournaments come back as the question (with each place's id, since the club player is about to go). Their history
    /// is never touched.
    /// </summary>
    public async Task<ClubSquadQuestionDto?> BeforeRemovedAsync(Club club, ClubPlayer person)
    {
        var question = new ClubSquadQuestionDto { Change = "remove", PlayerName = person.Name };
        var squads = await OpenSquadsAsync(club.ClubId);
        var teamIds = squads.Select(s => s.TeamId).ToList();
        var places = await players.ListAsync(p => p.ClubPlayerId == person.ClubPlayerId && p.LeftOn == null && teamIds.Contains(p.TeamId));
        foreach (var place in places)
        {
            var squad = squads.First(s => s.TeamId == place.TeamId);
            if (squad.Status == TournamentStatus.Upcoming)
            {
                await LeaveOutAsync(place, await teams.FirstOrDefaultAsync(t => t.Id == place.TeamId));
                question.Done.Add(squad.TournamentName);
            }
            else
            {
                question.Live.Add(new ClubSquadChoiceDto
                {
                    TournamentId = squad.TournamentId,
                    TournamentName = squad.TournamentName,
                    TeamId = squad.TeamId,
                    TeamName = squad.TeamName,
                    PlayerId = place.Id
                });
            }
        }

        // Ticks on entries go with the club player (and must, for the database's link).
        foreach (var tick in await entryPlayers.ListAsync(t => t.ClubPlayerId == person.ClubPlayerId)) entryPlayers.Remove(tick);
        return question.Live.Count == 0 ? null : question;
    }

    /// <summary>A club player's details changed: their places in upcoming and live squads follow; past squads keep theirs.</summary>
    public async Task FollowDetailsAsync(Club club, ClubPlayer person)
    {
        var teamIds = (await OpenSquadsAsync(club.ClubId)).Select(s => s.TeamId).ToList();
        if (teamIds.Count == 0) return;
        foreach (var place in await players.ListAsync(p => p.ClubPlayerId == person.ClubPlayerId && teamIds.Contains(p.TeamId), PlayerQueries.WithCricketProfile))
        {
            place.JerseyNumber = person.JerseyNumber;
            if (club.Sport == Sport.Football) place.Position = person.Position;
            if (place.UserId != null) continue; // a linked place follows the account instead
            place.Name = person.Name;
            if (club.Sport == Sport.Cricket && person.Cricket != null)
            {
                if (place.Cricket == null) place.Cricket = CricketProfiles.ForPlayer(person.Cricket);
                else CricketProfiles.Copy(person.Cricket, place.Cricket);
            }
        }
    }
}
