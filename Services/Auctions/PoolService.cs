using Microsoft.Extensions.Options;
using TournamentScheduler.Api.Auth;
using TournamentScheduler.Api.Data;
using TournamentScheduler.Api.Data.DataServices;
using TournamentScheduler.Api.Models;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Models.Auctions;
using TournamentScheduler.Api.Models.Cricket;
using TournamentScheduler.Api.Services.Access;
using TournamentScheduler.Api.Services.Accounts;

namespace TournamentScheduler.Api.Services.Auctions;

public interface IPoolService
{
    Task<ServiceResult<PoolDto>> GetAsync(int tournamentId);
    Task<ServiceResult<PlayerLookupDto>> LookupAsync(int tournamentId, PoolLookupRequest request);
    Task<ServiceResult<PoolDto>> AddAsync(int tournamentId, PoolPlayerRequest request);
    Task<ServiceResult<PoolDto>> UpdateAsync(int tournamentId, int poolPlayerId, PoolPlayerUpdateRequest request);
    Task<ServiceResult<PoolDto>> RemoveAsync(int tournamentId, int poolPlayerId);
    Task<ServiceResult<PoolDto>> MoveAsync(int tournamentId, PoolMoveRequest request);
    Task<ServiceResult<PoolDto>> SaveTiersAsync(int tournamentId, PoolTiersRequest request);
    Task<ServiceResult<PoolDto>> RegistrationAsync(int tournamentId, PoolRegistrationRequest request);
    Task<ServiceResult<PoolJoinPreviewDto>> JoinPreviewAsync(PoolJoinRequest request);
    Task<ServiceResult<PoolJoinResult>> JoinAsync(PoolJoinRequest request);
    Task<ServiceResult<PoolDto>> MethodAsync(int tournamentId, PoolMethodRequest request);

    /// <summary>What each pool player plays, in words (their own cricket profile when linked).</summary>
    Task<Dictionary<int, string?>> RolesAsync(Sport sport, IReadOnlyList<PoolPlayer> people);
}

/// <summary>
/// The player pool (plan section 2): everyone still to be placed in a team, added by the organiser
/// by email or number (so accounts link, never a pasted list) or registering themselves with the
/// pool's code, in tiers with a base price. Changes freely until the auction starts; players can
/// still be added while it runs (they go into the next round).
/// </summary>
public class PoolService : ServiceBase, IPoolService
{
    /// <summary>Pool codes are a character longer than team codes, so one "Join" field tells them apart.</summary>
    public const int CodeLength = 7;

    public const int MaxPlayers = 400;
    public const int MaxTiers = 8;

    private readonly IDataService<Tournament> _tournaments;
    private readonly IDataService<PlayerPool> _pools;
    private readonly IDataService<PoolTier> _tiers;
    private readonly IDataService<PoolPlayer> _poolPlayers;
    private readonly IDataService<Auction> _auctions;
    private readonly IDataService<AuctionLot> _lots;
    private readonly IDataService<Team> _teams;
    private readonly IDataService<Player> _players;
    private readonly IDataService<User> _users;
    private readonly IDataService<UserCricketProfile> _profiles;
    private readonly IPlayerLinkService _links;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITournamentContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly PlayerLookups _lookups;
    private readonly JoinCodeAttempts _attempts;
    private readonly SmsOptions _sms;
    private readonly IDataService<TeamDraw> _draws;
    private readonly IDataService<DrawPick> _picks;
    private readonly TimeProvider _clock;

    public PoolService(
        IDataService<Tournament> tournaments,
        IDataService<PlayerPool> pools,
        IDataService<PoolTier> tiers,
        IDataService<PoolPlayer> poolPlayers,
        IDataService<Auction> auctions,
        IDataService<AuctionLot> lots,
        IDataService<Team> teams,
        IDataService<Player> players,
        IDataService<User> users,
        IDataService<UserCricketProfile> profiles,
        IPlayerLinkService links,
        IUnitOfWork unitOfWork,
        ITournamentContext context,
        ICurrentUser currentUser,
        PlayerLookups lookups,
        JoinCodeAttempts attempts,
        IOptions<SmsOptions> sms,
        IDataService<TeamDraw> draws,
        IDataService<DrawPick> picks,
        TimeProvider clock)
    {
        _draws = draws;
        _picks = picks;
        _clock = clock;
        _tournaments = tournaments;
        _pools = pools;
        _tiers = tiers;
        _poolPlayers = poolPlayers;
        _auctions = auctions;
        _lots = lots;
        _teams = teams;
        _players = players;
        _users = users;
        _profiles = profiles;
        _links = links;
        _unitOfWork = unitOfWork;
        _context = context;
        _currentUser = currentUser;
        _lookups = lookups;
        _attempts = attempts;
        _sms = sms.Value;
    }

    /// <summary>The default tiers a new pool starts with (plan 2.2); the organiser renames, reprices or removes them.</summary>
    public static readonly (string Name, int BasePrice)[] DefaultTiers = [("Marquee", 1000), ("A", 500), ("B", 200), ("C", 100)];

    // -----------------------------------------------------------------
    // Reading
    // -----------------------------------------------------------------

    public async Task<ServiceResult<PoolDto>> GetAsync(int tournamentId)
    {
        if (!await _tournaments.AnyAsync(t => t.Id == tournamentId)) return NotFound("Tournament not found.");
        return Success(await BuildAsync(tournamentId));
    }

    private async Task<PoolDto> BuildAsync(int tournamentId)
    {
        var tournament = (await _tournaments.GetByIdAsync(tournamentId))!;
        var pool = await _pools.FirstOrDefaultAsync(p => p.TournamentId == tournamentId, tracking: false);
        var tiers = await _tiers.ListAsync(t => t.TournamentId == tournamentId, q => q.OrderBy(t => t.Order), tracking: false);
        var people = await _poolPlayers.ListAsync(p => p.TournamentId == tournamentId, q => q.OrderBy(p => p.Order).ThenBy(p => p.Id), tracking: false);
        var auction = await _auctions.FirstOrDefaultAsync(a => a.TournamentId == tournamentId, tracking: false);
        var lots = auction == null ? [] : await _lots.ListAsync(l => l.AuctionId == auction.Id && l.CancelledAt == null, tracking: false);
        var teams = await _teams.QueryAsync(q => q.Where(t => t.TournamentId == tournamentId)
            .Select(t => new { t.Id, t.Name, Players = t.Players.Count }), tracking: false);
        var teamNames = teams.ToDictionary(t => t.Id, t => t.Name);
        var roles = await RolesAsync(tournament.Sport, people);
        var draw = await _draws.FirstOrDefaultAsync(d => d.TournamentId == tournamentId, tracking: false);
        // Only names that have landed: one still spinning in the draw stays a secret here too.
        var now = _clock.GetUtcNow().UtcDateTime;
        var drawn = draw == null
            ? new Dictionary<int, int>()
            : (await _picks.QueryAsync(q => q.Where(p => p.DrawId == draw.Id && p.LandsAt <= now).Select(p => new { p.PoolPlayerId, p.TeamId }), tracking: false))
                .ToDictionary(p => p.PoolPlayerId, p => p.TeamId);

        var owner = _context.IsOwner;
        var me = _currentUser.UserId;
        var status = auction?.Status ?? AuctionStatus.Setup;
        var drawStatus = draw?.Status;
        var made = status == AuctionStatus.Confirmed || drawStatus == DrawStatus.Confirmed;
        var drawing = drawStatus == DrawStatus.Live;
        var open = !tournament.IsStarted && !tournament.IsClosed;

        return new PoolDto
        {
            Method = pool?.Method,
            CanChooseMethod = owner && open && status == AuctionStatus.Setup && drawStatus is null or DrawStatus.Setup,
            DrawStatus = drawStatus?.ToString() ?? "None",
            AuctionStatus = auction?.Status.ToString() ?? "None",
            JoinCode = owner ? pool?.JoinCode : null,
            RegistrationOpen = pool?.IsOpen == true && open && !made,
            CanEdit = owner && open && status == AuctionStatus.Setup && !drawing && !made,
            CanAdd = owner && open && !made,
            Tiers = tiers.Select(t => TierDto(t, people.Count(p => p.TierId == t.Id && !p.IsWithdrawn))).ToList(),
            Players = people.Select(p =>
            {
                var (state, teamId, price) = drawn.TryGetValue(p.Id, out var drawnTo) ? ("Drawn", (int?)drawnTo, (int?)null) : StatusOf(p.Id, lots);
                return new PoolPlayerDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    Role = roles.GetValueOrDefault(p.Id),
                    Position = p.Position,
                    CricketRole = p.CricketRole,
                    TierId = p.TierId,
                    IsLinked = p.UserId != null,
                    JoinedByCode = p.JoinedByCode,
                    IsWithdrawn = p.IsWithdrawn,
                    IsMe = p.UserId != null && p.UserId == me,
                    Email = owner ? p.EmailId : null,
                    Phone = owner ? p.PhoneNumber : null,
                    Status = state,
                    TeamId = teamId,
                    TeamName = teamId is int t ? teamNames.GetValueOrDefault(t) : null,
                    Price = price
                };
            }).ToList(),
            Teams = teams.Count,
            KeptPlayers = teams.Sum(t => t.Players),
            MyPoolPlayerId = people.FirstOrDefault(p => p.UserId != null && p.UserId == me)?.Id
        };
    }

    public static PoolTierDto TierDto(PoolTier t, int players) => new()
    {
        Id = t.Id, Name = t.Name, Order = t.Order, BasePrice = t.BasePrice, MaxPerTeam = t.MaxPerTeam, Players = players
    };

    /// <summary>Where a pool player stands, from their lots: sold (to whom, for what), being auctioned, unsold, or not yet offered.</summary>
    public static (string Status, int? TeamId, int? Price) StatusOf(int poolPlayerId, IReadOnlyList<AuctionLot> lots)
    {
        var mine = lots.Where(l => l.PoolPlayerId == poolPlayerId).ToList();
        if (mine.FirstOrDefault(l => l.Status == AuctionLotStatus.Sold) is { } sold) return ("Sold", sold.SoldTeamId, sold.Price);
        if (mine.Any(l => l.Status is AuctionLotStatus.Waiting or AuctionLotStatus.Open)) return ("InAuction", null, null);
        return mine.Count > 0 ? ("Unsold", null, null) : ("Available", null, null);
    }

    /// <summary>What each player plays, in words: their own cricket profile when linked, else what the organiser said.</summary>
    public async Task<Dictionary<int, string?>> RolesAsync(Sport sport, IReadOnlyList<PoolPlayer> people)
    {
        if (sport != Sport.Cricket)
            return people.ToDictionary(p => p.Id, p => string.IsNullOrWhiteSpace(p.Position) ? null : p.Position.Trim());

        var userIds = people.Where(p => p.UserId != null).Select(p => p.UserId!.Value).Distinct().ToList();
        var profiles = userIds.Count == 0
            ? new Dictionary<int, CricketRole>()
            : (await _profiles.QueryAsync(q => q.Where(p => userIds.Contains(p.UserId)).Select(p => new { p.UserId, p.PrimaryRole }), tracking: false))
                .ToDictionary(p => p.UserId, p => p.PrimaryRole);
        return people.ToDictionary(p => p.Id, p =>
            p.UserId is int u && profiles.TryGetValue(u, out var own) ? CricketRoles.Describe(own)
            : p.CricketRole is { } role ? CricketRoles.Describe(role) : null);
    }

    // -----------------------------------------------------------------
    // Adding and changing players
    // -----------------------------------------------------------------

    /// <summary>Why the pool can't change now, or null. <paramref name="adding"/>: players may still join a running auction.</summary>
    private async Task<string?> LockedAsync(int tournamentId, bool adding)
    {
        var tournament = await _tournaments.GetByIdAsync(tournamentId);
        if (tournament!.IsStarted) return "The tournament has started, so the pool is closed. Add players straight to a team.";
        var status = await _auctions.QueryFirstOrDefaultAsync(q => q.Where(a => a.TournamentId == tournamentId).Select(a => (AuctionStatus?)a.Status), tracking: false);
        if (status == AuctionStatus.Confirmed) return "The teams are confirmed, so the pool is closed. Add players straight to a team.";
        if (!adding && status is not (null or AuctionStatus.Setup))
            return "The auction has started, so the pool can't be changed. Withdraw a player instead, or cancel the auction.";
        var draw = await _draws.QueryFirstOrDefaultAsync(q => q.Where(d => d.TournamentId == tournamentId).Select(d => (DrawStatus?)d.Status), tracking: false);
        if (draw == DrawStatus.Confirmed) return "The teams are confirmed, so the pool is closed. Add players straight to a team.";
        if (!adding && draw == DrawStatus.Live)
            return "The draw has started, so the pool can't be changed. Withdraw a player instead, or start the draw again.";
        return null;
    }

    public async Task<ServiceResult<PlayerLookupDto>> LookupAsync(int tournamentId, PoolLookupRequest request)
    {
        var (contact, problem) = Contacts.Read(request.Contact, _sms.IsEnabled);
        if (contact is not { } found) return BadRequest(problem!);

        if (_lookups.TryLookup(_currentUser.UserId) is { } wait)
            return TooManyRequests($"That's a lot of lookups. Try again in {Math.Max(1, (int)Math.Ceiling(wait.TotalMinutes))} minutes, or add the player by name.");

        var user = await _links.AccountWithAsync(found);
        string? inPool = null;
        if (user != null && await _poolPlayers.AnyAsync(p => p.TournamentId == tournamentId && p.UserId == user.UserId))
            inPool = "the pool";
        return Success(new PlayerLookupDto
        {
            Kind = found.Kind,
            Contact = found.Value,
            Display = Contacts.Display(found.Value),
            Found = user != null,
            Name = user?.Name,
            PlaysFor = user == null ? null : await _links.TeamAlreadyInAsync(tournamentId, user.UserId) ?? inPool,
            WaitingIn = user == null ? await WaitingInAsync(tournamentId, found) : null
        });
    }

    private async Task<string?> WaitingInAsync(int tournamentId, Contact contact)
    {
        var value = contact.Value;
        var inPool = contact.IsEmail
            ? await _poolPlayers.AnyAsync(p => p.TournamentId == tournamentId && p.UserId == null && p.EmailId == value)
            : await _poolPlayers.AnyAsync(p => p.TournamentId == tournamentId && p.UserId == null && p.PhoneNumber == value);
        return inPool ? "the pool" : await _links.TeamWaitingOnAsync(tournamentId, contact);
    }

    public async Task<ServiceResult<PoolDto>> AddAsync(int tournamentId, PoolPlayerRequest request)
    {
        if (await LockedAsync(tournamentId, adding: true) is { } locked) return Conflict(locked);
        if (await _poolPlayers.CountAsync(p => p.TournamentId == tournamentId) >= MaxPlayers)
            return Conflict($"A pool holds up to {MaxPlayers} players.");

        var email = string.IsNullOrWhiteSpace(request.Email) ? null : AccountService.NormaliseEmail(request.Email);
        if (email == null && !string.IsNullOrWhiteSpace(request.Email)) return BadRequest("Enter a valid email address for the player, or leave it empty.");
        var phone = string.IsNullOrWhiteSpace(request.Phone) ? null : Contacts.NormalisePhone(request.Phone);
        if (phone == null && !string.IsNullOrWhiteSpace(request.Phone)) return BadRequest("Enter a valid mobile number for the player, or leave it empty.");

        var byEmail = email == null ? null : await _links.AccountWithAsync(new Contact(ContactKind.Email, email));
        var byPhone = phone == null ? null : await _links.AccountWithAsync(new Contact(ContactKind.Phone, phone));
        if (byEmail != null && byPhone != null && byEmail.UserId != byPhone.UserId)
            return Conflict("That email and mobile number belong to two different people. Give one of them.");
        var user = byEmail ?? byPhone;

        var name = request.Name?.Trim() ?? "";
        if (user == null && name.Length == 0)
            return BadRequest(email != null || phone != null
                ? "No account uses that yet, so add the player's name. They'll be linked when they sign up."
                : "Player name is required.");
        if (name.Length > 100) return BadRequest("Keep the name under 100 characters.");

        if (user != null && await ProblemWithAsync(tournamentId, user) is { } taken) return Conflict(taken);
        if (user == null && email != null && await _poolPlayers.AnyAsync(p => p.TournamentId == tournamentId && p.EmailId == email))
            return Conflict("Someone in the pool already has that email.");
        if (user == null && phone != null && await _poolPlayers.AnyAsync(p => p.TournamentId == tournamentId && p.PhoneNumber == phone))
            return Conflict("Someone in the pool already has that number.");

        await EnsurePoolAsync(tournamentId);
        if (request.TierId is int tierId && !await _tiers.AnyAsync(t => t.Id == tierId && t.TournamentId == tournamentId))
            return BadRequest("That tier isn't in this pool.");

        var row = new PoolPlayer
        {
            TournamentId = tournamentId,
            Name = user?.Name ?? name,
            UserId = user?.UserId,
            EmailId = user?.EmailId ?? email,
            PhoneNumber = phone,
            Position = Clean(request.Position, 40),
            CricketRole = request.CricketRole,
            TierId = request.TierId,
            Order = await NextOrderAsync(tournamentId),
            CreatedByUserId = _currentUser.UserId
        };
        _poolPlayers.Add(row);
        if (await SaveAsync() is { } race) return race;
        return Success(await BuildAsync(tournamentId));
    }

    /// <summary>Why this account can't join the pool: already in it, or already in a team.</summary>
    private async Task<string?> ProblemWithAsync(int tournamentId, User user, bool self = false)
    {
        if (await _poolPlayers.AnyAsync(p => p.TournamentId == tournamentId && p.UserId == user.UserId))
            return self ? "You're already in the pool." : $"{user.Name} is already in the pool.";
        if (await _links.TeamAlreadyInAsync(tournamentId, user.UserId) is { } team)
            return self ? $"You're already in {team}." : $"{user.Name} is already in {team} in this tournament.";
        return null;
    }

    public async Task<ServiceResult<PoolDto>> UpdateAsync(int tournamentId, int poolPlayerId, PoolPlayerUpdateRequest request)
    {
        var row = await _poolPlayers.FirstOrDefaultAsync(p => p.Id == poolPlayerId && p.TournamentId == tournamentId);
        if (row == null) return NotFound("That player isn't in the pool any more.");

        var changesPool = request.SetTier || request.Name != null || request.Position != null || request.CricketRole != null;
        if (changesPool && await LockedAsync(tournamentId, adding: false) is { } locked) return Conflict(locked);

        if (request.Name != null && row.UserId == null)
        {
            var name = request.Name.Trim();
            if (name.Length == 0) return BadRequest("Player name is required.");
            if (name.Length > 100) return BadRequest("Keep the name under 100 characters.");
            row.Name = name;
        }
        if (request.Position != null) row.Position = Clean(request.Position, 40);
        if (request.CricketRole != null) row.CricketRole = request.CricketRole;
        if (request.SetTier)
        {
            if (request.TierId is int tierId && !await _tiers.AnyAsync(t => t.Id == tierId && t.TournamentId == tournamentId))
                return BadRequest("That tier isn't in this pool.");
            row.TierId = request.TierId;
        }

        if (request.Withdrawn is bool withdrawn && withdrawn != row.IsWithdrawn)
        {
            if (await WithdrawProblemAsync(tournamentId, row, withdrawn) is { } problem) return Conflict(problem);
            row.IsWithdrawn = withdrawn;
        }

        await _unitOfWork.SaveChangesAsync();
        return Success(await BuildAsync(tournamentId));
    }

    /// <summary>
    /// Withdrawing takes a player out of the auction (plan 4.5): a waiting turn is dropped, a sale
    /// must be undone first, and someone under the hammer can't be pulled mid-bid. Stages the change.
    /// </summary>
    private async Task<string?> WithdrawProblemAsync(int tournamentId, PoolPlayer row, bool withdrawn)
    {
        var draw = await _draws.FirstOrDefaultAsync(d => d.TournamentId == tournamentId, tracking: false);
        if (draw?.Status == DrawStatus.Confirmed) return "The teams are confirmed, so the pool is closed.";
        if (draw?.Status == DrawStatus.Live && await _picks.FirstOrDefaultAsync(p => p.DrawId == draw.Id && p.PoolPlayerId == row.Id) is { } pick)
        {
            if (pick.LandsAt > _clock.GetUtcNow().UtcDateTime) return $"{row.Name} is being drawn right now. Wait for the name to land.";
            if (withdrawn) _picks.Remove(pick); // out of the team they were drawn for
        }

        var auction = await _auctions.FirstOrDefaultAsync(a => a.TournamentId == tournamentId, tracking: false);
        if (auction?.Status == AuctionStatus.Confirmed) return "The teams are confirmed, so the pool is closed.";
        if (auction == null || auction.Status == AuctionStatus.Setup) return null;

        var lots = await _lots.ListAsync(l => l.AuctionId == auction.Id && l.PoolPlayerId == row.Id && l.CancelledAt == null);
        if (lots.Any(l => l.Status == AuctionLotStatus.Sold)) return $"{row.Name} has been sold. Undo the sale first.";
        if (lots.Any(l => l.Status == AuctionLotStatus.Open)) return $"{row.Name} is under the hammer. Sell them or mark them unsold first.";
        if (withdrawn)
            foreach (var waiting in lots.Where(l => l.Status == AuctionLotStatus.Waiting))
                waiting.Status = AuctionLotStatus.Returned;
        return null;
    }

    public async Task<ServiceResult<PoolDto>> RemoveAsync(int tournamentId, int poolPlayerId)
    {
        if (await LockedAsync(tournamentId, adding: false) is { } locked) return Conflict(locked);
        var row = await _poolPlayers.FirstOrDefaultAsync(p => p.Id == poolPlayerId && p.TournamentId == tournamentId);
        if (row == null) return NotFound("That player isn't in the pool any more.");
        _poolPlayers.Remove(row);
        await _unitOfWork.SaveChangesAsync();
        return Success(await BuildAsync(tournamentId));
    }

    public async Task<ServiceResult<PoolDto>> MoveAsync(int tournamentId, PoolMoveRequest request)
    {
        if (await LockedAsync(tournamentId, adding: false) is { } locked) return Conflict(locked);
        if (request.PlayerIds.Count == 0) return BadRequest("Pick at least one player.");
        if (request.TierId is int tierId && !await _tiers.AnyAsync(t => t.Id == tierId && t.TournamentId == tournamentId))
            return BadRequest("That tier isn't in this pool.");

        var ids = request.PlayerIds.Distinct().ToList();
        var rows = await _poolPlayers.ListAsync(p => p.TournamentId == tournamentId && ids.Contains(p.Id));
        if (rows.Count != ids.Count) return Conflict("Some of those players aren't in the pool any more. Reload and try again.");
        rows.ForEach(r => r.TierId = request.TierId);
        await _unitOfWork.SaveChangesAsync();
        return Success(await BuildAsync(tournamentId));
    }

    // -----------------------------------------------------------------
    // Tiers
    // -----------------------------------------------------------------

    public async Task<ServiceResult<PoolDto>> SaveTiersAsync(int tournamentId, PoolTiersRequest request)
    {
        if (await LockedAsync(tournamentId, adding: false) is { } locked) return Conflict(locked);
        if (request.Tiers.Count > MaxTiers) return BadRequest($"Use at most {MaxTiers} tiers.");

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in request.Tiers)
        {
            var name = t.Name?.Trim() ?? "";
            if (name.Length == 0) return BadRequest("Every tier needs a name.");
            if (name.Length > 30) return BadRequest("Keep tier names under 30 characters.");
            if (!names.Add(name)) return BadRequest($"Two tiers are called {name}. Give each its own name.");
            if (t.BasePrice is < 0 or > 10_000_000) return BadRequest($"{name}: give a base price of 0 or more.");
            if (t.MaxPerTeam is < 1 or > 100) return BadRequest($"{name}: the most a team may buy is at least 1, or leave it as no limit.");
        }

        await EnsurePoolAsync(tournamentId, withDefaults: false);
        var existing = await _tiers.ListAsync(t => t.TournamentId == tournamentId);
        var kept = new HashSet<int>();
        for (var i = 0; i < request.Tiers.Count; i++)
        {
            var input = request.Tiers[i];
            var tier = input.Id is int id ? existing.FirstOrDefault(t => t.Id == id) : null;
            if (input.Id != null && tier == null) return Conflict("A tier was removed meanwhile. Reload and try again.");
            if (tier == null)
            {
                tier = new PoolTier { TournamentId = tournamentId };
                _tiers.Add(tier);
            }
            else kept.Add(tier.Id);
            tier.Name = input.Name.Trim();
            tier.Order = i + 1;
            tier.BasePrice = input.BasePrice;
            tier.MaxPerTeam = input.MaxPerTeam;
        }

        // A tier that goes leaves its players in "No tier" (plan 4.9).
        var gone = existing.Where(t => !kept.Contains(t.Id)).ToList();
        if (gone.Count > 0)
        {
            var goneIds = gone.Select(t => t.Id).ToList();
            foreach (var p in await _poolPlayers.ListAsync(p => p.TournamentId == tournamentId && p.TierId != null && goneIds.Contains(p.TierId.Value)))
                p.TierId = null;
            _tiers.RemoveRange(gone);
        }

        await _unitOfWork.SaveChangesAsync();
        return Success(await BuildAsync(tournamentId));
    }

    // -----------------------------------------------------------------
    // Registering with a code
    // -----------------------------------------------------------------

    public async Task<ServiceResult<PoolDto>> RegistrationAsync(int tournamentId, PoolRegistrationRequest request)
    {
        if (request.Open && await LockedAsync(tournamentId, adding: true) is { } locked) return Conflict(locked);
        var pool = await EnsurePoolAsync(tournamentId);
        pool.IsOpen = request.Open;
        if (request.Open && pool.JoinCode == null)
        {
            string code;
            do code = NewCode();
            while (await _pools.AnyAsync(p => p.JoinCode == code));
            pool.JoinCode = code;
        }
        await _unitOfWork.SaveChangesAsync();
        return Success(await BuildAsync(tournamentId));
    }

    // -----------------------------------------------------------------
    // Draw or auction
    // -----------------------------------------------------------------

    /// <summary>How the teams are filled: switchable until the draw or the auction starts, then fixed (E-1).</summary>
    public async Task<ServiceResult<PoolDto>> MethodAsync(int tournamentId, PoolMethodRequest request)
    {
        if (request.Method is not (PoolMethod.Draw or PoolMethod.Auction)) return BadRequest("Choose the draw or the auction.");
        var tournament = await _tournaments.GetByIdAsync(tournamentId);
        if (tournament!.IsStarted) return Conflict("The tournament has started, so its teams are made.");
        var auction = await _auctions.QueryFirstOrDefaultAsync(q => q.Where(a => a.TournamentId == tournamentId).Select(a => (AuctionStatus?)a.Status), tracking: false);
        var draw = await _draws.QueryFirstOrDefaultAsync(q => q.Where(d => d.TournamentId == tournamentId).Select(d => (DrawStatus?)d.Status), tracking: false);
        if (auction is not (null or AuctionStatus.Setup))
            return Conflict(auction == AuctionStatus.Confirmed ? "The teams were made by the auction." : "The auction has started, so it stays an auction. Cancel it first to draw the teams instead.");
        if (draw is not (null or DrawStatus.Setup))
            return Conflict(draw == DrawStatus.Confirmed ? "The teams were made by the draw." : "The draw has started, so it stays a draw. Start it again from the beginning to choose the auction instead.");

        var pool = await EnsurePoolAsync(tournamentId);
        pool.Method = request.Method;
        await _unitOfWork.SaveChangesAsync();
        return Success(await BuildAsync(tournamentId));
    }

    private static string NewCode()
    {
        Span<char> code = stackalloc char[CodeLength];
        for (var i = 0; i < CodeLength; i++)
            code[i] = JoinCodes.Alphabet[System.Security.Cryptography.RandomNumberGenerator.GetInt32(JoinCodes.Alphabet.Length)];
        return new string(code);
    }

    private static string? NormaliseCode(string? typed)
    {
        var code = new string((typed ?? "").Where(ch => !char.IsWhiteSpace(ch) && ch != '-').ToArray()).ToUpperInvariant();
        return code.Length == CodeLength && code.All(JoinCodes.Alphabet.Contains) ? code : null;
    }

    private sealed record OpenPool(int TournamentId, string Name, Sport Sport);

    public async Task<ServiceResult<PoolJoinPreviewDto>> JoinPreviewAsync(PoolJoinRequest request)
    {
        var (pool, error) = await FindPoolAsync(request.Code);
        if (error is { } e) return e;

        var user = await _users.GetByIdAsync(_currentUser.UserId);
        if (user == null) return NotFound("Account not found.");
        if (await ProblemWithAsync(pool!.TournamentId, user, self: true) is { } taken) return Conflict(taken);

        return Success(new PoolJoinPreviewDto
        {
            TournamentId = pool.TournamentId,
            TournamentName = pool.Name,
            Sport = pool.Sport,
            Players = await _poolPlayers.CountAsync(p => p.TournamentId == pool.TournamentId && !p.IsWithdrawn)
        });
    }

    public async Task<ServiceResult<PoolJoinResult>> JoinAsync(PoolJoinRequest request)
    {
        var (pool, error) = await FindPoolAsync(request.Code);
        if (error is { } e) return e;

        var user = await _users.GetByIdAsync(_currentUser.UserId);
        if (user == null) return NotFound("Account not found.");
        if (await ProblemWithAsync(pool!.TournamentId, user, self: true) is { } taken) return Conflict(taken);

        var row = new PoolPlayer
        {
            TournamentId = pool.TournamentId,
            Name = user.Name,
            UserId = user.UserId,
            EmailId = user.EmailId,
            PhoneNumber = user.PhoneNumber,
            JoinedByCode = true,
            Order = await NextOrderAsync(pool.TournamentId),
            CreatedByUserId = user.UserId
        };
        _poolPlayers.Add(row);
        if (await SaveAsync() is { } race) return race;

        return Success(new PoolJoinResult { TournamentId = pool.TournamentId, TournamentName = pool.Name, Sport = pool.Sport, PoolPlayerId = row.Id });
    }

    /// <summary>The open pool a typed code belongs to, if this person may register with it now. Wrong codes count towards the limit.</summary>
    private async Task<(OpenPool? Pool, ServiceError? Error)> FindPoolAsync(string? typed)
    {
        var me = _currentUser.UserId;
        if (_attempts.WaitFor(me) is { } wait)
            return (null, TooManyRequests($"Too many wrong codes. Try again in {Math.Max(1, (int)Math.Ceiling(wait.TotalMinutes))} minutes."));

        var code = NormaliseCode(typed);
        if (code == null) return (null, BadRequest($"A player pool code is {CodeLength} letters and numbers."));

        var pool = await _pools.FirstOrDefaultAsync(p => p.JoinCode == code, tracking: false);
        var tournament = pool == null ? null : await _tournaments.FirstOrDefaultAsync(t => t.Id == pool.TournamentId, tracking: false);
        if (pool == null || tournament == null)
        {
            _attempts.Missed(me);
            return (null, NotFound("No player pool has that code. Check it with the organiser."));
        }
        if (!pool.IsOpen || tournament.IsStarted || tournament.IsClosed)
            return (null, Conflict($"Registrations for {tournament.Name} are closed. Ask the organiser to add you."));
        var status = await _auctions.QueryFirstOrDefaultAsync(q => q.Where(a => a.TournamentId == tournament.Id).Select(a => (AuctionStatus?)a.Status), tracking: false);
        var drawStatus = await _draws.QueryFirstOrDefaultAsync(q => q.Where(d => d.TournamentId == tournament.Id).Select(d => (DrawStatus?)d.Status), tracking: false);
        if (status == AuctionStatus.Confirmed || drawStatus == DrawStatus.Confirmed)
            return (null, Conflict($"The teams for {tournament.Name} are already made. Ask the organiser to add you."));
        return (new OpenPool(tournament.Id, tournament.Name, tournament.Sport), null);
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    /// <summary>The tournament's pool row, made (with the default tiers) the first time anything is added.</summary>
    private async Task<PlayerPool> EnsurePoolAsync(int tournamentId, bool withDefaults = true)
    {
        var pool = await _pools.FirstOrDefaultAsync(p => p.TournamentId == tournamentId);
        if (pool != null) return pool;

        pool = new PlayerPool { TournamentId = tournamentId };
        _pools.Add(pool);
        if (withDefaults && !await _tiers.AnyAsync(t => t.TournamentId == tournamentId))
            for (var i = 0; i < DefaultTiers.Length; i++)
                _tiers.Add(new PoolTier { TournamentId = tournamentId, Name = DefaultTiers[i].Name, BasePrice = DefaultTiers[i].BasePrice, Order = i + 1 });
        return pool;
    }

    private async Task<int> NextOrderAsync(int tournamentId) =>
        (await _poolPlayers.QueryAsync(q => q.Where(p => p.TournamentId == tournamentId).Select(p => p.Order), tracking: false))
        .DefaultIfEmpty(0).Max() + 1;

    private static string? Clean(string? text, int max)
    {
        var t = text?.Trim();
        return string.IsNullOrEmpty(t) ? null : t.Length > max ? t[..max] : t;
    }

    private async Task<ServiceError?> SaveAsync()
    {
        try
        {
            await _unitOfWork.SaveChangesAsync();
            return null;
        }
        catch (DuplicateEntryException)
        {
            // One pool place per account per tournament, beaten by another request.
            return Conflict("That player has just been added to the pool.");
        }
    }
}
