using TournamentScheduler.Api.Auth;
using TournamentScheduler.Api.Data;
using TournamentScheduler.Api.Data.DataServices;
using TournamentScheduler.Api.Models;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Models.Auctions;
using TournamentScheduler.Api.Models.Cricket;
using TournamentScheduler.Api.Models.Messaging;
using TournamentScheduler.Api.Realtime;
using TournamentScheduler.Api.Services.Access;
using TournamentScheduler.Api.Services.Accounts;
using TournamentScheduler.Api.Services.Notifications;
using static TournamentScheduler.Api.Services.Auctions.AuctionMath;

namespace TournamentScheduler.Api.Services.Auctions;

public interface IAuctionService
{
    Task<ServiceResult<AuctionStateDto>> GetAsync(int tournamentId);
    Task<ServiceResult<AuctionStateDto>> SettingsAsync(int tournamentId, AuctionSettingsRequest request);
    Task<ServiceResult<AuctionStateDto>> BidderAsync(int tournamentId, AuctionBidderRequest request);
    Task<ServiceResult<AuctionStateDto>> StartAsync(int tournamentId);
    Task<ServiceResult<AuctionStateDto>> NextAsync(int tournamentId, AuctionNextRequest request);
    Task<ServiceResult<AuctionStateDto>> BidAsync(int tournamentId, AuctionBidRequest request);
    Task<ServiceResult<AuctionStateDto>> SellAsync(int tournamentId);
    Task<ServiceResult<AuctionStateDto>> UnsoldAsync(int tournamentId);
    Task<ServiceResult<AuctionStateDto>> UndoBidAsync(int tournamentId);
    Task<ServiceResult<AuctionStateDto>> UndoSaleAsync(int tournamentId, AuctionLotRequest request);
    Task<ServiceResult<AuctionStateDto>> PauseAsync(int tournamentId);
    Task<ServiceResult<AuctionStateDto>> ResumeAsync(int tournamentId);
    Task<ServiceResult<AuctionStateDto>> TakeOverAsync(int tournamentId);
    Task<ServiceResult<AuctionStateDto>> StartRoundAsync(int tournamentId, AuctionRoundRequest request);
    Task<ServiceResult<AuctionStateDto>> FinishAsync(int tournamentId);
    Task<ServiceResult<AuctionStateDto>> AssignAsync(int tournamentId, AuctionAssignRequest request);
    Task<ServiceResult<AuctionStateDto>> ConfirmAsync(int tournamentId);
    Task<ServiceResult<AuctionStateDto>> CancelAsync(int tournamentId);
    Task<ServiceResult<AuctionStateDto>> MoveAsync(int tournamentId, AuctionMoveRequest request);
    Task<ServiceResult<AuctionStateDto>> SwapAsync(int tournamentId, AuctionSwapRequest request);
    Task<ServiceResult<AuctionFillPreviewDto>> FillPreviewAsync(int tournamentId);
    Task<ServiceResult<AuctionStateDto>> FillAsync(int tournamentId);

    /// <summary>The bid clock ran out: sells (or marks unsold) the player under the hammer. For the clock job. Saves and nudges.</summary>
    Task CloseDueAsync(int tournamentId);

    /// <summary>Why the tournament can't start yet because of its auction or its pool (players still without a team); null if it can.</summary>
    Task<string?> StartProblemAsync(int tournamentId);
}

/// <summary>
/// The auction (plan section 4). One auctioneer runs it (an owner who bids for no team); each team's
/// bidder bids from their phone, or the auctioneer records a bid heard in the room. The server
/// decides everything: the next amount, who may bid, the clock, when a round is over. Every answer
/// is the whole <see cref="AuctionStateDto"/> as the caller sees it.
///
/// <para>Bids for one tournament are handled one at a time (<see cref="AuctionLocks"/>), each checked
/// against the amount its bidder saw, so two taps at the same moment can never both win.</para>
/// </summary>
public class AuctionService : ServiceBase, IAuctionService
{
    /// <summary>How many recent bids every screen shows.</summary>
    private const int RecentBids = 6;

    /// <summary>Resuming never leaves less than this on the clock, so nobody is caught out.</summary>
    private const int MinResumeMs = 5000;

    private readonly IDataService<Tournament> _tournaments;
    private readonly IDataService<Team> _teams;
    private readonly IDataService<Player> _players;
    private readonly IDataService<User> _users;
    private readonly IDataService<Auction> _auctions;
    private readonly IDataService<AuctionTeam> _auctionTeams;
    private readonly IDataService<AuctionLot> _lots;
    private readonly IDataService<AuctionBid> _bids;
    private readonly IDataService<PoolTier> _tiers;
    private readonly IDataService<PoolPlayer> _pool;
    private readonly IPoolService _poolService;
    private readonly IPlayerLinkService _links;
    private readonly INotifier _notifier;
    private readonly ILiveUpdates _live;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly ITournamentContext _context;
    private readonly AuctionLocks _locks;
    private readonly AuctionClock _clockWatch;
    private readonly ISquadMaker _squads;
    private readonly IDataService<TeamDraw> _draws;
    private readonly IDataService<PlayerPool> _pools;
    private readonly TimeProvider _clock;

    public AuctionService(
        IDataService<Tournament> tournaments,
        IDataService<Team> teams,
        IDataService<Player> players,
        IDataService<User> users,
        IDataService<Auction> auctions,
        IDataService<AuctionTeam> auctionTeams,
        IDataService<AuctionLot> lots,
        IDataService<AuctionBid> bids,
        IDataService<PoolTier> tiers,
        IDataService<PoolPlayer> pool,
        IPoolService poolService,
        IPlayerLinkService links,
        INotifier notifier,
        ILiveUpdates live,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        ITournamentContext context,
        AuctionLocks locks,
        AuctionClock clockWatch,
        ISquadMaker squads,
        IDataService<TeamDraw> draws,
        IDataService<PlayerPool> pools,
        TimeProvider clock)
    {
        _squads = squads;
        _draws = draws;
        _pools = pools;
        _tournaments = tournaments;
        _teams = teams;
        _players = players;
        _users = users;
        _auctions = auctions;
        _auctionTeams = auctionTeams;
        _lots = lots;
        _bids = bids;
        _tiers = tiers;
        _pool = pool;
        _poolService = poolService;
        _links = links;
        _notifier = notifier;
        _live = live;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _context = context;
        _locks = locks;
        _clockWatch = clockWatch;
        _clock = clock;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    /// <summary>Who is looking: 0 for the public TV board, which nobody signs in to.</summary>
    private int Viewer => _currentUser.IsSignedIn ? _currentUser.UserId : 0;

    // =================================================================
    // The book: everything about one auction, loaded fresh inside the lock
    // =================================================================

    private sealed record TeamRow(int Id, string Name, int? CaptainPlayerId, int Players);

    private sealed record Captain(int PlayerId, int? UserId, string Name);

    private sealed record Figures(int Players, int Spent, int PurseLeft, int Needs, bool Full, int MaxBid, Dictionary<int, int> ByTier);

    private sealed class Book
    {
        public required Tournament Tournament;
        public Auction? Auction;
        public required List<TeamRow> Teams;
        public required Dictionary<int, Captain> Captains;
        public required List<AuctionTeam> Rows;
        public required List<PoolTier> Tiers;
        public required List<PoolPlayer> Pool;
        public required List<AuctionLot> Lots;
        public required List<AuctionBid> OpenBids;
        public required Dictionary<int, string?> Roles;
        public required Dictionary<int, string> Names;

        /// <summary>Where the tournament's team draw stands (null: none), and the pool's chosen way.</summary>
        public DrawStatus? Draw;
        public PoolMethod? Method;

        public AuctionStatus Status => Auction?.Status ?? AuctionStatus.Setup;

        /// <summary>The rules: the auction's, or the defaults before one exists.</summary>
        public Auction Rules => Auction ?? (_defaults ??= new Auction());
        private Auction? _defaults;
        public BidLadder Ladder => BidLadder.Parse(Auction?.BidSteps);
        public bool Started => Auction != null && Auction.Status != AuctionStatus.Setup;

        public AuctionLot? OpenLot => Auction?.OpenLotId is int id ? Lots.FirstOrDefault(l => l.Id == id && l.Status == AuctionLotStatus.Open) : null;
        public PoolPlayer? PlayerOf(AuctionLot lot) => Pool.FirstOrDefault(p => p.Id == lot.PoolPlayerId);
        public PoolTier? TierOf(PoolPlayer? p) => p?.TierId is int id ? Tiers.FirstOrDefault(t => t.Id == id) : null;
        public string TeamName(int id) => Teams.FirstOrDefault(t => t.Id == id)?.Name ?? "a team";

        public IEnumerable<AuctionBid> Active(AuctionLot lot) => OpenBids.Where(b => b.LotId == lot.Id && !b.IsUndone);
        public AuctionBid? Top(AuctionLot lot) => Active(lot).OrderByDescending(b => b.Amount).ThenByDescending(b => b.Id).FirstOrDefault();

        public IEnumerable<AuctionLot> RoundLots => Lots.Where(l => l.Round == Auction!.Round && l.Status != AuctionLotStatus.Returned);
        public IEnumerable<AuctionLot> Waiting => Lots.Where(l => l.Round == (Auction?.Round ?? 0) && l.Status == AuctionLotStatus.Waiting).OrderBy(l => l.Order);
        public IEnumerable<AuctionLot> SoldLots => Lots.Where(l => l.Status == AuctionLotStatus.Sold);

        /// <summary>The team's bidder: as set, or (before anyone set one) its captain if the captain has an account.</summary>
        public int? BidderOf(int teamId)
        {
            if (Rows.FirstOrDefault(r => r.TeamId == teamId) is { } row) return row.BidderUserId;
            var team = Teams.FirstOrDefault(t => t.Id == teamId);
            return team?.CaptainPlayerId is int cid && Captains.TryGetValue(cid, out var c) ? c.UserId : null;
        }

        public int? TeamBiddenBy(int userId) => Teams.Select(t => t.Id).Cast<int?>().FirstOrDefault(id => BidderOf(id!.Value) == userId);

        /// <summary>Players already in a team before the auction (counted live until the start, then fixed).</summary>
        public int Kept(int teamId) => Started && Rows.FirstOrDefault(r => r.TeamId == teamId) is { } row
            ? row.KeptPlayers
            : Teams.FirstOrDefault(t => t.Id == teamId)?.Players ?? 0;

        /// <summary>The cheapest base price this round: what a team keeps back for each place it still has to fill.</summary>
        public int CheapestBase
        {
            get
            {
                var round = Auction == null ? [] : RoundLots.Select(l => l.BasePrice).ToList();
                if (round.Count > 0) return round.Min();
                var prices = Tiers.Select(t => t.BasePrice).Append(Auction?.NoTierBasePrice ?? 100).ToList();
                return prices.Min();
            }
        }

        public Figures FiguresOf(int teamId)
        {
            var a = Rules;
            var bought = SoldLots.Where(l => l.SoldTeamId == teamId).ToList();
            var kept = Kept(teamId);
            var players = kept + bought.Count;
            var spent = kept * a.KeptPlayerPrice + bought.Sum(l => l.Price ?? 0);
            var left = a.Purse - spent;
            var needs = Math.Max(0, a.MinSquad - players);
            var byTier = bought
                .Select(l => PlayerOf(l)?.TierId)
                .Where(t => t != null)
                .GroupBy(t => t!.Value)
                .ToDictionary(g => g.Key, g => g.Count());
            return new Figures(players, spent, left, needs, players >= a.MaxSquad, AuctionMath.MaxBid(left, needs, CheapestBase), byTier);
        }

        /// <summary>Why this team can't bid <paramref name="amount"/> on this lot, in words; null if it can.</summary>
        public string? CantBid(int teamId, AuctionLot lot, int amount)
        {
            var f = FiguresOf(teamId);
            var name = TeamName(teamId);
            if (f.Full) return $"{name} has a full squad of {Rules.MaxSquad}.";
            if (TierOf(PlayerOf(lot)) is { MaxPerTeam: int max } tier && f.ByTier.GetValueOrDefault(tier.Id) >= max)
                return $"{name} already has {Plural(max, $"{tier.Name} player")}, the most allowed.";
            if (amount > f.MaxBid)
                return $"{name} can bid up to {Amount(f.MaxBid)}, to keep enough for a squad of {Rules.MinSquad}.";
            return null;
        }

        /// <summary>Players not sold and not still to come: offered and passed, returned, or added after their round.</summary>
        public List<PoolPlayer> UnsoldPlayers() => Pool
            .Where(p => !p.IsWithdrawn
                        && !Lots.Any(l => l.PoolPlayerId == p.Id && l.Status is AuctionLotStatus.Sold or AuctionLotStatus.Waiting or AuctionLotStatus.Open))
            .ToList();

        /// <summary>A player's base price now: their last lot's, or their tier's if never offered.</summary>
        public int BaseOf(PoolPlayer p) =>
            Lots.Where(l => l.PoolPlayerId == p.Id).OrderByDescending(l => l.Id).FirstOrDefault()?.BasePrice
            ?? TierOf(p)?.BasePrice ?? Auction?.NoTierBasePrice ?? 100;
    }

    private async Task<Book?> LoadAsync(int tournamentId, bool tracking)
    {
        var tournament = await _tournaments.FirstOrDefaultAsync(t => t.Id == tournamentId, tracking: false);
        if (tournament == null) return null;

        var auction = await _auctions.FirstOrDefaultAsync(a => a.TournamentId == tournamentId, tracking: tracking);
        // A lasting team's squad came with it (C-17, C-18): it never bids, and the purse sums are about the pool's teams only.
        var teams = await _teams.QueryAsync(q => q.Where(t => t.TournamentId == tournamentId && t.ClubId == null).OrderBy(t => t.Name)
            .Select(t => new TeamRow(t.Id, t.Name, t.DefaultCaptainPlayerId, t.Players.Count)), tracking: false);
        var captainIds = teams.Where(t => t.CaptainPlayerId != null).Select(t => t.CaptainPlayerId!.Value).ToList();
        var captains = captainIds.Count == 0
            ? new Dictionary<int, Captain>()
            : (await _players.QueryAsync(q => q.Where(p => captainIds.Contains(p.Id)).Select(p => new Captain(p.Id, p.UserId, p.Name)), tracking: false))
                .ToDictionary(c => c.PlayerId);
        var tiers = await _tiers.ListAsync(t => t.TournamentId == tournamentId, q => q.OrderBy(t => t.Order), tracking: false);
        var pool = await _pool.ListAsync(p => p.TournamentId == tournamentId, q => q.OrderBy(p => p.Order).ThenBy(p => p.Id), tracking: tracking);

        List<AuctionTeam> rows = [];
        List<AuctionLot> lots = [];
        List<AuctionBid> openBids = [];
        if (auction != null)
        {
            var auctionId = auction.Id;
            rows = await _auctionTeams.ListAsync(r => r.AuctionId == auctionId, tracking: tracking);
            // A cancelled attempt's lots stay as history (plan 9.7), outside the auction.
            lots = await _lots.ListAsync(l => l.AuctionId == auctionId && l.CancelledAt == null, q => q.OrderBy(l => l.Round).ThenBy(l => l.Order), tracking: tracking);
            if (auction.OpenLotId is int open)
                openBids = await _bids.ListAsync(b => b.LotId == open, q => q.OrderBy(b => b.Id), tracking: tracking);
        }

        var people = new[] { auction?.AuctioneerUserId }.Concat(rows.Select(r => r.BidderUserId)).OfType<int>().Distinct().ToList();
        var names = people.Count == 0
            ? new Dictionary<int, string>()
            : (await _users.QueryAsync(q => q.Where(u => people.Contains(u.UserId)).Select(u => new { u.UserId, u.Name }), tracking: false))
                .ToDictionary(u => u.UserId, u => u.Name);
        foreach (var c in captains.Values)
            if (c.UserId is int u) names.TryAdd(u, c.Name);

        var draw = await _draws.QueryFirstOrDefaultAsync(q => q.Where(d => d.TournamentId == tournamentId).Select(d => (DrawStatus?)d.Status), tracking: false);
        var method = await _pools.QueryFirstOrDefaultAsync(q => q.Where(p => p.TournamentId == tournamentId).Select(p => p.Method), tracking: false);

        return new Book
        {
            Draw = draw,
            Method = method,
            Tournament = tournament,
            Auction = auction,
            Teams = teams,
            Captains = captains,
            Rows = rows,
            Tiers = tiers,
            Pool = pool,
            Lots = lots,
            OpenBids = openBids,
            Roles = await _poolService.RolesAsync(tournament.Sport, pool),
            Names = names
        };
    }

    /// <summary>
    /// Runs a change to the auction with the tournament's lock held: loads it fresh, closes a lot whose
    /// clock has run out (so nobody acts on a sale that has already happened), applies the change,
    /// saves, and answers with the new state. A refused change still keeps a sale the clock made.
    /// </summary>
    private async Task<ServiceResult<AuctionStateDto>> ChangeAsync(int tournamentId, Func<Book, Task<ServiceError?>> change, bool nudge = false)
    {
        using (await _locks.EnterAsync(tournamentId))
        {
            var book = await LoadAsync(tournamentId, tracking: true);
            if (book == null) return NotFound("Tournament not found.");

            var closed = book.Auction != null && CloseIfDue(book);
            var error = await change(book);
            if (error is { } refused)
            {
                if (closed)
                {
                    await SendSaleNoticesAsync();
                    await SaveAndTellAsync(book, nudge: true);
                }
                return refused;
            }

            await SendSaleNoticesAsync();
            await SaveAndTellAsync(book, nudge || closed);
            return Success(Build(book));
        }
    }

    private async Task SaveAndTellAsync(Book book, bool nudge)
    {
        await _unitOfWork.SaveChangesAsync();
        _clockWatch.Track(book.Tournament.Id, book.OpenLot?.ClosesAt);
        if (nudge) await _live.TournamentChangedAsync(book.Tournament.Id);
    }

    // =================================================================
    // Reading
    // =================================================================

    public async Task<ServiceResult<AuctionStateDto>> GetAsync(int tournamentId)
    {
        // An overdue lot is closed by whoever looks first, so a sale is never lost to a restart.
        var auctionId = await _auctions.QueryFirstOrDefaultAsync(q => q.Where(a => a.TournamentId == tournamentId).Select(a => (int?)a.Id), tracking: false);
        if (auctionId is int id)
        {
            var now = Now;
            if (await _lots.AnyAsync(l => l.AuctionId == id && l.Status == AuctionLotStatus.Open && l.ClosesAt != null && l.ClosesAt <= now))
                await CloseDueAsync(tournamentId);
        }

        var book = await LoadAsync(tournamentId, tracking: false);
        return book == null ? NotFound("Tournament not found.") : Success(Build(book));
    }

    private AuctionStateDto Build(Book b)
    {
        var me = Viewer;
        var a = b.Auction;
        var owner = _context.IsOwner;
        var status = a == null ? "None" : a.Status.ToString();
        var settings = a ?? new Auction();
        var ladder = b.Ladder;
        var iBidFor = me == 0 ? null : b.TeamBiddenBy(me);
        var isAuctioneer = a?.AuctioneerUserId is int runner && runner == me;
        var lot = b.OpenLot;
        var paused = a?.Status == AuctionStatus.Paused;
        var top = lot == null ? null : b.Top(lot);
        var nextBid = lot == null ? 0 : top == null ? lot.BasePrice : ladder.Next(top.Amount);
        var live = a?.Status is AuctionStatus.Live or AuctionStatus.Paused;
        var afterRounds = a?.Status is AuctionStatus.RoundOver or AuctionStatus.Finished or AuctionStatus.Confirmed;

        var state = new AuctionStateDto
        {
            Status = status,
            Method = b.Method,
            ServerNow = Now,
            Round = a?.Round ?? 0,
            Settings = new AuctionSettingsDto
            {
                Unit = settings.Unit,
                Purse = settings.Purse,
                MinSquad = settings.MinSquad,
                MaxSquad = settings.MaxSquad,
                KeptPlayerPrice = settings.KeptPlayerPrice,
                NoTierBasePrice = settings.NoTierBasePrice,
                BidSteps = ladder.Steps.ToList(),
                ClockOn = settings.ClockOn,
                FirstBidSeconds = settings.FirstBidSeconds,
                NextBidSeconds = settings.NextBidSeconds,
                Order = settings.Order,
                UnsoldPlan = settings.UnsoldPlan
            },
            Auctioneer = a?.AuctioneerUserId is int au ? new PersonDto { UserId = au, Name = b.Names.GetValueOrDefault(au, "Someone") } : null,
            Me = new AuctionMeDto
            {
                IsOwner = owner,
                IsAuctioneer = isAuctioneer,
                BidderForTeamId = iBidFor,
                PoolPlayerId = b.Pool.FirstOrDefault(p => p.UserId != null && p.UserId == me)?.Id
            },
            Tiers = b.Tiers.Select(t => PoolService.TierDto(t, b.Pool.Count(p => p.TierId == t.Id && !p.IsWithdrawn))).ToList()
        };

        // Teams, with what each may do on the player under the hammer.
        foreach (var t in b.Teams)
        {
            var f = b.FiguresOf(t.Id);
            var bidder = b.BidderOf(t.Id);
            var captain = t.CaptainPlayerId is int cid ? b.Captains.GetValueOrDefault(cid) : null;
            string? cant = null;
            var canBid = false;
            if (lot != null)
            {
                cant = top?.TeamId == t.Id ? "Highest bid" : b.CantBid(t.Id, lot, nextBid);
                canBid = cant == null;
            }
            state.Teams.Add(new AuctionTeamDto
            {
                TeamId = t.Id,
                Name = t.Name,
                Bidder = bidder is int bu ? new PersonDto { UserId = bu, Name = b.Names.GetValueOrDefault(bu, "Someone") } : null,
                BidderIsCaptain = bidder != null && captain?.UserId == bidder,
                CaptainName = captain?.Name,
                CaptainPlayerId = captain?.PlayerId,
                CaptainWithoutAccount = captain != null && captain.UserId == null,
                Players = f.Players,
                Spent = f.Spent,
                PurseLeft = f.PurseLeft,
                Needs = f.Needs,
                IsFull = f.Full,
                MaxBid = f.MaxBid,
                CanBid = canBid,
                CantBidReason = cant,
                IsHighest = top?.TeamId == t.Id
            });
        }

        if (lot != null && b.PlayerOf(lot) is { } player)
        {
            var roundLots = b.RoundLots.OrderBy(l => l.Order).ToList();
            state.Lot = new AuctionLotDto
            {
                LotId = lot.Id,
                Player = PlayerDto(b, player),
                BasePrice = lot.BasePrice,
                CurrentBid = top?.Amount,
                CurrentTeamId = top?.TeamId,
                CurrentTeamName = top == null ? null : b.TeamName(top.TeamId),
                NextBid = nextBid,
                TwoStepsBid = ladder.Up(nextBid, 1),
                FiveStepsBid = ladder.Up(nextBid, 4),
                OpenedAt = lot.OpenedAt,
                ClosesAt = paused ? null : lot.ClosesAt,
                SecondsLeftWhenPaused = paused && lot.PausedMsLeft is int ms ? (int)Math.Ceiling(ms / 1000.0) : null,
                Number = roundLots.FindIndex(l => l.Id == lot.Id) + 1,
                OfTotal = roundLots.Count,
                Bids = b.Active(lot).OrderByDescending(x => x.Id).Take(RecentBids).Select(x => new AuctionBidDto
                {
                    BidId = x.Id, TeamId = x.TeamId, TeamName = b.TeamName(x.TeamId), Amount = x.Amount, At = x.At, FromRoom = x.FromRoom
                }).ToList()
            };
        }

        if (a != null)
        {
            state.UpNext = b.Waiting.Take(3).Select(l =>
            {
                var p = b.PlayerOf(l);
                return new AuctionUpNextDto { LotId = l.Id, Name = p?.Name ?? "", Role = p == null ? null : b.Roles.GetValueOrDefault(p.Id), TierName = b.TierOf(p)?.Name, BasePrice = l.BasePrice };
            }).ToList();

            var last = b.Lots.Where(l => l.ClosedAt != null && l.Status is AuctionLotStatus.Sold or AuctionLotStatus.Unsold && !l.Assigned)
                .OrderByDescending(l => l.ClosedAt).ThenByDescending(l => l.Id).FirstOrDefault();
            if (last != null && b.PlayerOf(last) is { } lp)
            {
                state.Last = new AuctionResultDto
                {
                    LotId = last.Id,
                    PlayerName = lp.Name,
                    Sold = last.Status == AuctionLotStatus.Sold,
                    TeamId = last.SoldTeamId,
                    TeamName = last.SoldTeamId is int st ? b.TeamName(st) : null,
                    Price = last.Price,
                    At = last.ClosedAt!.Value,
                    IsMe = lp.UserId != null && lp.UserId == me
                };
            }
        }

        var unsold = a == null ? [] : b.UnsoldPlayers().Where(p => b.Lots.Any(l => l.PoolPlayerId == p.Id) || afterRounds).ToList();
        state.Counts = new AuctionCountsDto
        {
            Players = b.Pool.Count(p => !p.IsWithdrawn),
            Sold = b.SoldLots.Count(),
            Unsold = unsold.Count,
            Waiting = a == null ? 0 : b.Waiting.Count()
        };

        if (afterRounds)
        {
            state.Sales = b.SoldLots.OrderBy(l => l.SoldTeamId).ThenByDescending(l => l.Price).Select(l =>
            {
                var p = b.PlayerOf(l);
                return new AuctionSaleDto
                {
                    LotId = l.Id, PoolPlayerId = l.PoolPlayerId, Name = p?.Name ?? "", Role = p == null ? null : b.Roles.GetValueOrDefault(p.Id),
                    TierName = b.TierOf(p)?.Name, TeamId = l.SoldTeamId ?? 0, Price = l.Price ?? 0, Round = l.Round, Assigned = l.Assigned, ByDraw = l.ByDraw,
                    MovedFromTeamName = l.MovedFromTeamId is int from ? b.TeamName(from) : null
                };
            }).ToList();
            state.Unsold = unsold.Select(p => new AuctionUnsoldDto
            {
                PoolPlayerId = p.Id, Name = p.Name, Role = b.Roles.GetValueOrDefault(p.Id), TierId = p.TierId, TierName = b.TierOf(p)?.Name, BasePrice = b.BaseOf(p)
            }).ToList();
        }

        if (b.Tournament.IsStarted || b.Tournament.IsClosed)
        {
            // Said first, as the draw does: the pool's sums don't matter once the teams are made.
            if (a == null || a.Status == AuctionStatus.Setup)
                state.StartProblem = "The tournament has started, so its teams are made. An auction has to happen before the start.";
        }
        else if (a == null || a.Status == AuctionStatus.Setup)
        {
            var (problem, warning) = PoolCheckOf(b);
            state.StartProblem = DrawProblem(b) ?? problem;
            state.StartWarning = warning;
        }

        state.Actions = ActionsFor(b, owner, isAuctioneer, iBidFor, lot, top, unsold.Count, state.StartProblem);
        return state;
    }

    private AuctionPlayerDto PlayerDto(Book b, PoolPlayer p) => new()
    {
        PoolPlayerId = p.Id,
        Name = p.Name,
        Role = b.Roles.GetValueOrDefault(p.Id),
        TierName = b.TierOf(p)?.Name,
        IsLinked = p.UserId != null,
        IsMe = p.UserId != null && p.UserId == Viewer
    };

    /// <summary>The teams are being (or have been) drawn instead: a tournament fills its teams one way (E-1).</summary>
    private static string? DrawProblem(Book b) => b.Draw switch
    {
        DrawStatus.Live => "The teams are being drawn, so there's no auction. Start the draw again from the beginning to choose the auction instead.",
        DrawStatus.Confirmed => "The teams were made by a draw, so there's no auction.",
        _ => b.Method == PoolMethod.Draw ? "The pool is set to fill the teams by a draw. Choose the auction on the pool screen first." : null
    };

    private static (string? Problem, string? Warning) PoolCheckOf(Book b)
    {
        var a = b.Rules;
        var players = b.Pool.Where(p => !p.IsWithdrawn).ToList();
        return AuctionMath.PoolCheck(
            players.Count,
            b.Teams.Select(t => (t.Name, t.Players)).ToList(),
            a.MinSquad,
            a.MaxSquad,
            b.Tiers.Select(t => (t.Name, players.Count(p => p.TierId == t.Id), t.MaxPerTeam)).ToList());
    }

    private AuctionActionsDto ActionsFor(Book b, bool owner, bool isAuctioneer, int? iBidFor, AuctionLot? lot, AuctionBid? top, int unsold, string? startProblem)
    {
        var s = b.Auction?.Status;
        var setup = s is null or AuctionStatus.Setup;
        var open = !b.Tournament.IsStarted && !b.Tournament.IsClosed;
        var running = s is AuctionStatus.Live or AuctionStatus.Paused;
        var afterStart = s is AuctionStatus.Live or AuctionStatus.Paused or AuctionStatus.RoundOver or AuctionStatus.Finished;
        var confirmProblem = s == AuctionStatus.Finished ? ConfirmProblem(b, unsold) : null;

        return new AuctionActionsDto
        {
            CanEditSettings = owner && open && setup,
            CanEditClock = owner && open && (setup || (s == AuctionStatus.Paused && isAuctioneer)),
            CanSetBidders = owner && open && (setup || (s == AuctionStatus.Paused && isAuctioneer)),
            CanStart = owner && open && setup && startProblem == null && iBidFor == null,
            CanOpenNext = isAuctioneer && s == AuctionStatus.Live && lot == null && b.Waiting.Any(),
            CanSell = isAuctioneer && running && lot != null && top != null,
            CanMarkUnsold = isAuctioneer && running && lot != null && top == null,
            CanUndoBid = isAuctioneer && running && lot != null && top != null,
            CanUndoSale = isAuctioneer && afterStart && b.SoldLots.Any(),
            CanPause = isAuctioneer && s == AuctionStatus.Live,
            CanResume = isAuctioneer && s == AuctionStatus.Paused,
            CanTakeOver = owner && !isAuctioneer && afterStart && iBidFor == null,
            CanStartRound = isAuctioneer && s == AuctionStatus.RoundOver && unsold > 0,
            CanFinish = isAuctioneer && (s == AuctionStatus.RoundOver || (s == AuctionStatus.Live && lot == null)),
            CanAssign = isAuctioneer && s == AuctionStatus.Finished && unsold > 0,
            CanConfirm = isAuctioneer && s == AuctionStatus.Finished && confirmProblem == null,
            CanMoveSales = isAuctioneer && s == AuctionStatus.Finished && b.SoldLots.Any(),
            CanFill = isAuctioneer && s == AuctionStatus.Finished && unsold > 0 && TeamsShort(b).Count > 0,
            CanCancel = isAuctioneer && afterStart,
            ConfirmProblem = confirmProblem
        };
    }

    /// <summary>A team short of its minimum while players are still unsold: give them players before making the squads.</summary>
    private static string? ConfirmProblem(Book b, int unsold)
    {
        if (unsold == 0) return null;
        foreach (var t in b.Teams)
        {
            var f = b.FiguresOf(t.Id);
            if (f.Needs > 0 && !f.Full)
                return $"{t.Name} need {Plural(f.Needs, "more player")}. Assign them some of the unsold players first.";
        }
        return null;
    }

    // =================================================================
    // Setup
    // =================================================================

    private async Task<Auction> EnsureAuctionAsync(Book b)
    {
        if (b.Auction != null) return b.Auction;
        var auction = new Auction { TournamentId = b.Tournament.Id, CreatedAt = Now };
        _auctions.Add(auction);
        await _unitOfWork.SaveChangesAsync(); // its id, for the team rows
        b.Auction = auction;
        return auction;
    }

    private static ServiceError? NotRunning(Book b)
    {
        if (b.Tournament.IsStarted) return Conflict("The tournament has started, so its auction is over.");
        return null;
    }

    public Task<ServiceResult<AuctionStateDto>> SettingsAsync(int tournamentId, AuctionSettingsRequest r) => ChangeAsync(tournamentId, async b =>
    {
        if (NotRunning(b) is { } closed) return closed;
        var status = b.Status;
        var clockOnly = r.Unit == null && r.Purse == null && r.MinSquad == null && r.MaxSquad == null && r.KeptPlayerPrice == null
                        && r.NoTierBasePrice == null && r.BidSteps == null && r.Order == null && r.UnsoldPlan == null;
        if (status == AuctionStatus.Paused)
        {
            if (!IsAuctioneer(b)) return Conflict(RunnerOnly(b));
            if (!clockOnly) return Conflict("Only the bid clock can change once the auction has started. To change the rest, cancel the auction and start again.");
        }
        else if (status != AuctionStatus.Setup)
            return Conflict(status == AuctionStatus.Live
                ? "Pause the auction to change the bid clock. The rest is fixed once it starts."
                : "The auction has started, so its rules are fixed.");

        var a = await EnsureAuctionAsync(b);
        if (r.Unit != null)
        {
            if (r.Unit is not ("pts" or "₹")) return BadRequest("The unit is \"pts\" or \"₹\".");
            a.Unit = r.Unit;
        }
        if (r.Purse is int purse)
        {
            if (purse is < 1 or > 100_000_000) return BadRequest("Give each team a purse of at least 1.");
            a.Purse = purse;
        }
        if (r.MinSquad is int min) a.MinSquad = min;
        if (r.MaxSquad is int max) a.MaxSquad = max;
        if (a.MinSquad is < 1 or > 50) return BadRequest("The minimum squad is between 1 and 50 players.");
        if (a.MaxSquad < a.MinSquad) return BadRequest("The maximum squad can't be smaller than the minimum.");
        if (a.MaxSquad > 60) return BadRequest("The maximum squad is at most 60 players.");
        if (r.KeptPlayerPrice is int kept)
        {
            if (kept < 0 || kept > a.Purse) return BadRequest("What a player already in a team costs must be between 0 and the purse.");
            a.KeptPlayerPrice = kept;
        }
        if (r.NoTierBasePrice is int noTier)
        {
            if (noTier < 0 || noTier > a.Purse) return BadRequest("The base price for players in no tier must be between 0 and the purse.");
            a.NoTierBasePrice = noTier;
        }
        if (r.BidSteps != null)
        {
            if (BidLadder.Problem(r.BidSteps) is { } stepProblem) return BadRequest(stepProblem);
            a.BidSteps = BidLadder.Format(r.BidSteps);
        }
        if (r.ClockOn is bool on) a.ClockOn = on;
        if (r.FirstBidSeconds is int first)
        {
            if (first is < 5 or > 120) return BadRequest("The clock for the first bid is between 5 and 120 seconds.");
            a.FirstBidSeconds = first;
        }
        if (r.NextBidSeconds is int next)
        {
            if (next is < 3 or > 60) return BadRequest("The clock after each bid is between 3 and 60 seconds.");
            a.NextBidSeconds = next;
        }
        if (r.Order is { } order) a.Order = order;
        if (r.UnsoldPlan is { } plan) a.UnsoldPlan = plan;

        // A pause that switched the clock on gives the player under the hammer a fresh one on resume.
        if (status == AuctionStatus.Paused && b.OpenLot is { } lot)
            lot.PausedMsLeft = a.ClockOn ? lot.PausedMsLeft ?? (b.Top(lot) == null ? a.FirstBidSeconds : a.NextBidSeconds) * 1000 : null;
        return null;
    });

    public Task<ServiceResult<AuctionStateDto>> BidderAsync(int tournamentId, AuctionBidderRequest r) => ChangeAsync(tournamentId, async b =>
    {
        if (NotRunning(b) is { } closed) return closed;
        if (b.Status == AuctionStatus.Paused)
        {
            if (!IsAuctioneer(b)) return Conflict(RunnerOnly(b));
        }
        else if (b.Status != AuctionStatus.Setup)
            return Conflict(b.Status == AuctionStatus.Live
                ? "Bidders are fixed while the auction runs. Pause it to replace a bidder whose phone is down."
                : "The auction has started, so its bidders are fixed.");

        var team = b.Teams.FirstOrDefault(t => t.Id == r.TeamId);
        if (team == null) return NotFound("That team isn't in this tournament.");

        int? userId = null;
        if (!r.None)
        {
            User? user;
            if (r.PlayerId is int playerId)
            {
                var place = await _players.FirstOrDefaultAsync(p => p.Id == playerId && p.TeamId == team.Id, tracking: false);
                if (place == null) return NotFound($"That player isn't in {team.Name}.");
                if (place.UserId == null) return BadRequest($"{place.Name} has no account, so can't bid from a phone. Pick someone with one, or record {team.Name}'s bids yourself.");
                user = await _users.GetByIdAsync(place.UserId.Value);
            }
            else
            {
                var (contact, problem) = Contacts.Parse(r.Contact);
                if (contact is not { } found) return BadRequest(problem!);
                user = await _links.AccountWithAsync(found);
                if (user == null) return BadRequest("No account uses that yet. A bidder needs an account: ask them to sign up first.");
            }
            if (user == null) return NotFound("Account not found.");
            if (b.Pool.Any(p => p.UserId == user.UserId))
                return Conflict($"{user.Name} is in the player pool, so can't bid.");
            if (b.Teams.FirstOrDefault(t => t.Id != team.Id && b.BidderOf(t.Id) == user.UserId) is { } other)
                return Conflict($"{user.Name} already bids for {other.Name}. One person bids for one team.");
            if (b.Auction?.AuctioneerUserId == user.UserId && b.Started)
                return Conflict($"{user.Name} is running the auction, so can't bid.");
            userId = user.UserId;
            b.Names[user.UserId] = user.Name;
        }

        var a = await EnsureAuctionAsync(b);
        var row = b.Rows.FirstOrDefault(x => x.TeamId == team.Id);
        if (row == null)
        {
            row = new AuctionTeam { AuctionId = a.Id, TournamentId = b.Tournament.Id, TeamId = team.Id, KeptPlayers = team.Players };
            _auctionTeams.Add(row);
            b.Rows.Add(row);
        }

        // Replaced mid-auction (plan 4.1): both people hear it, so the old one stops trying to bid.
        if (b.Started && row.BidderUserId != userId)
        {
            var tournament = ("TournamentName", b.Tournament.Name);
            if (row.BidderUserId is int old)
                _pendingNotices.Add((old, NotificationTemplateCode.AuctionBidderReplaced, Notifier.With(
                    ("TeamName", team.Name), tournament,
                    ("NewBidder", userId is int now ? b.Names.GetValueOrDefault(now, "someone else") : "the auctioneer, from the room")), b.Tournament.Id));
            if (userId is int fresh)
                _pendingNotices.Add((fresh, NotificationTemplateCode.AuctionBidderNow, Notifier.With(("TeamName", team.Name), tournament), b.Tournament.Id));
        }
        row.BidderUserId = userId;
        return null;
    });

    public Task<ServiceResult<AuctionStateDto>> StartAsync(int tournamentId) => ChangeAsync(tournamentId, async b =>
    {
        if (NotRunning(b) is { } closed) return closed;
        if (b.Status != AuctionStatus.Setup) return Conflict("The auction has already started.");
        var me = _currentUser.UserId;
        if (b.TeamBiddenBy(me) is int mine)
            return Conflict($"You bid for {b.TeamName(mine)}, so someone else must run the auction. Pick another bidder for {b.TeamName(mine)}, or ask another owner to start it.");
        if (DrawProblem(b) is { } drawn) return Conflict(drawn);
        if (PoolCheckOf(b).Problem is { } problem) return Conflict(problem);

        var a = await EnsureAuctionAsync(b);
        var now = Now;
        // Started, it stays an auction (E-1).
        var pool = await _pools.FirstOrDefaultAsync(p => p.TournamentId == b.Tournament.Id);
        if (pool == null) _pools.Add(new PlayerPool { TournamentId = b.Tournament.Id, Method = PoolMethod.Auction });
        else pool.Method = PoolMethod.Auction;

        // Bidders are fixed from here: every team gets its row (the captain by default), and its squad so far is counted once.
        foreach (var t in b.Teams)
        {
            var row = b.Rows.FirstOrDefault(r => r.TeamId == t.Id);
            if (row == null)
            {
                row = new AuctionTeam { AuctionId = a.Id, TournamentId = b.Tournament.Id, TeamId = t.Id, BidderUserId = b.BidderOf(t.Id) };
                _auctionTeams.Add(row);
                b.Rows.Add(row);
            }
            row.KeptPlayers = t.Players;
        }

        a.Status = AuctionStatus.Live;
        a.Round = 1;
        a.AuctioneerUserId = me;
        a.StartedAt = now;
        a.OpenLotId = null;
        AddRound(b, b.Pool.Where(p => !p.IsWithdrawn).ToList(), p => b.TierOf(p)?.BasePrice ?? a.NoTierBasePrice);

        // Everyone with a stake hears it has started: the players in the pool and the bidders.
        var name = b.Tournament.Name;
        var told = new HashSet<int> { me };
        foreach (var person in b.Pool.Where(p => p.UserId != null && !p.IsWithdrawn).Select(p => p.UserId!.Value)
                     .Concat(b.Rows.Where(r => r.BidderUserId != null).Select(r => r.BidderUserId!.Value)))
        {
            if (!told.Add(person)) continue;
            var team = b.Rows.FirstOrDefault(r => r.BidderUserId == person);
            await _notifier.NotifyAsync(person, NotificationTemplateCode.AuctionStarted, Notifier.With(
                    ("TournamentName", name),
                    ("YourPart", team != null ? $"You bid for {b.TeamName(team.TeamId)}." : "You're in the player pool: watch where you go.")),
                new NotificationLink(b.Tournament.Id));
        }
        return null;
    });

    /// <summary>The next round's lots for these players, in the auction's order, at the base price given for each.</summary>
    private void AddRound(Book b, List<PoolPlayer> players, Func<PoolPlayer, int> basePrice)
    {
        var a = b.Auction!;
        IEnumerable<PoolPlayer> ordered;
        if (a.Order == AuctionOrder.ListOrder)
            ordered = players.OrderBy(p => p.Order).ThenBy(p => p.Id);
        else
        {
            // Top tier first, "No tier" last, shuffled inside each so nobody can predict the order.
            ordered = players
                .GroupBy(p => b.TierOf(p)?.Order ?? int.MaxValue)
                .OrderBy(g => g.Key)
                .SelectMany(g => g.OrderBy(_ => Random.Shared.Next()));
        }

        var order = 0;
        foreach (var p in ordered)
        {
            var lot = new AuctionLot
            {
                AuctionId = a.Id, PoolPlayerId = p.Id, Round = a.Round, Order = ++order, BasePrice = basePrice(p), Status = AuctionLotStatus.Waiting
            };
            _lots.Add(lot);
            b.Lots.Add(lot);
        }
    }

    // =================================================================
    // Running a lot
    // =================================================================

    private bool IsAuctioneer(Book b) => b.Auction?.AuctioneerUserId is int runner && runner == Viewer;

    private static string RunnerOnly(Book b) =>
        b.Auction?.AuctioneerUserId is int runner
            ? $"{b.Names.GetValueOrDefault(runner, "Someone else")} is running the auction. Take over to run it yourself."
            : "Only the auctioneer can do that.";

    /// <summary>The auction must be running and the caller its auctioneer.</summary>
    private ServiceError? Runner(Book b, params AuctionStatus[] allowed)
    {
        if (NotRunning(b) is { } closed) return closed;
        if (b.Auction == null || b.Status == AuctionStatus.Setup) return Conflict("The auction hasn't started yet.");
        if (!IsAuctioneer(b)) return Forbidden(RunnerOnly(b));
        if (allowed.Length > 0 && !allowed.Contains(b.Status))
            return Conflict(b.Status switch
            {
                AuctionStatus.Paused => "The auction is paused. Resume it first.",
                AuctionStatus.RoundOver => "This round is over. Start another round or end the auction.",
                AuctionStatus.Finished => "The auction is over.",
                AuctionStatus.Confirmed => "The teams are confirmed, so the auction is closed.",
                _ => "That can't be done right now."
            });
        return null;
    }

    public Task<ServiceResult<AuctionStateDto>> NextAsync(int tournamentId, AuctionNextRequest r) => ChangeAsync(tournamentId, b =>
    {
        if (Runner(b, AuctionStatus.Live) is { } refused) return Task.FromResult<ServiceError?>(refused);
        if (b.OpenLot is { } open)
            return Task.FromResult<ServiceError?>(Conflict($"{b.PlayerOf(open)?.Name ?? "A player"} is still under the hammer. Sell them or mark them unsold first."));

        var lot = r.LotId is int id ? b.Waiting.FirstOrDefault(l => l.Id == id) : b.Waiting.FirstOrDefault();
        if (lot == null)
            return Task.FromResult<ServiceError?>(r.LotId != null
                ? Conflict("That player isn't waiting in this round any more.")
                : Conflict("Everyone in this round has been offered."));
        OpenLot(b, lot);
        return Task.FromResult<ServiceError?>(null);
    });

    private void OpenLot(Book b, AuctionLot lot)
    {
        var a = b.Auction!;
        var now = Now;
        lot.Status = AuctionLotStatus.Open;
        lot.OpenedAt = now;
        lot.ClosesAt = a.ClockOn ? now.AddSeconds(a.FirstBidSeconds) : null;
        lot.PausedMsLeft = null;
        a.OpenLotId = lot.Id;

        // Nobody can afford them (or every team is at its limit): unsold at once, no clock to wait out (4.9).
        if (b.Teams.All(t => b.CantBid(t.Id, lot, lot.BasePrice) != null))
            CloseLot(b, lot, now);
    }

    public Task<ServiceResult<AuctionStateDto>> BidAsync(int tournamentId, AuctionBidRequest r) => ChangeAsync(tournamentId, b =>
    {
        var a = b.Auction;
        if (NotRunning(b) is { } closed) return Task.FromResult<ServiceError?>(closed);
        if (a == null || a.Status is AuctionStatus.Setup) return Task.FromResult<ServiceError?>(Conflict("The auction hasn't started yet."));

        var team = b.Teams.FirstOrDefault(t => t.Id == r.TeamId);
        if (team == null) return Task.FromResult<ServiceError?>(NotFound("That team isn't in this auction."));

        var me = _currentUser.UserId;
        var isBidder = b.BidderOf(team.Id) == me;
        var fromRoom = !isBidder && IsAuctioneer(b);
        if (!isBidder && !fromRoom)
            return Task.FromResult<ServiceError?>(Forbidden($"Only {team.Name}'s bidder or the auctioneer can bid for {team.Name}."));

        var lot = b.OpenLot;
        if (lot == null)
        {
            // The clock may have sold the player a moment ago.
            var just = b.Lots.Where(l => l.ClosedAt != null && Now - l.ClosedAt < TimeSpan.FromSeconds(5) && l.Status is AuctionLotStatus.Sold or AuctionLotStatus.Unsold)
                .OrderByDescending(l => l.ClosedAt).FirstOrDefault();
            return Task.FromResult<ServiceError?>(Conflict(just == null
                ? "Nobody is under the hammer right now."
                : just.Status == AuctionLotStatus.Sold
                    ? $"Too late: {b.PlayerOf(just)?.Name} went to {b.TeamName(just.SoldTeamId ?? 0)} for {Amount(just.Price ?? 0)}."
                    : $"Too late: {b.PlayerOf(just)?.Name} went unsold."));
        }
        if (a.Status == AuctionStatus.Paused && !fromRoom)
            return Task.FromResult<ServiceError?>(Conflict("The auction is paused. Bidding opens again when it resumes."));

        var top = b.Top(lot);
        if (top?.TeamId == team.Id)
            return Task.FromResult<ServiceError?>(Conflict(isBidder ? "You're the highest bid already." : $"{team.Name} already has the highest bid."));

        var next = top == null ? lot.BasePrice : b.Ladder.Next(top.Amount);
        if (r.Amount < next)
            return Task.FromResult<ServiceError?>(Conflict(top == null
                ? $"The first bid is the base price, {Amount(next)}."
                : $"{b.TeamName(top.TeamId)} bid {Amount(top.Amount)} first. The next bid is {Amount(next)}."));
        if (!b.Ladder.IsRung(next, r.Amount))
            return Task.FromResult<ServiceError?>(BadRequest($"Bids go up in steps: the next is {Amount(next)}."));
        if (b.CantBid(team.Id, lot, r.Amount) is { } cant)
            return Task.FromResult<ServiceError?>(Conflict(cant));

        var now = Now;
        var bid = new AuctionBid { LotId = lot.Id, TeamId = team.Id, Amount = r.Amount, ByUserId = me, FromRoom = fromRoom, At = now };
        _bids.Add(bid);
        b.OpenBids.Add(bid);

        // Every bid resets the clock, so nobody can snipe in the last second (4.4).
        if (a.ClockOn)
        {
            if (a.Status == AuctionStatus.Paused) lot.PausedMsLeft = a.NextBidSeconds * 1000;
            else lot.ClosesAt = now.AddSeconds(a.NextBidSeconds);
        }
        return Task.FromResult<ServiceError?>(null);
    }, nudge: true); // bidders bid with View, which the access filter doesn't nudge for

    public Task<ServiceResult<AuctionStateDto>> SellAsync(int tournamentId) => ChangeAsync(tournamentId, b =>
    {
        if (Runner(b, AuctionStatus.Live, AuctionStatus.Paused) is { } refused) return Task.FromResult<ServiceError?>(refused);
        if (b.OpenLot is not { } lot) return Task.FromResult<ServiceError?>(Conflict("Nobody is under the hammer right now."));
        if (b.Top(lot) == null) return Task.FromResult<ServiceError?>(Conflict("Nobody has bid yet. Mark them unsold instead."));
        CloseLot(b, lot, Now);
        return Task.FromResult<ServiceError?>(null);
    });

    public Task<ServiceResult<AuctionStateDto>> UnsoldAsync(int tournamentId) => ChangeAsync(tournamentId, b =>
    {
        if (Runner(b, AuctionStatus.Live, AuctionStatus.Paused) is { } refused) return Task.FromResult<ServiceError?>(refused);
        if (b.OpenLot is not { } lot) return Task.FromResult<ServiceError?>(Conflict("Nobody is under the hammer right now."));
        if (b.Top(lot) is { } top)
            return Task.FromResult<ServiceError?>(Conflict($"{b.TeamName(top.TeamId)} has bid {Amount(top.Amount)}. Sell, or undo the bids first."));
        CloseLot(b, lot, Now);
        return Task.FromResult<ServiceError?>(null);
    });

    public Task<ServiceResult<AuctionStateDto>> UndoBidAsync(int tournamentId) => ChangeAsync(tournamentId, b =>
    {
        if (Runner(b, AuctionStatus.Live, AuctionStatus.Paused) is { } refused) return Task.FromResult<ServiceError?>(refused);
        if (b.OpenLot is not { } lot) return Task.FromResult<ServiceError?>(Conflict("Nobody is under the hammer right now."));
        var last = b.Active(lot).OrderByDescending(x => x.Id).FirstOrDefault();
        if (last == null) return Task.FromResult<ServiceError?>(Conflict("There's no bid to undo."));
        last.IsUndone = true;

        var a = b.Auction!;
        if (a.ClockOn)
        {
            var seconds = b.Top(lot) == null ? a.FirstBidSeconds : a.NextBidSeconds;
            if (a.Status == AuctionStatus.Paused) lot.PausedMsLeft = seconds * 1000;
            else lot.ClosesAt = Now.AddSeconds(seconds);
        }
        return Task.FromResult<ServiceError?>(null);
    });

    /// <summary>
    /// Ends a lot: sold to the highest bid standing, or unsold without one. Then, if the round has
    /// nobody left to offer, it's over; if no team can take another player, so is the auction.
    /// </summary>
    private void CloseLot(Book b, AuctionLot lot, DateTime at)
    {
        var a = b.Auction!;
        var top = b.Top(lot);
        lot.ClosedAt = at;
        lot.ClosesAt = null;
        lot.PausedMsLeft = null;
        if (a.OpenLotId == lot.Id) a.OpenLotId = null;

        if (top != null)
        {
            lot.Status = AuctionLotStatus.Sold;
            lot.SoldTeamId = top.TeamId;
            lot.Price = top.Amount;
            if (b.PlayerOf(lot) is { UserId: int person })
                _pendingNotices.Add((person, NotificationTemplateCode.AuctionBought, Notifier.With(
                    ("TeamName", b.TeamName(top.TeamId)), ("Price", $"{Amount(top.Amount)} {a.Unit}"), ("TournamentName", b.Tournament.Name)), b.Tournament.Id));
        }
        else
            lot.Status = AuctionLotStatus.Unsold;

        AfterLot(b, at);
    }

    /// <summary>Notices made inside the lock, sent once the change is saved.</summary>
    private readonly List<(int UserId, NotificationTemplateCode Code, IReadOnlyDictionary<string, string> Values, int TournamentId)> _pendingNotices = [];

    private void AfterLot(Book b, DateTime at)
    {
        var a = b.Auction!;
        if (b.Teams.All(t => b.FiguresOf(t.Id).Full))
        {
            // Nobody has room: the rest go unsold and the auction is over (4.6).
            foreach (var waiting in b.Waiting.ToList())
            {
                waiting.Status = AuctionLotStatus.Unsold;
                waiting.ClosedAt = at;
            }
            a.Status = AuctionStatus.Finished;
            a.FinishedAt = at;
            return;
        }
        if (b.Waiting.Any() || b.OpenLot != null) return;
        if (b.UnsoldPlayers().Count == 0)
        {
            a.Status = AuctionStatus.Finished;
            a.FinishedAt = at;
        }
        else
            a.Status = AuctionStatus.RoundOver;
    }

    /// <summary>Closes the lot under the hammer if its clock has run out. True if it did.</summary>
    private bool CloseIfDue(Book b)
    {
        if (b.Auction is not { Status: AuctionStatus.Live } || b.OpenLot is not { ClosesAt: { } closesAt } lot || closesAt > Now) return false;
        CloseLot(b, lot, closesAt);
        return true;
    }

    private async Task SendSaleNoticesAsync()
    {
        foreach (var (userId, code, values, tournamentId) in _pendingNotices)
            await _notifier.NotifyAsync(userId, code, values, new NotificationLink(tournamentId));
        _pendingNotices.Clear();
    }

    public async Task CloseDueAsync(int tournamentId)
    {
        using (await _locks.EnterAsync(tournamentId))
        {
            var book = await LoadAsync(tournamentId, tracking: true);
            if (book?.Auction == null) return;
            if (CloseIfDue(book))
            {
                await SendSaleNoticesAsync();
                await SaveAndTellAsync(book, nudge: true);
            }
            else
                _clockWatch.Track(tournamentId, book.OpenLot?.ClosesAt);
        }
    }

    public Task<ServiceResult<AuctionStateDto>> PauseAsync(int tournamentId) => ChangeAsync(tournamentId, b =>
    {
        if (Runner(b, AuctionStatus.Live) is { } refused) return Task.FromResult<ServiceError?>(refused);
        var a = b.Auction!;
        a.Status = AuctionStatus.Paused;
        if (b.OpenLot is { } lot && lot.ClosesAt is { } closesAt)
        {
            lot.PausedMsLeft = Math.Max(0, (int)(closesAt - Now).TotalMilliseconds);
            lot.ClosesAt = null;
        }
        return Task.FromResult<ServiceError?>(null);
    });

    public Task<ServiceResult<AuctionStateDto>> ResumeAsync(int tournamentId) => ChangeAsync(tournamentId, b =>
    {
        if (Runner(b, AuctionStatus.Paused) is { } refused) return Task.FromResult<ServiceError?>(refused);
        var a = b.Auction!;
        a.Status = AuctionStatus.Live;
        if (b.OpenLot is { } lot)
        {
            lot.ClosesAt = a.ClockOn ? Now.AddMilliseconds(Math.Max(MinResumeMs, lot.PausedMsLeft ?? a.NextBidSeconds * 1000)) : null;
            lot.PausedMsLeft = null;
        }
        return Task.FromResult<ServiceError?>(null);
    });

    public Task<ServiceResult<AuctionStateDto>> TakeOverAsync(int tournamentId) => ChangeAsync(tournamentId, b =>
    {
        if (NotRunning(b) is { } closed) return Task.FromResult<ServiceError?>(closed);
        if (b.Auction == null || b.Status is AuctionStatus.Setup or AuctionStatus.Confirmed)
            return Task.FromResult<ServiceError?>(Conflict("There's no auction running to take over."));
        if (IsAuctioneer(b)) return Task.FromResult<ServiceError?>(Conflict("You're already running the auction."));
        if (b.TeamBiddenBy(_currentUser.UserId) is int mine)
            return Task.FromResult<ServiceError?>(Conflict($"You bid for {b.TeamName(mine)}, so you can't run the auction."));
        b.Auction.AuctioneerUserId = _currentUser.UserId;
        b.Names[_currentUser.UserId] = _currentUser.Name;
        return Task.FromResult<ServiceError?>(null);
    });

    // =================================================================
    // Rounds, the end, and confirming
    // =================================================================

    public Task<ServiceResult<AuctionStateDto>> UndoSaleAsync(int tournamentId, AuctionLotRequest r) => ChangeAsync(tournamentId, b =>
    {
        if (Runner(b, AuctionStatus.Live, AuctionStatus.Paused, AuctionStatus.RoundOver, AuctionStatus.Finished) is { } refused)
            return Task.FromResult<ServiceError?>(refused);
        var lot = b.Lots.FirstOrDefault(l => l.Id == r.LotId && l.Status == AuctionLotStatus.Sold);
        if (lot == null) return Task.FromResult<ServiceError?>(NotFound("That sale isn't there any more."));

        var a = b.Auction!;
        lot.Status = AuctionLotStatus.Returned;

        // Mid-round, the player goes back in line at the end of this round; otherwise they're unsold again.
        if (a.Status is AuctionStatus.Live or AuctionStatus.Paused)
        {
            var again = new AuctionLot
            {
                AuctionId = a.Id, PoolPlayerId = lot.PoolPlayerId, Round = a.Round, BasePrice = lot.BasePrice, Status = AuctionLotStatus.Waiting,
                Order = b.Lots.Where(l => l.Round == a.Round).Select(l => l.Order).DefaultIfEmpty(0).Max() + 1
            };
            _lots.Add(again);
            b.Lots.Add(again);
        }
        return Task.FromResult<ServiceError?>(null);
    });

    public Task<ServiceResult<AuctionStateDto>> StartRoundAsync(int tournamentId, AuctionRoundRequest r) => ChangeAsync(tournamentId, b =>
    {
        if (Runner(b, AuctionStatus.RoundOver) is { } refused) return Task.FromResult<ServiceError?>(refused);
        var players = b.UnsoldPlayers();
        if (players.Count == 0) return Task.FromResult<ServiceError?>(Conflict("Nobody is left unsold."));

        // A later round keeps each tier's base price or lowers it, never raises it (D-7).
        var asked = new Dictionary<int, int>(); // tier id, or 0 for no tier
        foreach (var price in r.Prices)
        {
            if (price.Price < 0) return Task.FromResult<ServiceError?>(BadRequest("A base price can't be below 0."));
            var key = price.TierId ?? 0;
            var inTier = players.Where(p => (p.TierId ?? 0) == key).ToList();
            if (inTier.Count == 0) continue;
            var highest = inTier.Max(b.BaseOf);
            if (price.Price > highest)
            {
                var tierName = price.TierId is int t ? b.Tiers.FirstOrDefault(x => x.Id == t)?.Name ?? "That tier" : "No tier";
                return Task.FromResult<ServiceError?>(BadRequest(
                    $"{tierName}: the base price was {Amount(highest)}. A later round can keep it or lower it, not raise it."));
            }
            asked[key] = price.Price;
        }

        var a = b.Auction!;
        a.Round++;
        a.Status = AuctionStatus.Live;
        a.OpenLotId = null;
        AddRound(b, players, p => asked.TryGetValue(p.TierId ?? 0, out var lower) ? Math.Min(lower, b.BaseOf(p)) : b.BaseOf(p));
        return Task.FromResult<ServiceError?>(null);
    });

    public Task<ServiceResult<AuctionStateDto>> FinishAsync(int tournamentId) => ChangeAsync(tournamentId, b =>
    {
        if (Runner(b, AuctionStatus.Live, AuctionStatus.RoundOver) is { } refused) return Task.FromResult<ServiceError?>(refused);
        if (b.OpenLot is { } open)
            return Task.FromResult<ServiceError?>(Conflict($"{b.PlayerOf(open)?.Name ?? "A player"} is still under the hammer. Sell them or mark them unsold first."));

        var a = b.Auction!;
        var now = Now;
        // Players still waiting were never offered this round: they're unsold, and can be assigned by hand.
        foreach (var waiting in b.Waiting.ToList())
        {
            waiting.Status = AuctionLotStatus.Returned;
            waiting.ClosedAt = now;
        }
        a.Status = AuctionStatus.Finished;
        a.FinishedAt = now;
        return Task.FromResult<ServiceError?>(null);
    });

    public Task<ServiceResult<AuctionStateDto>> AssignAsync(int tournamentId, AuctionAssignRequest r) => ChangeAsync(tournamentId, b =>
    {
        if (Runner(b, AuctionStatus.Finished) is { } refused) return Task.FromResult<ServiceError?>(refused);
        var player = b.UnsoldPlayers().FirstOrDefault(p => p.Id == r.PoolPlayerId);
        if (player == null) return Task.FromResult<ServiceError?>(Conflict("That player isn't unsold any more."));
        var team = b.Teams.FirstOrDefault(t => t.Id == r.TeamId);
        if (team == null) return Task.FromResult<ServiceError?>(NotFound("That team isn't in this auction."));

        var basePrice = b.BaseOf(player);
        var f = b.FiguresOf(team.Id);
        if (r.Price < 0) return Task.FromResult<ServiceError?>(BadRequest("A price can't be below 0."));
        if (r.Price > basePrice)
            return Task.FromResult<ServiceError?>(BadRequest($"{player.Name}'s base price is {Amount(basePrice)}. Assign them at that price or lower."));
        if (f.Full) return Task.FromResult<ServiceError?>(Conflict($"{team.Name} has a full squad of {b.Auction!.MaxSquad}."));
        if (r.Price > f.PurseLeft)
            return Task.FromResult<ServiceError?>(Conflict($"{team.Name} has {Amount(f.PurseLeft)} {b.Auction!.Unit} left. Lower the price to {Amount(f.PurseLeft)} or less."));
        if (b.TierOf(player) is { MaxPerTeam: int max } tier && f.ByTier.GetValueOrDefault(tier.Id) >= max)
            return Task.FromResult<ServiceError?>(Conflict($"{team.Name} already has {Plural(max, $"{tier.Name} player")}, the most allowed."));

        var a = b.Auction!;
        var lot = new AuctionLot
        {
            AuctionId = a.Id, PoolPlayerId = player.Id, Round = a.Round, BasePrice = basePrice, Status = AuctionLotStatus.Sold,
            Order = b.Lots.Where(l => l.Round == a.Round).Select(l => l.Order).DefaultIfEmpty(0).Max() + 1,
            SoldTeamId = team.Id, Price = r.Price, Assigned = true, OpenedAt = Now, ClosedAt = Now
        };
        _lots.Add(lot);
        b.Lots.Add(lot);
        return Task.FromResult<ServiceError?>(null);
    });

    public Task<ServiceResult<AuctionStateDto>> ConfirmAsync(int tournamentId) => ChangeAsync(tournamentId, async b =>
    {
        if (Runner(b, AuctionStatus.Finished) is { } refused) return refused;
        if (ConfirmProblem(b, b.UnsoldPlayers().Count) is { } problem) return Conflict(problem);

        // Every sale becomes a squad place, linked to the same account (or waiting on the same email or number).
        var a = b.Auction!;
        a.Status = AuctionStatus.Confirmed;
        a.ConfirmedAt = Now;
        a.OpenLotId = null;
        var places = b.SoldLots.OrderBy(l => l.SoldTeamId).ThenByDescending(l => l.Price)
            .Select(l => (Pool: b.PlayerOf(l), Team: l.SoldTeamId))
            .Where(x => x.Pool != null && x.Team != null)
            .Select(x => (x.Pool!, x.Team!.Value))
            .ToList();
        return await _squads.MakeAsync(b.Tournament, places) is { } problem2 ? Conflict(problem2) : null;
    });

    // =================================================================
    // Moving and swapping after the auction (E-4)
    // =================================================================

    /// <summary>
    /// A bought player moves to another team before the teams are confirmed: their price goes with
    /// them (the old team gets it back, the new one pays it), or less, down to 0, to fit the new
    /// team's purse. The new team must have room and stay within any per-tier limit.
    /// </summary>
    public Task<ServiceResult<AuctionStateDto>> MoveAsync(int tournamentId, AuctionMoveRequest r) => ChangeAsync(tournamentId, b =>
    {
        if (Runner(b, AuctionStatus.Finished) is { } refused) return Task.FromResult<ServiceError?>(refused);
        var lot = b.Lots.FirstOrDefault(l => l.Id == r.LotId && l.Status == AuctionLotStatus.Sold);
        if (lot == null) return Task.FromResult<ServiceError?>(NotFound("That sale isn't there any more."));
        var team = b.Teams.FirstOrDefault(t => t.Id == r.TeamId);
        if (team == null) return Task.FromResult<ServiceError?>(NotFound("That team isn't in this auction."));
        var name = b.PlayerOf(lot)?.Name ?? "That player";
        if (lot.SoldTeamId == team.Id) return Task.FromResult<ServiceError?>(Conflict($"{name} is already in {team.Name}."));

        var paid = lot.Price ?? 0;
        var price = r.Price ?? paid;
        var f = b.FiguresOf(team.Id);
        if (price < 0) return Task.FromResult<ServiceError?>(BadRequest("A price can't be below 0."));
        if (price > paid) return Task.FromResult<ServiceError?>(BadRequest($"{name} went for {Amount(paid)}. Move them at that price or lower."));
        if (f.Full) return Task.FromResult<ServiceError?>(Conflict($"{team.Name} has a full squad of {b.Rules.MaxSquad}."));
        if (price > f.PurseLeft)
            return Task.FromResult<ServiceError?>(Conflict($"{team.Name} has {Amount(f.PurseLeft)} {b.Rules.Unit} left. Lower the price to {Amount(f.PurseLeft)} or less."));
        if (b.TierOf(b.PlayerOf(lot)) is { MaxPerTeam: int max } tier && f.ByTier.GetValueOrDefault(tier.Id) >= max)
            return Task.FromResult<ServiceError?>(Conflict($"{team.Name} already has {Plural(max, $"{tier.Name} player")}, the most allowed."));

        MoveLot(lot, team.Id);
        lot.Price = price;
        return Task.FromResult<ServiceError?>(null);
    });

    /// <summary>Two bought players change teams, each keeping their price; both purses must stay at 0 or above.</summary>
    public Task<ServiceResult<AuctionStateDto>> SwapAsync(int tournamentId, AuctionSwapRequest r) => ChangeAsync(tournamentId, b =>
    {
        if (Runner(b, AuctionStatus.Finished) is { } refused) return Task.FromResult<ServiceError?>(refused);
        var one = b.Lots.FirstOrDefault(l => l.Id == r.LotId && l.Status == AuctionLotStatus.Sold);
        var other = b.Lots.FirstOrDefault(l => l.Id == r.OtherLotId && l.Status == AuctionLotStatus.Sold);
        if (one == null || other == null) return Task.FromResult<ServiceError?>(NotFound("That sale isn't there any more."));
        if (one.SoldTeamId == other.SoldTeamId) return Task.FromResult<ServiceError?>(Conflict("Those two are in the same team. Pick a player from another team."));

        var (teamOne, teamOther) = (one.SoldTeamId!.Value, other.SoldTeamId!.Value);
        MoveLot(one, teamOther);
        MoveLot(other, teamOne);
        foreach (var t in new[] { teamOne, teamOther })
        {
            var f = b.FiguresOf(t);
            string? problem = f.PurseLeft < 0 ? $"{b.TeamName(t)} would be {Amount(-f.PurseLeft)} {b.Rules.Unit} over its purse." : null;
            foreach (var tier in b.Tiers.Where(x => x.MaxPerTeam != null && problem == null))
                if (f.ByTier.GetValueOrDefault(tier.Id) > tier.MaxPerTeam)
                    problem = $"{b.TeamName(t)} would have more than {Plural(tier.MaxPerTeam!.Value, $"{tier.Name} player")}.";
            if (problem != null)
            {
                MoveLot(one, teamOne);
                MoveLot(other, teamOther);
                return Task.FromResult<ServiceError?>(Conflict(problem));
            }
        }
        return Task.FromResult<ServiceError?>(null);
    });

    // =================================================================
    // Fill the gaps by draw (plan 9.7)
    // =================================================================

    private sealed record Deal(PoolPlayer Player, int TeamId, int Price);

    private static List<int> TeamsShort(Book b) => b.Teams.Where(t => b.FiguresOf(t.Id) is { Needs: > 0, Full: false }).Select(t => t.Id).ToList();

    private static int Lucky() => System.Security.Cryptography.RandomNumberGenerator.GetInt32(int.MaxValue);

    /// <summary>
    /// Deals the unsold players to the teams short of the minimum squad by the draw's rule (9.2): the
    /// next team round the table among those with the fewest players, a player from the top tier left
    /// (random inside it), each at their base price or what the team has left, whichever is lower. A
    /// team stops at the minimum; one that can take nobody more (tier limits) drops out. Random only
    /// when it's done; the preview deals in a fixed order, which gives the same counts.
    /// </summary>
    private static List<Deal> PlanFill(Book b, bool random)
    {
        var order = TeamsShort(b);
        if (random) order = order.OrderBy(_ => Lucky()).ToList();
        var figures = order.ToDictionary(t => t, b.FiguresOf);
        var players = order.ToDictionary(t => t, t => figures[t].Players);
        var needs = order.ToDictionary(t => t, t => figures[t].Needs);
        var purse = order.ToDictionary(t => t, t => figures[t].PurseLeft);
        var byTier = order.ToDictionary(t => t, t => new Dictionary<int, int>(figures[t].ByTier));

        var pot = b.UnsoldPlayers()
            .GroupBy(p => b.TierOf(p)?.Order ?? int.MaxValue)
            .OrderBy(g => g.Key)
            .SelectMany(g => random ? g.OrderBy(_ => Lucky()) : g.OrderBy(p => p.Order).ThenBy(p => p.Id))
            .ToList();

        var deals = new List<Deal>();
        int? last = null;
        while (order.Count > 0 && pot.Count > 0)
        {
            var team = DrawRules.NextTeam(order, players, last);
            var player = pot.FirstOrDefault(p => b.TierOf(p) is not { MaxPerTeam: int max } tier || byTier[team].GetValueOrDefault(tier.Id) < max);
            if (player == null)
            {
                order.Remove(team);
                continue;
            }
            var price = Math.Max(0, Math.Min(b.BaseOf(player), purse[team]));
            deals.Add(new Deal(player, team, price));
            pot.Remove(player);
            purse[team] -= price;
            players[team]++;
            if (b.TierOf(player) is { } tier) byTier[team][tier.Id] = byTier[team].GetValueOrDefault(tier.Id) + 1;
            if (--needs[team] == 0) order.Remove(team);
            last = team;
        }
        return deals;
    }

    private static string? CantFill(Book b)
    {
        if (b.UnsoldPlayers().Count == 0) return "Nobody is left unsold.";
        if (TeamsShort(b).Count == 0) return $"Every team has its minimum squad of {b.Rules.MinSquad}. Assign anyone else by hand.";
        return null;
    }

    public async Task<ServiceResult<AuctionFillPreviewDto>> FillPreviewAsync(int tournamentId)
    {
        var b = await LoadAsync(tournamentId, tracking: false);
        if (b == null) return NotFound("Tournament not found.");
        if (b.Auction?.Status != AuctionStatus.Finished) return Conflict("The gaps can be filled once the auction is over.");
        if (CantFill(b) is { } cant) return Conflict(cant);

        var deals = PlanFill(b, random: false);
        var unsold = b.UnsoldPlayers().Count;
        var teams = TeamsShort(b).Select(id =>
        {
            var f = b.FiguresOf(id);
            return new AuctionFillTeamDto { TeamId = id, Name = b.TeamName(id), Has = f.Players, Gets = deals.Count(d => d.TeamId == id), PurseLeft = f.PurseLeft };
        }).OrderByDescending(t => t.Gets).ThenBy(t => t.Name).ToList();

        var min = b.Rules.MinSquad;
        var gets = teams.Where(t => t.Gets > 0).Select(t => $"{t.Name} get {t.Gets}").ToList();
        var left = unsold - deals.Count;
        var notes = new List<string> { "Each goes at their base price, or what the team has left if that's less. Who goes where is drawn when you fill." };
        if (left > 0) notes.Add($"{Plural(left, "player")} stay{(left == 1 ? "s" : "")} unsold.");
        var stuck = teams.Where(t => t.Has + t.Gets < min).Select(t => t.Name).ToList();
        if (stuck.Count > 0) notes.Add($"{string.Join(" and ", stuck)} still won't reach {min}.");
        return Success(new AuctionFillPreviewDto
        {
            Line = $"{Plural(unsold, "unsold player")} for {Plural(teams.Count, "team")} short of {min}" + (gets.Count > 0 ? $": {string.Join(", ", gets)}." : ". Nobody fits."),
            Note = string.Join(" ", notes),
            Teams = teams,
            Left = left
        });
    }

    public Task<ServiceResult<AuctionStateDto>> FillAsync(int tournamentId) => ChangeAsync(tournamentId, b =>
    {
        if (Runner(b, AuctionStatus.Finished) is { } refused) return Task.FromResult<ServiceError?>(refused);
        if (CantFill(b) is { } cant) return Task.FromResult<ServiceError?>(Conflict(cant));

        var a = b.Auction!;
        var now = Now;
        var deals = PlanFill(b, random: true);
        if (deals.Count == 0)
            return Task.FromResult<ServiceError?>(Conflict("None of the unsold players can join the teams that are short: their tier limits are reached. Assign them by hand."));
        var order = b.Lots.Where(l => l.Round == a.Round).Select(l => l.Order).DefaultIfEmpty(0).Max();
        foreach (var d in deals)
        {
            var lot = new AuctionLot
            {
                AuctionId = a.Id, PoolPlayerId = d.Player.Id, Round = a.Round, BasePrice = b.BaseOf(d.Player), Status = AuctionLotStatus.Sold,
                Order = ++order, SoldTeamId = d.TeamId, Price = d.Price, Assigned = true, ByDraw = true, OpenedAt = now, ClosedAt = now
            };
            _lots.Add(lot);
            b.Lots.Add(lot);
        }
        return Task.FromResult<ServiceError?>(null);
    });

    /// <summary>Changes a sale's team, remembering the team that bought it (cleared if it goes back).</summary>
    private static void MoveLot(AuctionLot lot, int teamId)
    {
        var bought = lot.MovedFromTeamId ?? lot.SoldTeamId;
        lot.SoldTeamId = teamId;
        lot.MovedFromTeamId = bought == teamId ? null : bought;
    }

    public Task<ServiceResult<AuctionStateDto>> CancelAsync(int tournamentId) => ChangeAsync(tournamentId, b =>
    {
        if (Runner(b, AuctionStatus.Live, AuctionStatus.Paused, AuctionStatus.RoundOver, AuctionStatus.Finished) is { } refused)
            return Task.FromResult<ServiceError?>(refused);

        // Everyone goes back to the pool; the rules and bidders stay for the next attempt. The lots
        // and their bids stay as history (plan 9.7), marked cancelled so nothing reads them again.
        var a = b.Auction!;
        var now = Now;
        foreach (var lot in b.Lots)
        {
            lot.CancelledAt = now;
            lot.ClosesAt = null;
            lot.PausedMsLeft = null;
            if (lot.Status is AuctionLotStatus.Waiting or AuctionLotStatus.Open) lot.Status = AuctionLotStatus.Returned;
        }
        b.Lots.Clear();
        b.OpenBids.Clear();
        a.Status = AuctionStatus.Setup;
        a.Round = 0;
        a.OpenLotId = null;
        a.AuctioneerUserId = null;
        a.StartedAt = null;
        a.FinishedAt = null;
        return Task.FromResult<ServiceError?>(null);
    });

    public async Task<string?> StartProblemAsync(int tournamentId)
    {
        var status = await _auctions.QueryFirstOrDefaultAsync(q => q.Where(a => a.TournamentId == tournamentId).Select(a => (AuctionStatus?)a.Status), tracking: false);
        if (status is AuctionStatus.Live or AuctionStatus.Paused or AuctionStatus.RoundOver or AuctionStatus.Finished)
            return "The player auction isn't finished. Finish it and confirm the teams before starting the tournament.";
        if (status == AuctionStatus.Confirmed) return null;

        // A pool whose players were never given teams: starting closes the pool for good, so an organiser
        // who meant to run the auction or draw would lose it (the user hit this, 2026-11-18).
        var draw = await _draws.QueryFirstOrDefaultAsync(q => q.Where(d => d.TournamentId == tournamentId).Select(d => (DrawStatus?)d.Status), tracking: false);
        if (draw is DrawStatus.Confirmed or DrawStatus.Live) return null; // a live draw: the draw's own check says so
        var waiting = await _pool.CountAsync(p => p.TournamentId == tournamentId && !p.IsWithdrawn && p.PlayerId == null);
        if (waiting == 0) return null;
        return $"{Plural(waiting, "player")} in the player pool {(waiting == 1 ? "doesn't" : "don't")} have a team yet, and the pool closes when the tournament starts. "
               + "Run the auction or the draw and confirm the teams first, or remove them from the pool.";
    }
}
