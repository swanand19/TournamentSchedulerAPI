using TournamentScheduler.Api.Auth;
using TournamentScheduler.Api.Data;
using TournamentScheduler.Api.Data.DataServices;
using TournamentScheduler.Api.Data.Queries;
using TournamentScheduler.Api.Models;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Models.Cricket;
using Microsoft.Extensions.Options;
using TournamentScheduler.Api.Services.Access;
using TournamentScheduler.Api.Services.Accounts;
using TournamentScheduler.Api.Pictures;
using TournamentScheduler.Api.Models.Messaging;
using TournamentScheduler.Api.Services.Notifications;

namespace TournamentScheduler.Api.Services;

public interface ITeamService
{
    Task<ServiceResult<List<Team>>> GetTeamsAsync(int tournamentId);
    Task<ServiceResult<Team>> GetTeamAsync(int tournamentId, int id);
    Task<ServiceResult<Team>> CreateTeamAsync(int tournamentId, CreateTeamRequest request);
    Task<ServiceResult<Team>> UpdateTeamAsync(int tournamentId, int id, UpdateTeamRequest request);
    Task<ServiceResult<bool>> DeleteTeamAsync(int tournamentId, int id);
    Task<ServiceResult<Team>> ResetJoinCodeAsync(int tournamentId, int id);
    Task<ServiceResult<PlayerLookupDto>> LookupPlayerAsync(int tournamentId, int teamId, PlayerLookupRequest request);
    Task<ServiceResult<Player>> AddPlayerAsync(int tournamentId, int teamId, CreatePlayerRequest request);
    Task<ServiceResult<Player>> UpdatePlayerAsync(int tournamentId, int teamId, int playerId, UpdatePlayerRequest request);
    Task<ServiceResult<Player>> UnlinkPlayerAsync(int tournamentId, int teamId, int playerId);
    Task<ServiceResult<bool>> DeletePlayerAsync(int tournamentId, int teamId, int playerId);
    Task<ServiceResult<Team>> RestorePlayerAsync(int tournamentId, int teamId, int playerId);
    Task<ServiceResult<Team>> AddAdminAsync(int tournamentId, int teamId, AddTeamAdminRequest request);
    Task<ServiceResult<Team>> RemoveAdminAsync(int tournamentId, int teamId, int userId);
}

/// <summary>
/// A tournament's teams and their players, including each player's cricket profile, their link to
/// a sign-in account (phase 4) and the team's join code. Players' emails and join codes are shown
/// to the people who may change that squad: the tournament's owners and the team's admins
/// (2026-11-20). A player who has played is never deleted, only left out (<see cref="Player.LeftOn"/>).
/// </summary>
public class TeamService : ServiceBase, ITeamService
{
    private readonly IDataService<Tournament> _tournaments;
    private readonly IDataService<Team> _teams;
    private readonly IDataService<Player> _players;
    private readonly IPlayerLinkService _links;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITournamentContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly PlayerLookups _lookups;
    private readonly SmsOptions _sms;
    private readonly IDataService<Models.Auctions.Auction> _auctions;
    private readonly IDataService<Models.Auctions.TeamDraw> _draws;

    private readonly IDataService<User> _users;
    private readonly PictureLinks _pictureLinks;
    private readonly IDataService<TeamAdmin> _admins;
    private readonly IDataService<MatchPlayer> _footballLineUps;
    private readonly IDataService<CricketMatchPlayer> _cricketLineUps;
    private readonly INotifier _notifier;
    private readonly TimeProvider _clock;
    private readonly IDataService<Models.Clubs.Club> _clubs;
    private readonly IDataService<Models.Clubs.ClubAdmin> _clubAdmins;
    private readonly Models.Public.PublicOptions _public;

    public TeamService(
        IDataService<Tournament> tournaments,
        IDataService<Team> teams,
        IDataService<Player> players,
        IPlayerLinkService links,
        IUnitOfWork unitOfWork,
        ITournamentContext context,
        ICurrentUser currentUser,
        PlayerLookups lookups,
        IOptions<SmsOptions> sms,
        IDataService<Models.Auctions.Auction> auctions,
        IDataService<Models.Auctions.TeamDraw> draws,
        IDataService<User> users,
        PictureLinks pictureLinks,
        IDataService<TeamAdmin> admins,
        IDataService<MatchPlayer> footballLineUps,
        IDataService<CricketMatchPlayer> cricketLineUps,
        INotifier notifier,
        TimeProvider clock,
        IDataService<Models.Clubs.Club> clubs,
        IDataService<Models.Clubs.ClubAdmin> clubAdmins,
        IOptions<Models.Public.PublicOptions> publicOptions)
    {
        _clubs = clubs;
        _clubAdmins = clubAdmins;
        _public = publicOptions.Value;
        _admins = admins;
        _footballLineUps = footballLineUps;
        _cricketLineUps = cricketLineUps;
        _notifier = notifier;
        _clock = clock;
        _users = users;
        _pictureLinks = pictureLinks;
        _auctions = auctions;
        _draws = draws;
        _tournaments = tournaments;
        _teams = teams;
        _players = players;
        _links = links;
        _unitOfWork = unitOfWork;
        _context = context;
        _currentUser = currentUser;
        _lookups = lookups;
        _sms = sms.Value;
    }

    private Task<bool> TournamentExists(int tournamentId) => _tournaments.AnyAsync(t => t.Id == tournamentId);

    private Task<Team?> FindTeam(int tournamentId, int id) =>
        _teams.FirstOrDefaultAsync(t => t.Id == id && t.TournamentId == tournamentId);

    private static string DuplicateName(string name) => $"A team named '{name}' already exists in this tournament.";

    /// <summary>The teams in a running auction are fixed: one added or taken away would change every purse and squad count.</summary>
    private async Task<string?> AuctionLocksTeamsAsync(int tournamentId)
    {
        var status = await _auctions.QueryFirstOrDefaultAsync(q => q.Where(a => a.TournamentId == tournamentId)
            .Select(a => (Models.Auctions.AuctionStatus?)a.Status), tracking: false);
        return status is Models.Auctions.AuctionStatus.Live or Models.Auctions.AuctionStatus.Paused
            or Models.Auctions.AuctionStatus.RoundOver or Models.Auctions.AuctionStatus.Finished
            ? "The player auction is running, so teams can't be added or removed. Finish it, or cancel it, first."
            : await _draws.AnyAsync(d => d.TournamentId == tournamentId && d.Status == Models.Auctions.DrawStatus.Live)
                ? "The team draw is running, so teams can't be added or removed. Confirm the teams, or start the draw again, first."
                : null;
    }

    public async Task<ServiceResult<List<Team>>> GetTeamsAsync(int tournamentId)
    {
        if (!await TournamentExists(tournamentId)) return NotFound("Tournament not found.");

        var teams = await _teams.ListAsync(
            t => t.TournamentId == tournamentId,
            q => TeamQueries.WithPlayersAndProfiles(q).OrderBy(t => t.Name), tracking: false);
        await AnswerAsync(teams);
        return Success(teams);
    }

    public async Task<ServiceResult<Team>> GetTeamAsync(int tournamentId, int id)
    {
        var team = await _teams.FirstOrDefaultAsync(
            t => t.Id == id && t.TournamentId == tournamentId, TeamQueries.WithPlayersAndProfiles, tracking: false);
        if (team == null) return NotFound();
        await AnswerAsync([team]);
        return Success(team);
    }

    /// <summary>A team as it is now, read fresh (untracked) after a change.</summary>
    private async Task<ServiceResult<Team>> Fresh(int tournamentId, int id) => await GetTeamAsync(tournamentId, id);

    public async Task<ServiceResult<Team>> CreateTeamAsync(int tournamentId, CreateTeamRequest request)
    {
        if (!await TournamentExists(tournamentId)) return NotFound("Tournament not found.");
        if (await AuctionLocksTeamsAsync(tournamentId) is { } locked) return Conflict(locked);

        var name = request.Name?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest("Team name is required.");

        if (await _teams.AnyAsync(t => t.Name == name && t.TournamentId == tournamentId))
            return Conflict(DuplicateName(name));

        var team = new Team { Name = name, TournamentId = tournamentId, JoinCode = await NewJoinCode() };
        _teams.Add(team);
        try
        {
            await _unitOfWork.SaveChangesAsync();
        }
        catch (DuplicateEntryException)
        {
            // Another request created the same name between the check above and this save.
            return Conflict(DuplicateName(name));
        }
        return await Fresh(tournamentId, team.Id);
    }

    public async Task<ServiceResult<Team>> UpdateTeamAsync(int tournamentId, int id, UpdateTeamRequest request)
    {
        var team = await FindTeam(tournamentId, id);
        if (team == null) return NotFound();

        var name = request.Name?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest("Team name is required.");

        if (await _teams.AnyAsync(t => t.Name == name && t.TournamentId == tournamentId && t.Id != id))
            return Conflict(DuplicateName(name));

        if (request.SetCaptain)
        {
            if (request.DefaultCaptainPlayerId.HasValue)
            {
                var onThisTeam = await _players.AnyAsync(p => p.Id == request.DefaultCaptainPlayerId && p.TeamId == id && p.LeftOn == null);
                if (!onThisTeam)
                    return BadRequest("The captain must be a player in this team's squad.");
            }
            team.DefaultCaptainPlayerId = request.DefaultCaptainPlayerId;
        }

        team.Name = name;
        await _unitOfWork.SaveChangesAsync();
        return await Fresh(tournamentId, id);
    }

    public async Task<ServiceResult<bool>> DeleteTeamAsync(int tournamentId, int id)
    {
        var team = await FindTeam(tournamentId, id);
        if (team == null) return NotFound();
        if (await AuctionLocksTeamsAsync(tournamentId) is { } locked) return Conflict(locked);

        _teams.Remove(team); // cascades to players
        await _unitOfWork.SaveChangesAsync();
        return Success(true);
    }

    /// <summary>A new join code, for when the old one has been shared too widely. The old one stops working at once.</summary>
    public async Task<ServiceResult<Team>> ResetJoinCodeAsync(int tournamentId, int id)
    {
        var team = await FindTeam(tournamentId, id);
        if (team == null) return NotFound("Team not found.");

        team.JoinCode = await NewJoinCode();
        await _unitOfWork.SaveChangesAsync();
        return await Fresh(tournamentId, id);
    }

    /// <summary>
    /// Who an email or number belongs to, so an owner adding a player can pick the person instead
    /// of typing their name. Limited per owner (<see cref="PlayerLookups"/>): a lookup reveals
    /// whether an account exists, and that's all it reveals.
    /// </summary>
    public async Task<ServiceResult<PlayerLookupDto>> LookupPlayerAsync(int tournamentId, int teamId, PlayerLookupRequest request)
    {
        var team = await FindTeam(tournamentId, teamId);
        if (team == null) return NotFound("Team not found.");

        var (contact, problem) = Contacts.Read(request.Contact, _sms.IsEnabled);
        if (contact is not { } found) return BadRequest(problem!);

        if (_lookups.TryLookup(_currentUser.UserId) is { } wait)
            return TooManyRequests($"That's a lot of lookups. Try again in {Math.Max(1, (int)Math.Ceiling(wait.TotalMinutes))} minutes, or add the player by name.");

        var user = await _links.AccountWithAsync(found);
        return Success(new PlayerLookupDto
        {
            Kind = found.Kind,
            Contact = found.Value,
            Display = Contacts.Display(found.Value),
            Found = user != null,
            Name = user?.Name,
            PlaysFor = user == null ? null : await _links.TeamAlreadyInAsync(tournamentId, user.UserId),
            WaitingIn = user == null ? await _links.TeamWaitingOnAsync(tournamentId, found) : null
        });
    }

    /// <summary>Why a player added without a name can't be: null when their email or number has an account.</summary>
    private async Task<string?> NameNeededAsync(CreatePlayerRequest request)
    {
        foreach (var typed in new[] { request.Email, request.Phone })
        {
            if (string.IsNullOrWhiteSpace(typed)) continue;
            if (Contacts.Parse(typed).Contact is { } contact && await _links.AccountWithAsync(contact) != null)
                return null;
        }

        if (!string.IsNullOrWhiteSpace(request.Email))
            return "No account uses that email yet, so add the player's name. They'll be linked when they sign up.";
        if (!string.IsNullOrWhiteSpace(request.Phone))
            return "No account uses that number yet, so add the player's name. They'll be linked when they sign up.";
        return "Player name is required.";
    }

    public async Task<ServiceResult<Player>> AddPlayerAsync(int tournamentId, int teamId, CreatePlayerRequest request)
    {
        var team = await FindTeam(tournamentId, teamId);
        if (team == null) return NotFound("Team not found.");

        // Someone with an account is shown by their account's name, so they can be added by their
        // email or number alone; anyone else needs a name to show until they sign up.
        var name = request.Name?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name) && await NameNeededAsync(request) is { } nameError)
            return BadRequest(nameError);

        if (CricketProfiles.Problem(request.Cricket) is { } cricketError)
            return BadRequest(cricketError);

        var player = new Player
        {
            Name = name,
            Position = request.Position,
            JerseyNumber = request.JerseyNumber,
            TeamId = teamId,
            TournamentId = tournamentId
        };
        if (request.Cricket != null)
            player.Cricket = CricketProfiles.ForPlayer(request.Cricket);

        // With an email or number that has an account, the place is linked now and takes the account's name.
        if (await _links.SetContactsAsync(player, tournamentId, request.Email, request.Phone) is { } linkError)
            return linkError;

        _players.Add(player);
        if (await SaveLinked() is { } raceError) return raceError;
        Present(player);
        return Success(player);
    }

    public async Task<ServiceResult<Player>> UpdatePlayerAsync(int tournamentId, int teamId, int playerId, UpdatePlayerRequest request)
    {
        var team = await FindTeam(tournamentId, teamId);
        if (team == null) return NotFound("Team not found.");

        var player = await _players.FirstOrDefaultAsync(
            p => p.Id == playerId && p.TeamId == teamId, PlayerQueries.WithCricketProfile);
        if (player == null) return NotFound();
        if (player.LeftOn != null) return Conflict($"{player.Name} has left the squad. Bring them back first.");

        var name = request.Name?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest("Player name is required.");

        if (CricketProfiles.Problem(request.Cricket) is { } cricketError)
            return BadRequest(cricketError);

        if (await _links.SetContactsAsync(player, tournamentId, request.Email, request.Phone) is { } linkError)
            return linkError;

        player.Position = request.Position;
        player.JerseyNumber = request.JerseyNumber;

        // A linked player's name and cricket profile are theirs, kept in step with their account.
        if (!player.IsLinked)
        {
            player.Name = name;

            // Null means "not sent", not "clear it" — a football client never sends this block, and a
            // rename must not silently discard a cricketer's styles.
            if (request.Cricket != null)
            {
                if (player.Cricket == null)
                    player.Cricket = CricketProfiles.ForPlayer(request.Cricket);
                else
                    CricketProfiles.Copy(request.Cricket, player.Cricket);
            }
        }

        if (await SaveLinked() is { } raceError) return raceError;
        Present(player);
        return Success(player);
    }

    /// <summary>
    /// Takes a squad place off the account linked to it — an owner fixing a wrong link, or the
    /// player who picked the wrong name ("Leave team"). The name stays: it's on the scorecards.
    /// </summary>
    public async Task<ServiceResult<Player>> UnlinkPlayerAsync(int tournamentId, int teamId, int playerId)
    {
        var player = await _players.FirstOrDefaultAsync(
            p => p.Id == playerId && p.TeamId == teamId && p.TournamentId == tournamentId, PlayerQueries.WithCricketProfile);
        if (player == null) return NotFound("Player not found.");

        var mine = player.UserId == _currentUser.UserId;
        if (!_context.CanManageTeam(teamId) && !mine)
            return Forbidden("Only the tournament's owners and this team's admins can unlink another player.");
        if (_context.Status is TournamentStatus.Completed or TournamentStatus.Cancelled)
            return Conflict("This tournament is over, so its squads can't be changed.");
        if (!player.IsLinked && player.EmailId == null && player.PhoneNumber == null)
            return Conflict("This player isn't linked to an account.");

        _links.Unlink(player);
        await _unitOfWork.SaveChangesAsync();
        Present(player);
        return Success(player);
    }

    public async Task<ServiceResult<bool>> DeletePlayerAsync(int tournamentId, int teamId, int playerId)
    {
        var team = await FindTeam(tournamentId, teamId);
        if (team == null) return NotFound("Team not found.");

        var player = await _players.FirstOrDefaultAsync(p => p.Id == playerId && p.TeamId == teamId);
        if (player == null) return NotFound();
        if (player.LeftOn != null) return Success(true);

        // Someone who has played is in line-ups, and every goal, ball and card points at them: they leave the squad
        // but stay on the scorecards, the leaderboards and their profile. Nobody else is simply deleted.
        if (await HasPlayedAsync(player.Id))
        {
            player.LeftOn = _clock.GetUtcNow().UtcDateTime;
            if (team.DefaultCaptainPlayerId == player.Id) team.DefaultCaptainPlayerId = null;
        }
        else
        {
            _players.Remove(player);
        }
        await _unitOfWork.SaveChangesAsync();
        return Success(true);
    }

    private async Task<bool> HasPlayedAsync(int playerId) =>
        await _footballLineUps.AnyAsync(m => m.PlayerId == playerId) || await _cricketLineUps.AnyAsync(m => m.PlayerId == playerId);

    /// <summary>A player left out by mistake (or back from injury) rejoins the squad they left.</summary>
    public async Task<ServiceResult<Team>> RestorePlayerAsync(int tournamentId, int teamId, int playerId)
    {
        var player = await _players.FirstOrDefaultAsync(p => p.Id == playerId && p.TeamId == teamId && p.TournamentId == tournamentId);
        if (player == null) return NotFound("Player not found.");
        if (player.LeftOn == null) return Conflict($"{player.Name} is already in the squad.");

        player.LeftOn = null;
        await _unitOfWork.SaveChangesAsync();
        return await Fresh(tournamentId, teamId);
    }

    /// <summary>
    /// An owner names someone to run this team (2026-11-20): one of its linked players, or anyone with an account by
    /// email or number. At most <see cref="TeamAdmin.MaxPerTeam"/>. They're told in their inbox.
    /// </summary>
    public async Task<ServiceResult<Team>> AddAdminAsync(int tournamentId, int teamId, AddTeamAdminRequest request)
    {
        var team = await FindTeam(tournamentId, teamId);
        if (team == null) return NotFound("Team not found.");

        User? user;
        if (request.PlayerId is { } playerId)
        {
            var place = await _players.FirstOrDefaultAsync(p => p.Id == playerId && p.TeamId == teamId, tracking: false);
            if (place == null) return NotFound("That player isn't in this team.");
            if (place.UserId == null) return Conflict($"{place.Name} isn't on the app yet, so they can't run the team. Add them by their email or number once they've signed up.");
            user = await _users.FirstOrDefaultAsync(u => u.UserId == place.UserId && u.DeletedOn == null, tracking: false);
        }
        else
        {
            var (contact, problem) = Contacts.Read(request.Contact, _sms.IsEnabled);
            if (contact is not { } to) return BadRequest(problem!);
            user = await _links.AccountWithAsync(to);
            if (user == null)
                return NotFound(to.IsEmail
                    ? "No one has signed up with that email yet. Ask them to sign up first, then add them."
                    : "No one has signed up with that number yet. Ask them to sign up first, then add them.");
        }
        if (user == null) return NotFound("That account no longer exists.");
        if (user.IsDisabled) return Conflict("That account has been blocked, so it can't run a team.");

        var admins = await _admins.ListAsync(a => a.TeamId == teamId);
        if (admins.Any(a => a.UserId == user.UserId)) return Conflict($"{user.Name} already runs {team.Name}.");
        if (admins.Count >= TeamAdmin.MaxPerTeam) return Conflict($"A team can have {TeamAdmin.MaxPerTeam} admins. Remove one first.");

        var admin = new TeamAdmin { TeamId = teamId, TournamentId = tournamentId, UserId = user.UserId };
        admin.Created(_clock.GetUtcNow().UtcDateTime, _currentUser.UserId);
        _admins.Add(admin);
        if (user.UserId != _currentUser.UserId)
        {
            await _notifier.NotifyAsync(user.UserId, NotificationTemplateCode.MadeTeamAdmin, Notifier.With(
                    ("ActorName", string.IsNullOrWhiteSpace(_currentUser.Name) ? "Someone" : _currentUser.Name),
                    ("TeamName", team.Name),
                    ("TournamentName", await _notifier.TournamentNameAsync(tournamentId))),
                new NotificationLink(tournamentId));
        }

        try
        {
            await _unitOfWork.SaveChangesAsync();
        }
        catch (DuplicateEntryException)
        {
            return Conflict($"{user.Name} was added a moment ago. Reload the team.");
        }
        return await Fresh(tournamentId, teamId);
    }

    /// <summary>An owner takes someone off running this team, or an admin steps down.</summary>
    public async Task<ServiceResult<Team>> RemoveAdminAsync(int tournamentId, int teamId, int userId)
    {
        if (!_context.IsOwner && userId != _currentUser.UserId)
            return Forbidden("Only the tournament's owners can remove a team's admins.");
        if (_context.Status is TournamentStatus.Completed or TournamentStatus.Cancelled)
            return Conflict("This tournament is over, so its teams can't be changed.");

        var admin = await _admins.FirstOrDefaultAsync(a => a.TeamId == teamId && a.TournamentId == tournamentId && a.UserId == userId);
        if (admin == null) return NotFound("They don't run this team.");

        _admins.Remove(admin);
        await _unitOfWork.SaveChangesAsync();
        return await Fresh(tournamentId, teamId);
    }

    /// <summary>Saves a player change that another request may have beaten to a unique index.</summary>
    private async Task<ServiceError?> SaveLinked()
    {
        try
        {
            await _unitOfWork.SaveChangesAsync();
            return null;
        }
        catch (DuplicateEntryException)
        {
            // The one-place-per-tournament index, beaten by another request.
            return Conflict("Someone else changed this squad at the same moment. Reload the teams and try again.");
        }
    }

    private async Task<string> NewJoinCode()
    {
        // A clash is about one in a billion; checking costs one indexed read.
        string code;
        do code = JoinCodes.New();
        while (await _teams.AnyAsync(t => t.JoinCode == code));
        return code;
    }

    /// <summary>
    /// Fills what the caller sees on (untracked) teams: who runs each one, its left players apart from the squad, the
    /// join code, emails and numbers for the people who may change that squad, photos, and which place is theirs.
    /// </summary>
    private async Task AnswerAsync(List<Team> teams)
    {
        var ids = teams.Select(t => t.Id).ToList();
        var admins = ids.Count == 0 ? [] : await _admins.QueryAsync(q => q
            .Where(a => ids.Contains(a.TeamId) && !a.IsDisabled)
            .OrderBy(a => a.Crd)
            .Select(a => new { a.TeamId, a.UserId, a.User!.Name }), tracking: false);
        var me = _currentUser.UserId;

        foreach (var team in teams)
        {
            team.LeftPlayers = team.Players.Where(p => p.LeftOn != null).OrderBy(p => p.Name).ToList();
            team.Players = team.Players.Where(p => p.LeftOn == null).ToList();
            team.Admins = admins.Where(a => a.TeamId == team.Id)
                .Select(a => new TeamAdminDto { UserId = a.UserId, Name = a.Name, IsMe = a.UserId == me, CanRemove = _context.IsOwner || a.UserId == me })
                .ToList();
            team.CanManage = _context.CanManageTeam(team.Id) && !Closed;
            team.CanManageAdmins = _context.IsOwner && !Closed;
            // Keeping it for other tournaments is for the team's own admins only (C-23), once.
            team.CanKeep = team.ClubId == null && admins.Any(a => a.TeamId == team.Id && a.UserId == me) ? true : null;
            Present(team);
        }
        await AddInvitesAsync(teams);
        await AddPhotosAsync(teams.SelectMany(t => t.Players));
    }

    /// <summary>
    /// The invite link (2026-11-23) for whoever may change a squad, until the tournament is over. A team kept from a
    /// lasting one shares that team's link, and only its admins have it; an organiser is told whose it is to share.
    /// </summary>
    private async Task AddInvitesAsync(List<Team> teams)
    {
        var shown = teams.Where(t => _context.CanManageTeam(t.Id) && !Closed).ToList();
        if (shown.Count == 0) return;
        var tournamentName = await _tournaments.QueryFirstOrDefaultAsync(q => q.Where(t => t.Id == _context.TournamentId).Select(t => t.Name), tracking: false) ?? "";
        var clubIds = shown.Where(t => t.ClubId != null).Select(t => t.ClubId!.Value).Distinct().ToList();
        var clubs = clubIds.Count == 0 ? [] : await _clubs.QueryAsync(q => q
            .Where(c => clubIds.Contains(c.ClubId) && !c.IsDisabled)
            .Select(c => new { c.ClubId, c.Name, c.JoinCode }), tracking: false);
        var me = _currentUser.UserId;
        var mine = clubIds.Count == 0 ? [] : await _clubAdmins.QueryAsync(q => q
            .Where(a => clubIds.Contains(a.ClubId) && a.UserId == me && !a.IsDisabled)
            .Select(a => a.ClubId), tracking: false);

        foreach (var team in shown)
        {
            if (team.ClubId is { } clubId)
            {
                var club = clubs.FirstOrDefault(c => c.ClubId == clubId);
                if (club == null) continue;
                team.Invite = mine.Contains(clubId) ? InviteLinks.ForClub(_public, club.JoinCode, club.Name) : InviteLinks.SharedByClub(club.Name);
            }
            else team.Invite = InviteLinks.ForTeam(_public, team.JoinCode, team.Name, tournamentName);
        }
    }

    private bool Closed => _context.Status is TournamentStatus.Completed or TournamentStatus.Cancelled;

    /// <summary>What this caller may see: emails, numbers and the join code for whoever may change the squad; which place is theirs.</summary>
    private void Present(Team team)
    {
        var manages = _context.CanManageTeam(team.Id);
        team.VisibleJoinCode = manages ? team.JoinCode : null;
        foreach (var player in team.Players.Concat(team.LeftPlayers ?? [])) Present(player, manages);
    }

    /// <summary>Linked places show their person's profile photo (HLD P-26), one query for the whole list.</summary>
    private async Task AddPhotosAsync(IEnumerable<Player> players)
    {
        var linked = players.Where(p => p.UserId != null).ToList();
        if (linked.Count == 0) return;
        var ids = linked.Select(p => p.UserId!.Value).Distinct().ToList();
        var photos = (await _users.QueryAsync(q => q
                .Where(u => ids.Contains(u.UserId) && u.PhotoId != null)
                .Select(u => new { u.UserId, u.PhotoId }), tracking: false))
            .ToDictionary(u => u.UserId, u => u.PhotoId);
        foreach (var player in linked)
            player.PhotoUrl = _pictureLinks.For(photos.GetValueOrDefault(player.UserId!.Value));
    }

    private void Present(Player player) => Present(player, _context.CanManageTeam(player.TeamId));

    private void Present(Player player, bool manages)
    {
        player.IsMe = player.UserId != null && player.UserId == _currentUser.UserId;
        player.Email = manages ? player.EmailId : null;
        player.Phone = manages ? player.PhoneNumber : null;
    }
}
