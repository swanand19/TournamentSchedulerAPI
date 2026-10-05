using TournamentScheduler.Api.Data.DataServices;
using TournamentScheduler.Api.Models;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Models.Cricket;
using TournamentScheduler.Api.Services.Accounts;

namespace TournamentScheduler.Api.Services.Access;

public interface IPlayerLinkService
{
    /// <summary>
    /// An owner set a squad place's email (adding or editing it): null leaves it alone, "" clears
    /// it and unlinks, an address with an account links to it now, one without waits for sign-up.
    /// Stages the change; the caller saves.
    /// </summary>
    Task<ServiceError?> SetEmailAsync(Player place, int tournamentId, string? email);

    /// <summary>Links a place to an account: the account's name, its email, and (cricket) its profile.</summary>
    Task LinkAsync(Player place, User user, Sport sport);

    /// <summary>The place goes back to being just a name. The name stays: it's on the scorecards.</summary>
    void Unlink(Player place);

    /// <summary>On sign-in: places an organiser gave this email, waiting for the account. Returns how many were linked.</summary>
    Task<int> LinkWaitingPlacesAsync(User user);

    /// <summary>After the person changes their name or cricket profile: every place linked to them follows.</summary>
    Task FollowAccountAsync(User user, UserCricketProfile? profile);

    /// <summary>The team the person already plays for in this tournament, if any.</summary>
    Task<string?> TeamAlreadyInAsync(int tournamentId, int userId, int? exceptPlayerId = null);
}

/// <summary>
/// Squad places and accounts (docs/accounts-and-access-plan.md §6). A place is linked to at most one
/// account, and an account to at most one place per tournament (a unique index backs this up).
/// While linked, the place shows the account's name and, in cricket, the person's own profile —
/// both copied onto the place, so nothing that reads a squad has to know about accounts.
/// </summary>
public class PlayerLinkService : ServiceBase, IPlayerLinkService
{
    private readonly IDataService<Player> _players;
    private readonly IDataService<User> _users;
    private readonly IDataService<Tournament> _tournaments;
    private readonly IDataService<UserCricketProfile> _profiles;

    public PlayerLinkService(
        IDataService<Player> players,
        IDataService<User> users,
        IDataService<Tournament> tournaments,
        IDataService<UserCricketProfile> profiles)
    {
        _players = players;
        _users = users;
        _tournaments = tournaments;
        _profiles = profiles;
    }

    public async Task<ServiceError?> SetEmailAsync(Player place, int tournamentId, string? email)
    {
        if (email == null) return null;

        if (string.IsNullOrWhiteSpace(email))
        {
            Unlink(place);
            return null;
        }

        var normalised = AccountService.NormaliseEmail(email);
        if (normalised == null) return BadRequest("Enter a valid email address for the player, or leave it empty.");
        if (normalised == place.EmailId) return null;

        var user = await _users.FirstOrDefaultAsync(u => u.EmailId == normalised && u.DeletedOn == null && !u.IsDisabled);
        if (user != null && await TeamAlreadyInAsync(tournamentId, user.UserId, place.Id) is { } team)
            return Conflict($"{user.Name} is already in {team} in this tournament.");

        Unlink(place);
        place.EmailId = normalised;
        if (user != null)
            await LinkAsync(place, user, await SportOf(tournamentId));
        return null;
    }

    public async Task LinkAsync(Player place, User user, Sport sport)
    {
        place.UserId = user.UserId;
        place.EmailId = user.EmailId;
        place.Name = user.Name;
        if (sport != Sport.Cricket) return;

        var mine = await _profiles.GetByIdAsync(user.UserId);
        if (mine != null)
        {
            if (place.Cricket == null) place.Cricket = CricketProfiles.ForPlayer(mine);
            else CricketProfiles.Copy(mine, place.Cricket);
        }
        else if (place.Cricket != null)
        {
            // Nothing of their own yet: start from what the organiser described.
            var adopted = new UserCricketProfile { UserId = user.UserId };
            CricketProfiles.Copy(place.Cricket, adopted);
            _profiles.Add(adopted);
        }
    }

    public void Unlink(Player place)
    {
        place.UserId = null;
        place.User = null;
        place.EmailId = null; // or the next sign-in would link it straight back
    }

    public async Task<int> LinkWaitingPlacesAsync(User user)
    {
        if (user.EmailId == null) return 0;

        var waiting = await _players.ListAsync(
            p => p.EmailId == user.EmailId && p.UserId == null,
            q => Data.Queries.PlayerQueries.WithCricketProfile(q).OrderBy(p => p.Id));
        if (waiting.Count == 0) return 0;

        var tournamentIds = waiting.Select(p => p.TournamentId).Distinct().ToList();
        var sports = await SportsOf(tournamentIds);
        var alreadyIn = (await _players.QueryAsync(q => q
            .Where(p => p.UserId == user.UserId && tournamentIds.Contains(p.TournamentId))
            .Select(p => p.TournamentId), tracking: false)).ToHashSet();

        var linked = 0;
        foreach (var place in waiting)
        {
            // An organiser who typed the same email on two places: the first one wins, the other waits.
            if (!alreadyIn.Add(place.TournamentId)) continue;
            await LinkAsync(place, user, sports.GetValueOrDefault(place.TournamentId));
            linked++;
        }
        return linked;
    }

    public async Task FollowAccountAsync(User user, UserCricketProfile? profile)
    {
        var places = await _players.ListAsync(p => p.UserId == user.UserId, Data.Queries.PlayerQueries.WithCricketProfile);
        if (places.Count == 0) return;

        var sports = profile == null ? new Dictionary<int, Sport>() : await SportsOf(places.Select(p => p.TournamentId).Distinct().ToList());
        foreach (var place in places)
        {
            place.Name = user.Name;
            if (profile == null || sports.GetValueOrDefault(place.TournamentId) != Sport.Cricket) continue;

            if (place.Cricket == null) place.Cricket = CricketProfiles.ForPlayer(profile);
            else CricketProfiles.Copy(profile, place.Cricket);
        }
    }

    public async Task<string?> TeamAlreadyInAsync(int tournamentId, int userId, int? exceptPlayerId = null) =>
        await _players.QueryFirstOrDefaultAsync(q => q
            .Where(p => p.TournamentId == tournamentId && p.UserId == userId && p.Id != exceptPlayerId)
            .Select(p => p.Team!.Name), tracking: false);

    private async Task<Sport> SportOf(int tournamentId) =>
        await _tournaments.QueryFirstOrDefaultAsync(q => q.Where(t => t.Id == tournamentId).Select(t => t.Sport), tracking: false);

    private async Task<Dictionary<int, Sport>> SportsOf(List<int> tournamentIds) =>
        (await _tournaments.QueryAsync(q => q
            .Where(t => tournamentIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Sport }), tracking: false))
        .ToDictionary(t => t.Id, t => t.Sport);
}
