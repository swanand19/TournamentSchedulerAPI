using TournamentScheduler.Api.Data;
using TournamentScheduler.Api.Data.DataServices;
using TournamentScheduler.Api.Models;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Models.Auctions;
using TournamentScheduler.Api.Models.Cricket;
using TournamentScheduler.Api.Services.Access;

namespace TournamentScheduler.Api.Services.Auctions;

public interface ISquadMaker
{
    /// <summary>
    /// "Confirm teams": every placed pool player becomes an ordinary squad place in their team. Saves
    /// once (for the places' ids) and stages the rest; null, or why it can't (a person already in a team).
    /// </summary>
    Task<string?> MakeAsync(Tournament tournament, IReadOnlyList<(PoolPlayer Pool, int TeamId)> places);
}

/// <summary>
/// Turns pool players into squad places, the same way after an auction and after a draw: linked to
/// the same account (name and cricket profile copied from it), or waiting on the same email or number.
/// From then on scoring, stats and profiles never know there was a pool.
/// </summary>
public class SquadMaker(IDataService<Player> players, IDataService<User> users, IPlayerLinkService links, IUnitOfWork unitOfWork) : ISquadMaker
{
    public async Task<string?> MakeAsync(Tournament tournament, IReadOnlyList<(PoolPlayer Pool, int TeamId)> places)
    {
        var sport = tournament.Sport;
        var made = new List<(PoolPlayer Pool, Player Place)>();
        foreach (var (p, teamId) in places)
        {
            var place = new Player { Name = p.Name, TeamId = teamId, TournamentId = tournament.Id };
            if (sport == Sport.Football) place.Position = p.Position;
            else if (p.CricketRole is { } role) place.Cricket = new PlayerCricketProfile { PrimaryRole = role };

            if (p.UserId is int userId && await users.GetByIdAsync(userId) is { } user)
            {
                if (await links.TeamAlreadyInAsync(tournament.Id, userId) is { } already)
                    return $"{p.Name} is already in {already}. Take them out of {already}, or out of the pool, first.";
                await links.LinkAsync(place, user, sport);
            }
            else
            {
                place.EmailId = p.EmailId;
                place.PhoneNumber = p.PhoneNumber;
            }
            players.Add(place);
            made.Add((p, place));
        }

        await unitOfWork.SaveChangesAsync(); // the places' ids
        foreach (var (pool, place) in made) pool.PlayerId = place.Id;

        // Places waiting on an email or number that has an account by now link at once.
        foreach (var (_, place) in made.Where(m => m.Place.UserId == null && (m.Place.EmailId != null || m.Place.PhoneNumber != null)))
        {
            var email = place.EmailId;
            var phone = place.PhoneNumber;
            place.EmailId = null;
            place.PhoneNumber = null;
            if (await links.SetContactsAsync(place, tournament.Id, email ?? "", phone ?? "") is not null)
            {
                place.EmailId = email; // someone else has them: leave the place waiting on what was typed
                place.PhoneNumber = phone;
            }
        }
        return null;
    }
}
