using TournamentScheduler.Api.Auth;
using TournamentScheduler.Api.Data.DataServices;
using TournamentScheduler.Api.Models;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Models.Cricket;
using TournamentScheduler.Api.Models.Messaging;
using TournamentScheduler.Api.Services.Accounts;
using TournamentScheduler.Api.Services.Notifications;

namespace TournamentScheduler.Api.Services.Access;

public interface IPlayerLinkService
{
    /// <summary>
    /// An owner set a squad place's email and/or mobile number (adding or editing it). For each,
    /// null leaves it alone and "" clears it. If either belongs to an account the place is linked
    /// to it now; otherwise it waits for that person to sign in. A place stays linked while either
    /// still belongs to its person. Stages the change; the caller saves.
    /// </summary>
    Task<ServiceError?> SetContactsAsync(Player place, int tournamentId, string? email, string? phone);

    /// <summary>Links a place to an account: the account's name, its email, and (cricket) its profile.</summary>
    Task LinkAsync(Player place, User user, Sport sport);

    /// <summary>The place goes back to being just a name. The name stays: it's on the scorecards.</summary>
    void Unlink(Player place);

    /// <summary>
    /// On sign-in, or when the person adds an email or number: places an organiser gave either,
    /// waiting for the account. Returns how many were linked.
    /// </summary>
    Task<int> LinkWaitingPlacesAsync(User user);

    /// <summary>After the person changes their name, email or cricket profile: every place linked to them follows.</summary>
    Task FollowAccountAsync(User user, UserCricketProfile? profile);

    /// <summary>The team the person already plays for in this tournament, if any.</summary>
    Task<string?> TeamAlreadyInAsync(int tournamentId, int userId, int? exceptPlayerId = null);

    /// <summary>The account that signs in with this email or number — one a place would link to.</summary>
    Task<User?> AccountWithAsync(Contact contact);

    /// <summary>The team whose unlinked place is waiting on this email or number in this tournament, if any.</summary>
    Task<string?> TeamWaitingOnAsync(int tournamentId, Contact contact);
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
    private readonly IDataService<Team> _teams;
    private readonly INotifier _notifier;
    private readonly ICurrentUser _currentUser;
    private readonly IDataService<Models.Auctions.PoolPlayer> _pool;
    private readonly IDataService<Models.Clubs.ClubPlayer> _clubPlayers;

    public PlayerLinkService(
        IDataService<Player> players,
        IDataService<User> users,
        IDataService<Tournament> tournaments,
        IDataService<UserCricketProfile> profiles,
        IDataService<Team> teams,
        INotifier notifier,
        ICurrentUser currentUser,
        IDataService<Models.Auctions.PoolPlayer> pool,
        IDataService<Models.Clubs.ClubPlayer> clubPlayers)
    {
        _clubPlayers = clubPlayers;
        _pool = pool;
        _players = players;
        _users = users;
        _tournaments = tournaments;
        _profiles = profiles;
        _teams = teams;
        _notifier = notifier;
        _currentUser = currentUser;
    }

    public async Task<ServiceError?> SetContactsAsync(Player place, int tournamentId, string? email, string? phone)
    {
        if (email == null && phone == null) return null;

        var newEmail = place.EmailId;
        if (email != null)
        {
            newEmail = string.IsNullOrWhiteSpace(email) ? null : AccountService.NormaliseEmail(email);
            if (newEmail == null && !string.IsNullOrWhiteSpace(email))
                return BadRequest("Enter a valid email address for the player, or leave it empty.");
        }

        var newPhone = place.PhoneNumber;
        if (phone != null)
        {
            newPhone = string.IsNullOrWhiteSpace(phone) ? null : Contacts.NormalisePhone(phone);
            if (newPhone == null && !string.IsNullOrWhiteSpace(phone))
                return BadRequest("Enter a valid mobile number for the player, or leave it empty.");
        }

        if (newEmail == place.EmailId && newPhone == place.PhoneNumber) return null;

        var byEmail = newEmail == null ? null : await _users.FirstOrDefaultAsync(u => u.EmailId == newEmail && u.DeletedOn == null && !u.IsDisabled);
        var byPhone = newPhone == null ? null : await _users.FirstOrDefaultAsync(u => u.PhoneNumber == newPhone && u.DeletedOn == null && !u.IsDisabled);
        if (byEmail != null && byPhone != null && byEmail.UserId != byPhone.UserId)
            return Conflict("That email and mobile number belong to two different people. Give one of them.");
        var user = byEmail ?? byPhone;

        // Still the same person: keep the link, and the number the owner typed.
        if (user != null && user.UserId == place.UserId)
        {
            place.EmailId = user.EmailId; // a linked place shows the account's email
            place.PhoneNumber = newPhone;
            return null;
        }

        if (user != null && await TeamAlreadyInAsync(tournamentId, user.UserId, place.Id) is { } team)
        {
            // Someone left out after playing (2026-11-20) still holds their one place here.
            var left = await _players.AnyAsync(p => p.TournamentId == tournamentId && p.UserId == user.UserId && p.Id != place.Id && p.LeftOn != null);
            return Conflict(left
                ? $"{user.Name} played for {team} earlier in this tournament. Bring them back from that team's left players instead."
                : $"{user.Name} is already in {team} in this tournament.");
        }

        Unlink(place);
        place.EmailId = newEmail;
        place.PhoneNumber = newPhone;
        if (user != null)
        {
            await LinkAsync(place, user, await SportOf(tournamentId));
            if (user.UserId != _currentUser.UserId)
            {
                await _notifier.NotifyAsync(user.UserId, NotificationTemplateCode.AddedToTeam, Notifier.With(
                        ("ActorName", string.IsNullOrWhiteSpace(_currentUser.Name) ? "Someone" : _currentUser.Name),
                        ("TeamName", await TeamNameAsync(place.TeamId)),
                        ("TournamentName", await _notifier.TournamentNameAsync(tournamentId))),
                    new NotificationLink(tournamentId));
            }
        }
        return null;
    }

    private async Task<string> TeamNameAsync(int teamId) =>
        await _teams.QueryFirstOrDefaultAsync(q => q.Where(t => t.Id == teamId).Select(t => t.Name), tracking: false) ?? "a team";

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
        place.PhoneNumber = null;
    }

    public async Task<int> LinkWaitingPlacesAsync(User user)
    {
        if (user.EmailId == null && user.PhoneNumber == null) return 0;

        var email = user.EmailId;
        var phone = user.PhoneNumber;

        // Player pools wait for accounts the same way (2026-11-17): one pool place per person per tournament.
        var waitingInPools = await _pool.ListAsync(p => p.UserId == null && ((email != null && p.EmailId == email) || (phone != null && p.PhoneNumber == phone)));
        if (waitingInPools.Count > 0)
        {
            var poolIds = waitingInPools.Select(p => p.TournamentId).Distinct().ToList();
            var inPool = (await _pool.QueryAsync(q => q.Where(p => p.UserId == user.UserId && poolIds.Contains(p.TournamentId)).Select(p => p.TournamentId), tracking: false)).ToHashSet();
            foreach (var p in waitingInPools.OrderBy(p => p.Id))
            {
                if (!inPool.Add(p.TournamentId)) continue;
                p.UserId = user.UserId;
                p.Name = user.Name;
                p.EmailId = user.EmailId;
            }
        }
        await LinkWaitingClubPlacesAsync(user);

        var waiting = await _players.ListAsync(
            p => p.UserId == null && ((email != null && p.EmailId == email) || (phone != null && p.PhoneNumber == phone)),
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

            var by = phone != null && place.PhoneNumber == phone ? "number" : "email";
            await _notifier.NotifyAsync(user.UserId, NotificationTemplateCode.JoinedTeamOnSignUp, Notifier.With(
                    ("TeamName", await TeamNameAsync(place.TeamId)),
                    ("TournamentName", await _notifier.TournamentNameAsync(place.TournamentId)),
                    ("ContactKind", by)),
                new NotificationLink(place.TournamentId));
        }
        return linked;
    }

    /// <summary>
    /// Lasting teams' players waiting on this email or number (2026-11-20): linked now, one place per person per club, the
    /// account's name and (cricket) profile copied as on a squad place.
    /// </summary>
    private async Task LinkWaitingClubPlacesAsync(User user)
    {
        var email = user.EmailId;
        var phone = user.PhoneNumber;
        var waiting = await _clubPlayers.ListAsync(p => p.UserId == null && ((email != null && p.EmailId == email) || (phone != null && p.PhoneNumber == phone)));
        if (waiting.Count == 0) return;

        var clubIds = waiting.Select(p => p.ClubId).Distinct().ToList();
        var inClub = (await _clubPlayers.QueryAsync(q => q.Where(p => p.UserId == user.UserId && clubIds.Contains(p.ClubId)).Select(p => p.ClubId), tracking: false)).ToHashSet();
        var mine = await _profiles.GetByIdAsync(user.UserId);
        foreach (var place in waiting.OrderBy(p => p.ClubPlayerId))
        {
            if (!inClub.Add(place.ClubId)) continue;
            place.UserId = user.UserId;
            place.EmailId = user.EmailId;
            place.Name = user.Name;
            if (mine != null && place.Cricket != null) CricketProfiles.Copy(mine, place.Cricket);
        }
    }

    public async Task FollowAccountAsync(User user, UserCricketProfile? profile)
    {
        // A lasting team's players show their person's name and profile too.
        foreach (var p in await _clubPlayers.ListAsync(p => p.UserId == user.UserId))
        {
            p.Name = user.Name;
            p.EmailId = user.EmailId;
            if (profile != null && p.Cricket != null) CricketProfiles.Copy(profile, p.Cricket);
        }

        // A pool place shows its person's name too, until it becomes a squad place.
        foreach (var p in await _pool.ListAsync(p => p.UserId == user.UserId && p.PlayerId == null))
        {
            p.Name = user.Name;
            p.EmailId = user.EmailId;
        }

        var places = await _players.ListAsync(p => p.UserId == user.UserId, Data.Queries.PlayerQueries.WithCricketProfile);
        if (places.Count == 0) return;

        var sports = profile == null ? new Dictionary<int, Sport>() : await SportsOf(places.Select(p => p.TournamentId).Distinct().ToList());
        foreach (var place in places)
        {
            place.Name = user.Name;
            place.EmailId = user.EmailId; // a linked place shows the account's email; the number stays what the owner typed
            if (profile == null || sports.GetValueOrDefault(place.TournamentId) != Sport.Cricket) continue;

            if (place.Cricket == null) place.Cricket = CricketProfiles.ForPlayer(profile);
            else CricketProfiles.Copy(profile, place.Cricket);
        }
    }

    public async Task<string?> TeamAlreadyInAsync(int tournamentId, int userId, int? exceptPlayerId = null) =>
        await _players.QueryFirstOrDefaultAsync(q => q
            .Where(p => p.TournamentId == tournamentId && p.UserId == userId && p.Id != exceptPlayerId)
            .Select(p => p.Team!.Name), tracking: false);

    public Task<User?> AccountWithAsync(Contact contact) => contact.IsEmail
        ? _users.FirstOrDefaultAsync(u => u.EmailId == contact.Value && u.DeletedOn == null && !u.IsDisabled)
        : _users.FirstOrDefaultAsync(u => u.PhoneNumber == contact.Value && u.DeletedOn == null && !u.IsDisabled);

    public async Task<string?> TeamWaitingOnAsync(int tournamentId, Contact contact)
    {
        var value = contact.Value;
        return contact.IsEmail
            ? await _players.QueryFirstOrDefaultAsync(q => q
                .Where(p => p.TournamentId == tournamentId && p.UserId == null && p.EmailId == value)
                .Select(p => p.Team!.Name), tracking: false)
            : await _players.QueryFirstOrDefaultAsync(q => q
                .Where(p => p.TournamentId == tournamentId && p.UserId == null && p.PhoneNumber == value)
                .Select(p => p.Team!.Name), tracking: false);
    }

    private async Task<Sport> SportOf(int tournamentId) =>
        await _tournaments.QueryFirstOrDefaultAsync(q => q.Where(t => t.Id == tournamentId).Select(t => t.Sport), tracking: false);

    private async Task<Dictionary<int, Sport>> SportsOf(List<int> tournamentIds) =>
        (await _tournaments.QueryAsync(q => q
            .Where(t => tournamentIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Sport }), tracking: false))
        .ToDictionary(t => t.Id, t => t.Sport);
}
