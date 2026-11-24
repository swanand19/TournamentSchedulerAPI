using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using TournamentScheduler.Api.Auth;
using TournamentScheduler.Api.Data;
using TournamentScheduler.Api.Data.DataServices;
using TournamentScheduler.Api.Data.Queries;
using TournamentScheduler.Api.Models;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Models.Clubs;
using TournamentScheduler.Api.Models.Cricket;
using TournamentScheduler.Api.Models.Messaging;
using TournamentScheduler.Api.Models.Public;
using TournamentScheduler.Api.Pictures;
using TournamentScheduler.Api.Services.Access;
using TournamentScheduler.Api.Services.Accounts;
using TournamentScheduler.Api.Services.Cricket;
using TournamentScheduler.Api.Services.Knockouts;
using TournamentScheduler.Api.Services.Notifications;
using TournamentScheduler.Api.Services.Public;

namespace TournamentScheduler.Api.Services.Clubs;

public interface IClubService
{
    Task<ServiceResult<List<ClubListItemDto>>> ListMineAsync();
    Task<ServiceResult<ClubDto>> CreateAsync(CreateClubRequest request);
    Task<ServiceResult<ClubDto>> GetAsync(int clubId);
    Task<ServiceResult<ClubDto>> UpdateAsync(int clubId, UpdateClubRequest request);
    Task<ServiceResult<bool>> DeleteAsync(int clubId);
    Task<ServiceResult<ClubDto>> ResetJoinCodeAsync(int clubId);
    Task<ServiceResult<ClubPlayerLookupDto>> LookupPlayerAsync(int clubId, PlayerLookupRequest request);
    Task<ServiceResult<ClubDto>> AddPlayerAsync(int clubId, ClubPlayerRequest request);
    Task<ServiceResult<ClubDto>> UpdatePlayerAsync(int clubId, int clubPlayerId, ClubPlayerRequest request);
    Task<ServiceResult<ClubDto>> RemovePlayerAsync(int clubId, int clubPlayerId);
    Task<ServiceResult<ClubDto>> AddAdminAsync(int clubId, AddClubAdminRequest request);
    Task<ServiceResult<ClubDto>> RemoveAdminAsync(int clubId, int userId);
    Task<ServiceResult<ClubJoinPreviewDto>> JoinPreviewAsync(JoinTeamPreviewRequest request);
    Task<ServiceResult<ClubJoinResult>> JoinAsync(ClubJoinRequest request);
    Task<ServiceResult<ClubDto>> KeepTeamAsync(int tournamentId, int teamId);

    /// <summary>The live-tournaments question for one club player (from the "joined" notification, or the player's sheet).</summary>
    Task<ServiceResult<ClubSquadQuestionDto>> SquadsPreviewAsync(int clubId, int clubPlayerId);

    /// <summary>The sheet's answer: add the player to these live squads, or leave these places out (C-6, C-20).</summary>
    Task<ServiceResult<ClubDto>> SquadsApplyAsync(int clubId, ClubSquadsApplyRequest request);

    /// <summary>The signed-out page at /team/&lt;code&gt; on the website (C-15, step 5).</summary>
    Task<ServiceResult<JsonNode>> PublicAsync(string code);

    /// <summary>An account being deleted: its club places become plain names, its admin seats go (handed on if it was the last).</summary>
    Task ForgetUserAsync(int userId);
}

/// <summary>
/// Lasting teams (docs/reusable-teams-plan.md, step 2): people see "team" (C-21), the code says club. Anyone signed in
/// makes one (C-13) and may look at any one (C-15); only its admins (1–3, C-16) change it. Players are added the way a
/// squad place is (email or number first, linked to the account; else a name), or join with the club's code. A
/// tournament's team becomes one when one of its admins keeps it (C-23). Club pages never show emails, numbers or the
/// join code except to the club's admins.
/// </summary>
public class ClubService : ServiceBase, IClubService
{
    private readonly IDataService<Club> _clubs;
    private readonly IDataService<ClubAdmin> _admins;
    private readonly IDataService<ClubPlayer> _players;
    private readonly IDataService<Team> _teams;
    private readonly IDataService<Player> _squads;
    private readonly IDataService<Tournament> _tournaments;
    private readonly IDataService<TournamentMember> _members;
    private readonly IDataService<TeamAdmin> _teamAdmins;
    private readonly IDataService<User> _users;
    private readonly IDataService<UserCricketProfile> _profiles;
    private readonly IDataService<Match> _matches;
    private readonly IDataService<CricketMatch> _cricketMatches;
    private readonly IDataService<TournamentEntry> _entries;
    private readonly ClubSquads _squadSync;
    private readonly PublicOptions _public;
    private readonly IStatsService _footballStats;
    private readonly ICricketStatsService _cricketStats;
    private readonly IPlayerLinkService _links;
    private readonly INotifier _notifier;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly PlayerLookups _lookups;
    private readonly JoinCodeAttempts _attempts;
    private readonly SmsOptions _sms;
    private readonly PictureLinks _pictureLinks;
    private readonly IPictureStore _pictureStore;
    private readonly TimeProvider _clock;

    public ClubService(
        IDataService<Club> clubs,
        IDataService<ClubAdmin> admins,
        IDataService<ClubPlayer> players,
        IDataService<Team> teams,
        IDataService<Player> squads,
        IDataService<Tournament> tournaments,
        IDataService<TournamentMember> members,
        IDataService<TeamAdmin> teamAdmins,
        IDataService<User> users,
        IDataService<UserCricketProfile> profiles,
        IDataService<Match> matches,
        IDataService<CricketMatch> cricketMatches,
        IDataService<TournamentEntry> entries,
        ClubSquads squadSync,
        IOptions<PublicOptions> publicOptions,
        IStatsService footballStats,
        ICricketStatsService cricketStats,
        IPlayerLinkService links,
        INotifier notifier,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        PlayerLookups lookups,
        JoinCodeAttempts attempts,
        IOptions<SmsOptions> sms,
        PictureLinks pictureLinks,
        IPictureStore pictureStore,
        TimeProvider clock)
    {
        _clubs = clubs;
        _admins = admins;
        _players = players;
        _teams = teams;
        _squads = squads;
        _tournaments = tournaments;
        _members = members;
        _teamAdmins = teamAdmins;
        _users = users;
        _profiles = profiles;
        _matches = matches;
        _cricketMatches = cricketMatches;
        _entries = entries;
        _squadSync = squadSync;
        _public = publicOptions.Value;
        _footballStats = footballStats;
        _cricketStats = cricketStats;
        _links = links;
        _notifier = notifier;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _lookups = lookups;
        _attempts = attempts;
        _sms = sms.Value;
        _pictureLinks = pictureLinks;
        _pictureStore = pictureStore;
        _clock = clock;
    }

    private int Me => _currentUser.IsSignedIn ? _currentUser.UserId : 0; // 0: the signed-out public page
    private DateTime Now => _clock.GetUtcNow().UtcDateTime;
    private string ActorName => string.IsNullOrWhiteSpace(_currentUser.Name) ? "Someone" : _currentUser.Name;

    private const string Gone = "That team doesn't exist any more.";
    private const string AdminsOnly = "Only this team's admins can do that.";

    /// <summary>The club, tracked, if the caller runs it.</summary>
    private async Task<(Club? Club, ServiceError? Error)> ManagedAsync(int clubId)
    {
        var club = await _clubs.FirstOrDefaultAsync(c => c.ClubId == clubId && !c.IsDisabled);
        if (club == null) return (null, NotFound(Gone));
        if (!await IsAdminAsync(clubId)) return (null, Forbidden(AdminsOnly));
        return (club, null);
    }

    private Task<bool> IsAdminAsync(int clubId) => _admins.AnyAsync(a => a.ClubId == clubId && a.UserId == Me && !a.IsDisabled);

    // ---------------------------------------------------------------------------------------------------------------------
    // Home's Teams tab, making one, the page
    // ---------------------------------------------------------------------------------------------------------------------

    /// <summary>The caller's own teams: ones they run or play for (C-22). Every sport; the apps filter.</summary>
    public async Task<ServiceResult<List<ClubListItemDto>>> ListMineAsync()
    {
        var runs = (await _admins.QueryAsync(q => q.Where(a => a.UserId == Me && !a.IsDisabled).Select(a => a.ClubId), tracking: false)).ToHashSet();
        var plays = (await _players.QueryAsync(q => q.Where(p => p.UserId == Me).Select(p => p.ClubId), tracking: false)).ToHashSet();
        var ids = runs.Concat(plays).Distinct().ToList();
        if (ids.Count == 0) return Success(new List<ClubListItemDto>());

        var clubs = await _clubs.QueryAsync(q => q
            .Where(c => ids.Contains(c.ClubId) && !c.IsDisabled)
            .Select(c => new { c.ClubId, c.Name, c.Sport, c.Area, c.LogoId, Players = c.Players.Count }), tracking: false);
        var entered = await _teams.QueryAsync(q => q
            .Where(t => t.ClubId != null && ids.Contains(t.ClubId.Value))
            .Select(t => new { ClubId = t.ClubId!.Value, t.Tournament!.Status }), tracking: false);

        return Success(clubs
            .Select(c => new ClubListItemDto
            {
                ClubId = c.ClubId,
                Name = c.Name,
                Sport = c.Sport,
                Area = c.Area,
                LogoUrl = _pictureLinks.For(c.LogoId),
                IsAdmin = runs.Contains(c.ClubId),
                IsPlayer = plays.Contains(c.ClubId),
                Players = c.Players,
                Tournaments = entered.Count(e => e.ClubId == c.ClubId),
                Live = entered.Count(e => e.ClubId == c.ClubId && e.Status == TournamentStatus.Live)
            })
            .OrderByDescending(c => c.Live > 0).ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList());
    }

    public async Task<ServiceResult<ClubDto>> CreateAsync(CreateClubRequest request)
    {
        var (name, area, problem) = Describe(request.Name, request.Area);
        if (problem != null) return BadRequest(problem);
        if (!Enum.IsDefined(request.Sport)) return BadRequest("Choose football or cricket. A team plays one sport.");

        var me = await _users.FirstOrDefaultAsync(u => u.UserId == Me && u.DeletedOn == null, tracking: false);
        if (me == null) return NotFound("Account not found.");

        var club = new Club { Name = name!, Area = area, Sport = request.Sport, JoinCode = await NewJoinCodeAsync() };
        club.Created(Now, Me);
        var admin = new ClubAdmin { Club = club, UserId = Me };
        admin.Created(Now, Me);
        club.Admins.Add(admin);
        if (request.IPlay)
        {
            var place = new ClubPlayer { Club = club };
            place.Created(Now, Me);
            await LinkAsync(place, me, club.Sport);
            club.Players.Add(place);
        }
        _clubs.Add(club);
        await _unitOfWork.SaveChangesAsync();
        return await GetAsync(club.ClubId);
    }

    public async Task<ServiceResult<ClubDto>> GetAsync(int clubId)
    {
        var club = await _clubs.FirstOrDefaultAsync(c => c.ClubId == clubId && !c.IsDisabled, tracking: false);
        if (club == null) return NotFound(Gone);
        club.PublicCode ??= await GivePublicCodeAsync(clubId);

        var admins = await _admins.QueryAsync(q => q
            .Where(a => a.ClubId == clubId && !a.IsDisabled)
            .OrderBy(a => a.Crd).ThenBy(a => a.ClubAdminId)
            .Select(a => new { a.UserId, a.User!.Name }), tracking: false);
        var isAdmin = admins.Any(a => a.UserId == Me);
        var players = await _players.ListAsync(p => p.ClubId == clubId, tracking: false);
        var photos = await PhotosAsync(players.Where(p => p.UserId != null).Select(p => p.UserId!.Value).ToList());
        var adminIds = admins.Select(a => a.UserId).ToHashSet();

        var dto = new ClubDto
        {
            ClubId = club.ClubId,
            Name = club.Name,
            Sport = club.Sport,
            Area = club.Area,
            LogoUrl = _pictureLinks.For(club.LogoId),
            JoinCode = isAdmin ? club.JoinCode : null,
            Invite = isAdmin ? InviteLinks.ForClub(_public, club.JoinCode, club.Name) : null,
            CanManage = isAdmin,
            MyClubPlayerId = players.FirstOrDefault(p => p.UserId == Me)?.ClubPlayerId,
            Share = ShareOf(club.PublicCode),
            Admins = admins.Select(a => new ClubAdminDto
            {
                UserId = a.UserId,
                Name = a.Name,
                IsMe = a.UserId == Me,
                CanRemove = (isAdmin || a.UserId == Me) && admins.Count > 1
            }).ToList(),
            Players = players
                .OrderByDescending(p => p.IsCaptain).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(p => new ClubPlayerDto
                {
                    ClubPlayerId = p.ClubPlayerId,
                    Name = p.Name,
                    JerseyNumber = p.JerseyNumber,
                    Position = p.Position,
                    IsCaptain = p.IsCaptain,
                    IsLinked = p.UserId != null,
                    IsWaiting = p.UserId == null && (p.EmailId != null || p.PhoneNumber != null),
                    IsMe = p.UserId == Me,
                    IsAdmin = p.UserId is { } u && adminIds.Contains(u),
                    Cricket = club.Sport == Sport.Cricket ? p.Cricket : null,
                    PhotoUrl = p.UserId is { } id ? _pictureLinks.For(photos.GetValueOrDefault(id)) : null,
                    Email = isAdmin ? p.EmailId : null,
                    Phone = isAdmin ? p.PhoneNumber : null
                }).ToList()
        };
        await AddTournamentsAsync(dto, isAdmin);
        return Success(dto);
    }

    /// <summary>
    /// Every tournament the club played in, from its squads (teams with this ClubId), each with its own result, grouped
    /// being played / still to play / played (C-12). Totals come from those results; nothing new is scored.
    /// </summary>
    private async Task AddTournamentsAsync(ClubDto dto, bool isAdmin)
    {
        if (isAdmin) await AddWaitingEntriesAsync(dto);
        var squads = await _teams.QueryAsync(q => q
            .Where(t => t.ClubId == dto.ClubId)
            .Select(t => new
            {
                TeamId = t.Id,
                TeamName = t.Name,
                t.TournamentId,
                t.Tournament!.Name,
                t.Tournament.Sport,
                t.Tournament.Status,
                t.Tournament.StartDate,
                t.Tournament.EndDate,
                t.Tournament.LogoId,
                t.Tournament.CreatedByUserId,
                Squad = t.Players.Count(p => p.LeftOn == null)
            }), tracking: false);
        if (squads.Count == 0) return;

        var teamIds = squads.Select(s => s.TeamId).ToList();
        var tournamentIds = squads.Select(s => s.TournamentId).Distinct().ToList();

        // Results: matches over, and who won them (the knockout rules' own reading of a result).
        var results = new List<(int TeamId, bool Won)>();
        foreach (var m in await _matches.ListAsync(m => tournamentIds.Contains(m.TournamentId) && m.Status == MatchStatus.Completed
                     && ((m.HomeTeamId != null && teamIds.Contains(m.HomeTeamId.Value)) || (m.AwayTeamId != null && teamIds.Contains(m.AwayTeamId.Value))), tracking: false))
        {
            var winner = KnockoutResults.WinnerOf(m);
            foreach (var side in new[] { m.HomeTeamId, m.AwayTeamId })
                if (side is { } id && teamIds.Contains(id)) results.Add((id, winner == id));
        }
        foreach (var m in await _cricketMatches.ListAsync(m => tournamentIds.Contains(m.TournamentId)
                     && (m.Status == CricketMatchStatus.Completed || m.Status == CricketMatchStatus.Abandoned)
                     && ((m.HomeTeamId != null && teamIds.Contains(m.HomeTeamId.Value)) || (m.AwayTeamId != null && teamIds.Contains(m.AwayTeamId.Value))), tracking: false))
        {
            var winner = KnockoutResults.WinnerOf(m);
            foreach (var side in new[] { m.HomeTeamId, m.AwayTeamId })
                if (side is { } id && teamIds.Contains(id)) results.Add((id, winner == id));
        }

        var open = await OpenableAsync(tournamentIds, isAdmin);
        foreach (var s in squads)
        {
            var played = results.Count(r => r.TeamId == s.TeamId);
            var won = results.Count(r => r.TeamId == s.TeamId && r.Won);
            var champion = s.Status == TournamentStatus.Completed
                           && (s.Sport == Sport.Cricket ? await _cricketStats.ChampionAsync(s.TournamentId) : await _footballStats.ChampionAsync(s.TournamentId)) == s.TeamId;
            var section = s.Status switch
            {
                TournamentStatus.Live => "live",
                TournamentStatus.Upcoming => "upcoming",
                _ => "past"
            };
            dto.Tournaments.Add(new ClubTournamentDto
            {
                TournamentId = s.TournamentId,
                Name = s.Name,
                Sport = s.Sport,
                Status = s.Status,
                StartDate = s.StartDate,
                EndDate = s.EndDate,
                LogoUrl = _pictureLinks.For(s.LogoId),
                TeamId = s.TeamId,
                TeamName = s.TeamName,
                Section = section,
                Played = played,
                Won = won,
                Champion = champion,
                Squad = s.Squad,
                Line = Line(s.Status, s.StartDate, played, won, champion, s.Squad),
                CanOpen = open.Contains(s.TournamentId)
            });
        }

        dto.Tournaments = dto.Tournaments
            .OrderBy(t => t.Section switch { "live" => 0, "upcoming" => 1, _ => 2 })
            .ThenByDescending(t => t.StartDate ?? DateOnly.MaxValue)
            .ThenByDescending(t => t.TournamentId)
            .ToList();
        dto.Totals = new ClubTotalsDto
        {
            Tournaments = dto.Tournaments.Count(t => t.EntryId == null),
            Matches = dto.Tournaments.Sum(t => t.Played),
            Won = dto.Tournaments.Sum(t => t.Won),
            Titles = dto.Tournaments.Count(t => t.Champion)
        };
    }

    /// <summary>"Champions · 5 played, 5 won", "Played 4 · won 3", "Starts 12 Dec · 14 in the squad".</summary>
    private static string Line(TournamentStatus status, DateOnly? start, int played, int won, bool champion, int squad)
    {
        var record = played == 0 ? "No matches played" : $"{played} played, {won} won";
        return status switch
        {
            TournamentStatus.Live => played == 0 ? "No matches played yet" : $"Played {played} · won {won}",
            TournamentStatus.Upcoming => start is { } d
                ? $"Starts {d.ToString("d MMM", CultureInfo.InvariantCulture)} · {squad} in the squad"
                : $"{squad} in the squad",
            TournamentStatus.Cancelled => "Cancelled",
            _ => champion ? $"Champions · {record}" : record
        };
    }

    /// <summary>Which of these tournaments the caller may open: theirs to own, score, play in or run a team in. A club's admins run its squads.</summary>
    private async Task<HashSet<int>> OpenableAsync(List<int> tournamentIds, bool isAdmin)
    {
        if (isAdmin) return tournamentIds.ToHashSet();
        var open = (await _tournaments.QueryAsync(q => q.Where(t => tournamentIds.Contains(t.Id) && t.CreatedByUserId == Me).Select(t => t.Id), tracking: false)).ToHashSet();
        open.UnionWith(await _members.QueryAsync(q => q.Where(m => m.UserId == Me && !m.IsDisabled && tournamentIds.Contains(m.TournamentId)).Select(m => m.TournamentId), tracking: false));
        open.UnionWith(await _squads.QueryAsync(q => q.Where(p => p.UserId == Me && tournamentIds.Contains(p.TournamentId)).Select(p => p.TournamentId), tracking: false));
        open.UnionWith(await _teamAdmins.QueryAsync(q => q.Where(a => a.UserId == Me && !a.IsDisabled && tournamentIds.Contains(a.TournamentId)).Select(a => a.TournamentId), tracking: false));
        return open;
    }

    public async Task<ServiceResult<ClubDto>> UpdateAsync(int clubId, UpdateClubRequest request)
    {
        var (club, error) = await ManagedAsync(clubId);
        if (error is { } e) return e;

        var (name, area, problem) = Describe(request.Name, request.Area);
        if (problem != null) return BadRequest(problem);
        club!.Name = name!;
        club.Area = area;

        if (request.SetCaptain)
        {
            var players = await _players.ListAsync(p => p.ClubId == clubId);
            if (request.CaptainClubPlayerId is { } captain && players.All(p => p.ClubPlayerId != captain))
                return BadRequest("The captain must be one of this team's players.");
            foreach (var p in players) p.IsCaptain = p.ClubPlayerId == request.CaptainClubPlayerId;
        }
        club.Modified(Now, Me);
        await _unitOfWork.SaveChangesAsync();
        return await GetAsync(clubId);
    }

    /// <summary>
    /// Deletes the club only. Its tournament squads stay as ordinary teams with their history; they just lose the link
    /// (plan section 5).
    /// </summary>
    public async Task<ServiceResult<bool>> DeleteAsync(int clubId)
    {
        var (club, error) = await ManagedAsync(clubId);
        if (error is { } e) return e;

        await UnlinkSquadsAsync(clubId, null);
        foreach (var entry in await _entries.ListAsync(x => x.ClubId == clubId)) _entries.Remove(entry); // their ticks go with them
        var logo = club!.LogoId;
        _clubs.Remove(club); // its admins and players go with it
        await _unitOfWork.SaveChangesAsync();
        _pictureStore.Delete(logo);
        return Success(true);
    }

    /// <summary>Tournament rows that point at the club (or one club player): the links are NoAction, so they're cleared first.</summary>
    private async Task UnlinkSquadsAsync(int clubId, int? clubPlayerId)
    {
        if (clubPlayerId == null)
        {
            foreach (var team in await _teams.ListAsync(t => t.ClubId == clubId)) team.ClubId = null;
            var ids = await _players.QueryAsync(q => q.Where(p => p.ClubId == clubId).Select(p => p.ClubPlayerId), tracking: false);
            foreach (var place in await _squads.ListAsync(p => p.ClubPlayerId != null && ids.Contains(p.ClubPlayerId.Value))) place.ClubPlayerId = null;
        }
        else
        {
            foreach (var place in await _squads.ListAsync(p => p.ClubPlayerId == clubPlayerId)) place.ClubPlayerId = null;
        }
    }

    public async Task<ServiceResult<ClubDto>> ResetJoinCodeAsync(int clubId)
    {
        var (club, error) = await ManagedAsync(clubId);
        if (error is { } e) return e;
        club!.JoinCode = await NewJoinCodeAsync();
        club.Modified(Now, Me);
        await _unitOfWork.SaveChangesAsync();
        return await GetAsync(clubId);
    }

    // ---------------------------------------------------------------------------------------------------------------------
    // Players
    // ---------------------------------------------------------------------------------------------------------------------

    /// <summary>Who an email or number belongs to, as for a squad place (<see cref="PlayerLookups"/> limits it per person).</summary>
    public async Task<ServiceResult<ClubPlayerLookupDto>> LookupPlayerAsync(int clubId, PlayerLookupRequest request)
    {
        var (_, error) = await ManagedAsync(clubId);
        if (error is { } e) return e;

        var (contact, problem) = Contacts.Read(request.Contact, _sms.IsEnabled);
        if (contact is not { } found) return BadRequest(problem!);
        if (_lookups.TryLookup(Me) is { } wait)
            return TooManyRequests($"That's a lot of lookups. Try again in {Math.Max(1, (int)Math.Ceiling(wait.TotalMinutes))} minutes, or add the player by name.");

        var user = await _links.AccountWithAsync(found);
        var value = found.Value;
        var alreadyIn = user != null && await _players.AnyAsync(p => p.ClubId == clubId && p.UserId == user.UserId)
                        || (found.IsEmail
                            ? await _players.AnyAsync(p => p.ClubId == clubId && p.EmailId == value)
                            : await _players.AnyAsync(p => p.ClubId == clubId && p.PhoneNumber == value));
        return Success(new ClubPlayerLookupDto
        {
            Kind = found.Kind,
            Contact = found.Value,
            Display = Contacts.Display(found.Value),
            Found = user != null,
            Name = user?.Name,
            AlreadyIn = alreadyIn
        });
    }

    public async Task<ServiceResult<ClubDto>> AddPlayerAsync(int clubId, ClubPlayerRequest request)
    {
        var (club, error) = await ManagedAsync(clubId);
        if (error is { } e) return e;

        var name = Squash(request.Name);
        if (club!.Sport == Sport.Cricket && CricketProfiles.Problem(request.Cricket) is { } cricketError) return BadRequest(cricketError);

        var place = new ClubPlayer
        {
            ClubId = clubId,
            Name = name,
            JerseyNumber = request.JerseyNumber,
            Position = club.Sport == Sport.Football ? Squash(request.Position) is { Length: > 0 } pos ? pos : null : null,
            Cricket = club.Sport == Sport.Cricket && request.Cricket != null ? ClubPlayerCricket.From(request.Cricket) : null
        };
        place.Created(Now, Me);
        if (await SetContactsAsync(place, club, request.Email, request.Phone) is { } contactError) return contactError;
        if (string.IsNullOrWhiteSpace(place.Name))
            return BadRequest(!string.IsNullOrWhiteSpace(request.Email) || !string.IsNullOrWhiteSpace(request.Phone)
                ? "No account uses that yet, so add the player's name. They'll be linked when they sign up."
                : "Player name is required.");

        _players.Add(place);
        if (await SaveAsync() is { } race) return race;

        // Upcoming squads and waiting entries take them now; live tournaments are asked about (C-5, C-6).
        var question = await _squadSync.AfterAddedAsync(club, place);
        if (await SaveAsync() is { } race2) return race2;
        return await WithQuestionAsync(clubId, question, place.ClubPlayerId);
    }

    public async Task<ServiceResult<ClubDto>> UpdatePlayerAsync(int clubId, int clubPlayerId, ClubPlayerRequest request)
    {
        var (club, error) = await ManagedAsync(clubId);
        if (error is { } e) return e;

        var place = await _players.FirstOrDefaultAsync(p => p.ClubPlayerId == clubPlayerId && p.ClubId == clubId);
        if (place == null) return NotFound("That player isn't in this team any more.");
        if (club!.Sport == Sport.Cricket && CricketProfiles.Problem(request.Cricket) is { } cricketError) return BadRequest(cricketError);

        if (await SetContactsAsync(place, club, request.Email, request.Phone) is { } contactError) return contactError;
        place.JerseyNumber = request.JerseyNumber;
        if (club.Sport == Sport.Football) place.Position = Squash(request.Position) is { Length: > 0 } pos ? pos : null;

        // A linked player's name and cricket profile are theirs, kept in step with their account.
        if (!place.IsLinked)
        {
            var name = Squash(request.Name);
            if (name.Length == 0) return BadRequest("Player name is required.");
            place.Name = name;
            if (club.Sport == Sport.Cricket && request.Cricket != null)
            {
                if (place.Cricket == null) place.Cricket = ClubPlayerCricket.From(request.Cricket);
                else CricketProfiles.Copy(request.Cricket, place.Cricket);
            }
        }
        place.Modified(Now, Me);
        await _squadSync.FollowDetailsAsync(club, place); // upcoming and live squads follow; past ones keep theirs
        if (await SaveAsync() is { } race) return race;
        return await GetAsync(clubId);
    }

    /// <summary>
    /// An admin takes a player off the team, or a player leaves it themselves ("Leave team"). Past squads keep them;
    /// upcoming squads and waiting entries drop them now (C-20). For live ones the admin is asked in one sheet; a player
    /// leaving by themselves stays in live squads until an admin or the organiser leaves them out. History never changes.
    /// </summary>
    public async Task<ServiceResult<ClubDto>> RemovePlayerAsync(int clubId, int clubPlayerId)
    {
        var place = await _players.FirstOrDefaultAsync(p => p.ClubPlayerId == clubPlayerId && p.ClubId == clubId);
        if (place == null) return NotFound("That player isn't in this team any more.");
        var isAdmin = await IsAdminAsync(clubId);
        if (place.UserId != Me && !isAdmin) return Forbidden(AdminsOnly);
        var club = (await _clubs.FirstOrDefaultAsync(c => c.ClubId == clubId, tracking: false))!;

        var question = await _squadSync.BeforeRemovedAsync(club, place);
        await UnlinkSquadsAsync(clubId, clubPlayerId);
        _players.Remove(place);
        await _unitOfWork.SaveChangesAsync();
        return await WithQuestionAsync(clubId, isAdmin ? question : null, null);
    }

    /// <summary>The club as the caller sees it, with the live-tournaments question when there is one.</summary>
    private async Task<ServiceResult<ClubDto>> WithQuestionAsync(int clubId, ClubSquadQuestionDto? question, int? clubPlayerId)
    {
        var answer = await GetAsync(clubId);
        if (question != null && answer.Value is { } dto)
        {
            question.ClubPlayerId = clubPlayerId;
            dto.Question = question;
        }
        return answer;
    }

    public async Task<ServiceResult<ClubSquadQuestionDto>> SquadsPreviewAsync(int clubId, int clubPlayerId)
    {
        var (_, error) = await ManagedAsync(clubId);
        if (error is { } e) return e;
        var place = await _players.FirstOrDefaultAsync(p => p.ClubPlayerId == clubPlayerId && p.ClubId == clubId, tracking: false);
        if (place == null) return NotFound("That player isn't in this team any more.");

        var question = new ClubSquadQuestionDto { Change = "add", ClubPlayerId = clubPlayerId, PlayerName = place.Name };
        foreach (var squad in (await _squadSync.OpenSquadsAsync(clubId)).Where(s => s.Status == TournamentStatus.Live))
        {
            question.Live.Add(new ClubSquadChoiceDto
            {
                TournamentId = squad.TournamentId,
                TournamentName = squad.TournamentName,
                TeamId = squad.TeamId,
                TeamName = squad.TeamName,
                Reason = (await _squadSync.ReasonsAsync(squad.TournamentId, [place], squad.TeamId)).GetValueOrDefault(place.ClubPlayerId)
            });
        }
        return Success(question);
    }

    public async Task<ServiceResult<ClubDto>> SquadsApplyAsync(int clubId, ClubSquadsApplyRequest request)
    {
        var (club, error) = await ManagedAsync(clubId);
        if (error is { } e) return e;

        if (request.TeamIds.Count > 0)
        {
            var place = request.ClubPlayerId is { } id
                ? await _players.FirstOrDefaultAsync(p => p.ClubPlayerId == id && p.ClubId == clubId, tracking: false)
                : null;
            if (place == null) return NotFound("That player isn't in this team any more.");
            var wanted = request.TeamIds.Distinct().ToList();
            var squads = (await _squadSync.OpenSquadsAsync(clubId)).Where(s => wanted.Contains(s.TeamId)).ToList();
            if (squads.Count != wanted.Count)
                return Conflict("One of those tournaments has finished, or the team isn't in it any more. Reload the team.");
            foreach (var squad in squads)
            {
                if ((await _squadSync.ReasonsAsync(squad.TournamentId, [place], squad.TeamId)).TryGetValue(place.ClubPlayerId, out var reason))
                    return Conflict($"{place.Name} can't join {squad.TeamName} in {squad.TournamentName}: {reason.ToLowerInvariant()}.");
                _squadSync.NewPlace(place, squad.TeamId, squad.TournamentId, club!.Sport);
            }
        }

        if (request.PlayerIds.Count > 0)
        {
            var wanted = request.PlayerIds.Distinct().ToList();
            var places = await _squads.ListAsync(p => wanted.Contains(p.Id));
            var teamIds = places.Select(p => p.TeamId).Distinct().ToList();
            var squadTeams = await _teams.ListAsync(t => teamIds.Contains(t.Id) && t.ClubId == clubId
                && t.Tournament!.Status != TournamentStatus.Completed && t.Tournament.Status != TournamentStatus.Cancelled);
            if (places.Count != wanted.Count || places.Any(p => squadTeams.All(t => t.Id != p.TeamId)))
                return Conflict("One of those tournaments has finished, or the place isn't in this team's squad any more. Reload the team.");
            foreach (var place in places) await _squadSync.LeaveOutAsync(place, squadTeams.First(t => t.Id == place.TeamId));
        }

        if (await SaveAsync() is { } race) return race;
        return await GetAsync(clubId);
    }

    /// <summary>
    /// A club player's email and number, as on a squad place (<see cref="IPlayerLinkService.SetContactsAsync"/>): null leaves
    /// one alone, "" clears it; an account behind either links the player now and takes its name. Stages; the caller saves.
    /// </summary>
    private async Task<ServiceError?> SetContactsAsync(ClubPlayer place, Club club, string? email, string? phone)
    {
        if (email == null && phone == null) return null;

        var newEmail = place.EmailId;
        if (email != null)
        {
            newEmail = string.IsNullOrWhiteSpace(email) ? null : AccountService.NormaliseEmail(email);
            if (newEmail == null && !string.IsNullOrWhiteSpace(email)) return BadRequest("Enter a valid email address for the player, or leave it empty.");
        }
        var newPhone = place.PhoneNumber;
        if (phone != null)
        {
            newPhone = string.IsNullOrWhiteSpace(phone) ? null : Contacts.NormalisePhone(phone);
            if (newPhone == null && !string.IsNullOrWhiteSpace(phone)) return BadRequest("Enter a valid mobile number for the player, or leave it empty.");
        }
        if (newEmail == place.EmailId && newPhone == place.PhoneNumber) return null;

        var byEmail = newEmail == null ? null : await _users.FirstOrDefaultAsync(u => u.EmailId == newEmail && u.DeletedOn == null && !u.IsDisabled);
        var byPhone = newPhone == null ? null : await _users.FirstOrDefaultAsync(u => u.PhoneNumber == newPhone && u.DeletedOn == null && !u.IsDisabled);
        if (byEmail != null && byPhone != null && byEmail.UserId != byPhone.UserId)
            return Conflict("That email and mobile number belong to two different people. Give one of them.");
        var user = byEmail ?? byPhone;

        if (user != null && user.UserId == place.UserId)
        {
            place.EmailId = user.EmailId;
            place.PhoneNumber = newPhone;
            return null;
        }
        var otherId = place.ClubPlayerId;
        if (user != null && await _players.AnyAsync(p => p.ClubId == club.ClubId && p.UserId == user.UserId && p.ClubPlayerId != otherId))
            return Conflict($"{user.Name} is already in {club.Name}.");
        if (user == null && newEmail != null && await _players.AnyAsync(p => p.ClubId == club.ClubId && p.EmailId == newEmail && p.ClubPlayerId != otherId))
            return Conflict("Another player in this team already has that email.");
        if (user == null && newPhone != null && await _players.AnyAsync(p => p.ClubId == club.ClubId && p.PhoneNumber == newPhone && p.ClubPlayerId != otherId))
            return Conflict("Another player in this team already has that number.");

        place.UserId = null;
        place.User = null;
        place.EmailId = newEmail;
        place.PhoneNumber = newPhone;
        if (user != null)
        {
            await LinkAsync(place, user, club.Sport);
            if (user.UserId != Me)
                await _notifier.NotifyAsync(user.UserId, NotificationTemplateCode.AddedToClub,
                    Notifier.With(("ActorName", ActorName), ("TeamName", club.Name)), NotificationLink.ToClub(club.ClubId));
        }
        return null;
    }

    /// <summary>Links a club player to an account: its name, email and (cricket) the person's own profile, as a squad place.</summary>
    private async Task LinkAsync(ClubPlayer place, User user, Sport sport)
    {
        place.UserId = user.UserId;
        place.EmailId = user.EmailId;
        place.Name = user.Name;
        if (sport != Sport.Cricket) return;
        if (await _profiles.GetByIdAsync(user.UserId) is { } mine)
        {
            if (place.Cricket == null) place.Cricket = ClubPlayerCricket.From(mine);
            else CricketProfiles.Copy(mine, place.Cricket);
        }
    }

    // ---------------------------------------------------------------------------------------------------------------------
    // Admins
    // ---------------------------------------------------------------------------------------------------------------------

    /// <summary>One of its players on the app, or anyone with an account by email or number. At most <see cref="ClubAdmin.MaxPerClub"/>.</summary>
    public async Task<ServiceResult<ClubDto>> AddAdminAsync(int clubId, AddClubAdminRequest request)
    {
        var (club, error) = await ManagedAsync(clubId);
        if (error is { } e) return e;

        User? user;
        if (request.ClubPlayerId is { } playerId)
        {
            var place = await _players.FirstOrDefaultAsync(p => p.ClubPlayerId == playerId && p.ClubId == clubId, tracking: false);
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

        var admins = await _admins.ListAsync(a => a.ClubId == clubId);
        if (admins.Any(a => a.UserId == user.UserId)) return Conflict($"{user.Name} already runs {club!.Name}.");
        if (admins.Count >= ClubAdmin.MaxPerClub) return Conflict($"A team can have {ClubAdmin.MaxPerClub} admins. Remove one first.");

        var admin = new ClubAdmin { ClubId = clubId, UserId = user.UserId };
        admin.Created(Now, Me);
        _admins.Add(admin);
        if (user.UserId != Me)
            await _notifier.NotifyAsync(user.UserId, NotificationTemplateCode.MadeClubAdmin,
                Notifier.With(("ActorName", ActorName), ("TeamName", club!.Name)), NotificationLink.ToClub(clubId));

        try
        {
            await _unitOfWork.SaveChangesAsync();
        }
        catch (DuplicateEntryException)
        {
            return Conflict($"{user.Name} was added a moment ago. Reload the team.");
        }
        return await GetAsync(clubId);
    }

    /// <summary>An admin takes another off, or steps down. A team always keeps one admin.</summary>
    public async Task<ServiceResult<ClubDto>> RemoveAdminAsync(int clubId, int userId)
    {
        if (!await _clubs.AnyAsync(c => c.ClubId == clubId && !c.IsDisabled)) return NotFound(Gone);
        if (!await IsAdminAsync(clubId)) return Forbidden(AdminsOnly);

        var admins = await _admins.ListAsync(a => a.ClubId == clubId);
        var admin = admins.FirstOrDefault(a => a.UserId == userId);
        if (admin == null) return NotFound("They don't run this team.");
        if (admins.Count == 1)
            return Conflict(userId == Me
                ? "A team needs an admin. Make someone else an admin before you step down, or delete the team."
                : "A team needs an admin.");

        _admins.Remove(admin);
        await _unitOfWork.SaveChangesAsync();
        return await GetAsync(clubId);
    }

    // ---------------------------------------------------------------------------------------------------------------------
    // Joining with the code
    // ---------------------------------------------------------------------------------------------------------------------

    public async Task<ServiceResult<ClubJoinPreviewDto>> JoinPreviewAsync(JoinTeamPreviewRequest request)
    {
        var (club, error) = await FindByCodeAsync(request.Code);
        if (error is { } e) return e;

        var places = await _players.ListAsync(p => p.ClubId == club!.ClubId && p.UserId == null && p.EmailId == null && p.PhoneNumber == null, tracking: false);
        return Success(new ClubJoinPreviewDto
        {
            ClubId = club!.ClubId,
            Name = club.Name,
            Sport = club.Sport,
            Area = club.Area,
            LogoUrl = _pictureLinks.For(club.LogoId),
            Players = await _players.CountAsync(p => p.ClubId == club.ClubId),
            Places = places
                .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(p => p.JerseyNumber)
                .Select(p => new ClubJoinPlaceDto { ClubPlayerId = p.ClubPlayerId, Name = p.Name, Detail = Detail(p, club.Sport) })
                .ToList()
        });
    }

    /// <summary>Instant, like a team's code: claim a name an admin added, or join as new. Upcoming and live squads are step 4.</summary>
    public async Task<ServiceResult<ClubJoinResult>> JoinAsync(ClubJoinRequest request)
    {
        var (club, error) = await FindByCodeAsync(request.Code, request.Link);
        if (error is { } e) return e;
        var user = await _users.FirstOrDefaultAsync(u => u.UserId == Me && u.DeletedOn == null);
        if (user == null) return NotFound("Account not found.");

        // An invite link opened again by someone already in: nothing to do, the apps just open the team.
        if (request.Link && await _players.AnyAsync(p => p.ClubId == club!.ClubId && p.UserId == Me))
            return Success(new ClubJoinResult { ClubId = club!.ClubId, Name = club.Name, Sport = club.Sport, AlreadyIn = true });

        ClubPlayer place;
        // A link joins straight in as new (the user's choice, 2026-11-23): an admin removes a duplicate name by hand.
        if (request.ClubPlayerId is { } id && !request.Link)
        {
            var picked = await _players.FirstOrDefaultAsync(p => p.ClubPlayerId == id && p.ClubId == club!.ClubId);
            if (picked == null) return NotFound("That name isn't in this team any more. Pick again.");
            if (picked.UserId != null || picked.EmailId != null || picked.PhoneNumber != null) return Conflict("Someone else has just claimed that name. Pick again.");
            place = picked;
            place.Modified(Now, Me);
        }
        else
        {
            place = new ClubPlayer { ClubId = club!.ClubId };
            place.Created(Now, Me);
            _players.Add(place);
        }
        await LinkAsync(place, user, club!.Sport);
        try
        {
            await _unitOfWork.SaveChangesAsync();
        }
        catch (DuplicateEntryException)
        {
            return Conflict($"You've just joined {club.Name}.");
        }

        // Upcoming squads take them now; the admins hear of it and choose about live ones (C-5, plan 8.2).
        await _squadSync.AfterAddedAsync(club, place);
        foreach (var admin in await _admins.QueryAsync(q => q.Where(a => a.ClubId == club.ClubId && !a.IsDisabled && a.UserId != Me).Select(a => a.UserId), tracking: false))
            await _notifier.NotifyAsync(admin, NotificationTemplateCode.ClubPlayerJoined,
                Notifier.With(("PlayerName", place.Name), ("TeamName", club.Name)), NotificationLink.ToClubPlayer(club.ClubId, place.ClubPlayerId));
        await _unitOfWork.SaveChangesAsync();
        return Success(new ClubJoinResult { ClubId = club.ClubId, Name = club.Name, Sport = club.Sport });
    }

    /// <summary>The code's club. From a link, a dead code says so in a link's words, and being in it already is left to the caller.</summary>
    private async Task<(Club? Club, ServiceError? Error)> FindByCodeAsync(string? typed, bool link = false)
    {
        if (_attempts.WaitFor(Me) is { } wait)
            return (null, TooManyRequests($"Too many wrong codes. Try again in {Math.Max(1, (int)Math.Ceiling(wait.TotalMinutes))} minutes."));
        var code = JoinCodes.Normalise(typed, JoinCodes.ClubLength);
        if (code == null) return (null, link ? NotFound(InviteLinks.NoLongerWorks) : BadRequest($"A team's code is {JoinCodes.ClubLength} letters and numbers."));

        var club = await _clubs.FirstOrDefaultAsync(c => c.JoinCode == code && !c.IsDisabled, tracking: false);
        if (club == null)
        {
            _attempts.Missed(Me);
            return (null, NotFound(link ? InviteLinks.NoLongerWorks : "No team has that code. Check it with the team's admin."));
        }
        if (!link && await _players.AnyAsync(p => p.ClubId == club.ClubId && p.UserId == Me))
            return (null, Conflict($"You're already in {club.Name}."));
        return (club, null);
    }

    /// <summary>"#7 · Batter" or "#7 · Defender": what tells two players of the same name apart.</summary>
    private static string? Detail(ClubPlayer place, Sport sport)
    {
        var parts = new List<string>();
        if (place.JerseyNumber is { } number) parts.Add($"#{number}");
        var what = sport == Sport.Cricket ? place.Cricket?.RoleLabel : place.Position;
        if (!string.IsNullOrWhiteSpace(what)) parts.Add(what.Trim());
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    // ---------------------------------------------------------------------------------------------------------------------
    // Keeping a tournament's team (C-23)
    // ---------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// "Keep this team for other tournaments": only the team's own admins (named by an owner, step 1), never an organiser
    /// for being organiser. Copies the squad into a new club, makes the caller its first admin and the team's other admins
    /// the next (up to 3), and links the team and its players to it, so this tournament counts in the club's record.
    /// </summary>
    public async Task<ServiceResult<ClubDto>> KeepTeamAsync(int tournamentId, int teamId)
    {
        var team = await _teams.FirstOrDefaultAsync(t => t.Id == teamId && t.TournamentId == tournamentId, TeamQueries.WithPlayersAndProfiles);
        if (team == null) return NotFound("Team not found.");
        var teamAdmins = await _teamAdmins.QueryAsync(q => q
            .Where(a => a.TeamId == teamId && !a.IsDisabled && a.User!.DeletedOn == null && !a.User.IsDisabled)
            .OrderBy(a => a.Crd).ThenBy(a => a.TeamAdminId)
            .Select(a => a.UserId), tracking: false);
        if (!teamAdmins.Contains(Me))
            return Forbidden("Only this team's admins can keep it. The organiser can name you one of its admins first.");
        if (team.ClubId != null) return Conflict($"{team.Name} is already kept. It's on your Teams tab.");

        var sport = await _tournaments.QueryFirstOrDefaultAsync(q => q.Where(t => t.Id == tournamentId).Select(t => t.Sport), tracking: false);
        var club = new Club { Name = Fit(team.Name, Club.NameMax), Sport = sport, JoinCode = await NewJoinCodeAsync() };
        club.Created(Now, Me);
        foreach (var userId in teamAdmins.Where(id => id != Me).Prepend(Me).Take(ClubAdmin.MaxPerClub))
        {
            var admin = new ClubAdmin { Club = club, UserId = userId };
            admin.Created(Now, Me);
            club.Admins.Add(admin);
        }

        foreach (var player in team.Players.Where(p => p.LeftOn == null))
        {
            var place = new ClubPlayer
            {
                Club = club,
                Name = player.Name,
                JerseyNumber = player.JerseyNumber,
                Position = player.Position,
                IsCaptain = team.DefaultCaptainPlayerId == player.Id,
                UserId = player.UserId,
                EmailId = player.EmailId,
                PhoneNumber = player.PhoneNumber,
                Cricket = sport == Sport.Cricket && player.Cricket != null ? ClubPlayerCricket.From(player.Cricket) : null
            };
            place.Created(Now, Me);
            club.Players.Add(place);
            player.ClubPlayer = place;
        }
        team.Club = club;
        _clubs.Add(club);
        await _unitOfWork.SaveChangesAsync();

        // Then the news, now the club has an id to open: its other admins, and the players on the app.
        var admins = club.Admins.Select(a => a.UserId).ToHashSet();
        foreach (var userId in admins.Where(id => id != Me))
            await _notifier.NotifyAsync(userId, NotificationTemplateCode.MadeClubAdmin,
                Notifier.With(("ActorName", ActorName), ("TeamName", club.Name)), NotificationLink.ToClub(club.ClubId));
        foreach (var userId in club.Players.Where(p => p.UserId is { } u && !admins.Contains(u)).Select(p => p.UserId!.Value))
            await _notifier.NotifyAsync(userId, NotificationTemplateCode.AddedToClub,
                Notifier.With(("ActorName", ActorName), ("TeamName", club.Name)), NotificationLink.ToClub(club.ClubId));
        await _unitOfWork.SaveChangesAsync();
        return await GetAsync(club.ClubId);
    }

    // ---------------------------------------------------------------------------------------------------------------------
    // Waiting entries, the public page
    // ---------------------------------------------------------------------------------------------------------------------

    /// <summary>Entries still waiting for an organiser, shown to the club's admins under "Still to play" (step 3).</summary>
    private async Task AddWaitingEntriesAsync(ClubDto dto)
    {
        var waiting = await _entries.QueryAsync(q => q
            .Where(e => e.ClubId == dto.ClubId && e.Status == EntryStatus.Waiting)
            .Select(e => new { e.TournamentEntryId, e.TournamentId, e.TeamName, Picked = e.Players.Count }), tracking: false);
        if (waiting.Count == 0) return;
        var ids = waiting.Select(e => e.TournamentId).ToList();
        var rows = (await _tournaments.QueryAsync(q => q.Where(t => ids.Contains(t.Id))
            .Select(t => new { t.Id, t.Name, t.Sport, t.Status, t.StartDate, t.EndDate, t.LogoId }), tracking: false)).ToDictionary(t => t.Id);
        foreach (var entry in waiting)
        {
            if (!rows.TryGetValue(entry.TournamentId, out var t)) continue;
            dto.Tournaments.Add(new ClubTournamentDto
            {
                TournamentId = t.Id,
                Name = t.Name,
                Sport = t.Sport,
                Status = t.Status,
                StartDate = t.StartDate,
                EndDate = t.EndDate,
                LogoUrl = _pictureLinks.For(t.LogoId),
                TeamName = entry.TeamName,
                Section = "upcoming",
                Squad = entry.Picked,
                Line = $"Waiting for the organiser · {entry.Picked} picked",
                EntryId = entry.TournamentEntryId
            });
        }
    }

    private PublicShareDto ShareOf(string? code)
    {
        var site = _public.WebAppUrl.Trim().TrimEnd('/');
        return new PublicShareDto { Code = code ?? "", Url = site.Length == 0 || code == null ? null : $"{site}/team/{code}" };
    }

    /// <summary>The public page's code, made the first time the page is opened (clubs from step 2 had none) and kept for good.</summary>
    private async Task<string> GivePublicCodeAsync(int clubId)
    {
        var club = (await _clubs.FirstOrDefaultAsync(c => c.ClubId == clubId))!;
        if (club.PublicCode != null) return club.PublicCode;
        string code;
        do code = JoinCodes.New(JoinCodes.ClubLength);
        while (await _clubs.AnyAsync(c => c.PublicCode == code));
        club.PublicCode = code;
        await _unitOfWork.SaveChangesAsync();
        return code;
    }

    /// <summary>
    /// The signed-out page (C-15, plan 8.3): name, sport, area, logo, who runs it, its tournaments and results, its players'
    /// names and roles. Never emails, numbers, codes or photos. A wrong code reads like a missing page.
    /// </summary>
    public async Task<ServiceResult<JsonNode>> PublicAsync(string code)
    {
        var typed = JoinCodes.Normalise(code, JoinCodes.ClubLength);
        var club = typed == null ? null : await _clubs.FirstOrDefaultAsync(c => c.PublicCode == typed && !c.IsDisabled, tracking: false);
        if (club == null) return NotFound("This team's page isn't here. Check the link with whoever sent it.");

        var page = (await GetAsyncAs(club.ClubId))!;
        var ids = page.Tournaments.Select(x => x.TournamentId).ToList();
        var live = (await _tournaments.QueryAsync(q => q
                .Where(t => t.IsPublic && t.PublicCode != null && ids.Contains(t.Id))
                .Select(t => new { t.Id, t.PublicCode }), tracking: false))
            .ToDictionary(t => t.Id, t => t.PublicCode);
        var dto = new PublicClubDto
        {
            Name = page.Name,
            Sport = page.Sport,
            Area = page.Area,
            LogoUrl = page.LogoUrl,
            RunBy = page.Admins.Select(a => a.Name).ToList(),
            Totals = page.Totals,
            Tournaments = page.Tournaments.Select(t => new PublicClubTournamentDto
            {
                Name = t.Name,
                Status = t.Status,
                StartDate = t.StartDate,
                EndDate = t.EndDate,
                TeamName = t.TeamName,
                Section = t.Section,
                Line = t.Line,
                Champion = t.Champion,
                LiveCode = live.GetValueOrDefault(t.TournamentId)
            }).ToList(),
            Players = page.Players.Select(p => new PublicClubPlayerDto
            {
                Name = p.Name,
                Detail = ClubEntryService.Detail(new ClubPlayer { JerseyNumber = p.JerseyNumber, Position = p.Position, Cricket = p.Cricket }, page.Sport),
                IsCaptain = p.IsCaptain
            }).ToList()
        };
        return Success(PublicShapes.Of(dto)!);
    }

    /// <summary>The page as a stranger sees it (no admin's extras), for the public answer.</summary>
    private async Task<ClubDto?> GetAsyncAs(int clubId) => (await GetAsync(clubId)).Value;

    // ---------------------------------------------------------------------------------------------------------------------
    // Accounts going away
    // ---------------------------------------------------------------------------------------------------------------------

    public async Task ForgetUserAsync(int userId)
    {
        foreach (var place in await _players.ListAsync(p => p.UserId == userId))
        {
            place.UserId = null;
            place.User = null;
            place.EmailId = null;
            place.PhoneNumber = null;
        }

        // A club whose last admin leaves is handed to the player on the app who has been in it longest.
        var seats = await _admins.ListAsync(a => a.UserId == userId);
        foreach (var seat in seats)
        {
            if (!await _admins.AnyAsync(a => a.ClubId == seat.ClubId && a.UserId != userId))
            {
                var next = await _players.QueryFirstOrDefaultAsync(q => q
                    .Where(p => p.ClubId == seat.ClubId && p.UserId != null && p.UserId != userId)
                    .OrderBy(p => p.Crd).ThenBy(p => p.ClubPlayerId)
                    .Select(p => p.UserId), tracking: false);
                if (next is { } heir)
                {
                    var admin = new ClubAdmin { ClubId = seat.ClubId, UserId = heir };
                    admin.Created(Now, userId);
                    _admins.Add(admin);
                }
            }
            _admins.Remove(seat);
        }
    }

    // ---------------------------------------------------------------------------------------------------------------------

    private static (string? Name, string? Area, string? Problem) Describe(string? name, string? area)
    {
        var n = Squash(name);
        var a = Squash(area);
        if (n.Length == 0) return (null, null, "Give the team a name.");
        if (n.Length > Club.NameMax) return (null, null, $"A team's name is at most {Club.NameMax} characters.");
        if (a.Length > Club.AreaMax) return (null, null, $"Where it plays is at most {Club.AreaMax} characters.");
        return (n, a.Length == 0 ? null : a, null);
    }

    /// <summary>Trimmed, with runs of spaces made one.</summary>
    private static string Squash(string? text) => string.Join(' ', (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string Fit(string text, int max) => text.Length <= max ? text : text[..max].TrimEnd();

    private async Task<ServiceError?> SaveAsync()
    {
        try
        {
            await _unitOfWork.SaveChangesAsync();
            return null;
        }
        catch (DuplicateEntryException)
        {
            return Conflict("Someone else changed this team at the same moment. Reload it and try again.");
        }
    }

    private async Task<string> NewJoinCodeAsync()
    {
        string code;
        do code = JoinCodes.New(JoinCodes.ClubLength);
        while (await _clubs.AnyAsync(c => c.JoinCode == code));
        return code;
    }

    private async Task<Dictionary<int, string?>> PhotosAsync(List<int> userIds)
    {
        if (userIds.Count == 0) return [];
        return (await _users.QueryAsync(q => q
                .Where(u => userIds.Contains(u.UserId) && u.PhotoId != null)
                .Select(u => new { u.UserId, u.PhotoId }), tracking: false))
            .ToDictionary(u => u.UserId, u => u.PhotoId);
    }
}
