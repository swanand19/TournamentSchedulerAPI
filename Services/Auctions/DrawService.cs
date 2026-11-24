using System.Security.Cryptography;
using TournamentScheduler.Api.Auth;
using TournamentScheduler.Api.Data;
using TournamentScheduler.Api.Data.DataServices;
using TournamentScheduler.Api.Models;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Models.Auctions;
using TournamentScheduler.Api.Models.Messaging;
using TournamentScheduler.Api.Realtime;
using TournamentScheduler.Api.Services.Access;
using TournamentScheduler.Api.Services.Notifications;

namespace TournamentScheduler.Api.Services.Auctions;

public interface IDrawService
{
    Task<ServiceResult<DrawStateDto>> GetAsync(int tournamentId);
    Task<ServiceResult<DrawStateDto>> SettingsAsync(int tournamentId, DrawSettingsRequest request);
    Task<ServiceResult<DrawStateDto>> StartAsync(int tournamentId);
    Task<ServiceResult<DrawStateDto>> NextAsync(int tournamentId);
    Task<ServiceResult<DrawStateDto>> RestAsync(int tournamentId, DrawRestRequest request);
    Task<ServiceResult<DrawStateDto>> QuickAsync(int tournamentId);
    Task<ServiceResult<DrawStateDto>> RestartAsync(int tournamentId);
    Task<ServiceResult<DrawStateDto>> TakeOverAsync(int tournamentId);
    Task<ServiceResult<DrawStateDto>> MoveAsync(int tournamentId, DrawMoveRequest request);
    Task<ServiceResult<DrawStateDto>> SwapAsync(int tournamentId, DrawSwapRequest request);
    Task<ServiceResult<DrawStateDto>> ConfirmAsync(int tournamentId);

    /// <summary>For the clock job: lands names whose spin is over, and draws the next one when the pace says so. Saves and nudges.</summary>
    Task TickAsync(int tournamentId);

    /// <summary>Why the tournament can't start yet because of its draw; null if it can.</summary>
    Task<string?> StartProblemAsync(int tournamentId);
}

/// <summary>
/// The team draw (plan sections 3 and 9). One owner draws at a time; each tap names the team whose
/// turn it is (<see cref="DrawRules"/>) and picks a player for it at random from the pot, there and
/// then, with a cryptographic random number. The name stays on the server until it lands
/// (<see cref="SpinMs"/> later) on every screen at once, so nobody, the drawer included, can see or
/// choose it first. There is no "undo last pick" (E-3): fixes are Move / Swap, marked for everyone,
/// or Start again.
/// </summary>
public class DrawService : ServiceBase, IDrawService
{
    /// <summary>How long the names spin before one lands.</summary>
    public const int SpinMs = 3500;

    public const int MinAutoSeconds = 6;
    public const int MaxAutoSeconds = 30;

    /// <summary>Most names the spinner is sent: plenty for the effect, small on the wire.</summary>
    private const int SpinNames = 30;

    private readonly IDataService<Tournament> _tournaments;
    private readonly IDataService<Team> _teams;
    private readonly IDataService<Player> _players;
    private readonly IDataService<User> _users;
    private readonly IDataService<TournamentMember> _members;
    private readonly IDataService<PlayerPool> _pools;
    private readonly IDataService<PoolTier> _tiers;
    private readonly IDataService<PoolPlayer> _pool;
    private readonly IDataService<Auction> _auctions;
    private readonly IDataService<TeamDraw> _draws;
    private readonly IDataService<DrawPick> _picks;
    private readonly IPoolService _poolService;
    private readonly ISquadMaker _squads;
    private readonly INotifier _notifier;
    private readonly ILiveUpdates _live;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly ITournamentContext _context;
    private readonly AuctionLocks _locks;
    private readonly DrawClock _clockWatch;
    private readonly TimeProvider _clock;

    public DrawService(
        IDataService<Tournament> tournaments,
        IDataService<Team> teams,
        IDataService<Player> players,
        IDataService<User> users,
        IDataService<TournamentMember> members,
        IDataService<PlayerPool> pools,
        IDataService<PoolTier> tiers,
        IDataService<PoolPlayer> pool,
        IDataService<Auction> auctions,
        IDataService<TeamDraw> draws,
        IDataService<DrawPick> picks,
        IPoolService poolService,
        ISquadMaker squads,
        INotifier notifier,
        ILiveUpdates live,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        ITournamentContext context,
        AuctionLocks locks,
        DrawClock clockWatch,
        TimeProvider clock)
    {
        _tournaments = tournaments;
        _teams = teams;
        _players = players;
        _users = users;
        _members = members;
        _pools = pools;
        _tiers = tiers;
        _pool = pool;
        _auctions = auctions;
        _draws = draws;
        _picks = picks;
        _poolService = poolService;
        _squads = squads;
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
    // The book: the draw and everything around it, loaded fresh inside the lock
    // =================================================================

    private sealed record TeamRow(int Id, string Name);

    private sealed record Kept(int Id, int TeamId, string Name, int? UserId);

    private sealed class Book
    {
        public required Tournament Tournament;
        public TeamDraw? Draw;
        public PlayerPool? PoolRow;
        public AuctionStatus? Auction;
        public required List<TeamRow> Teams;
        public required List<Kept> KeptPlayers;
        public required List<PoolTier> Tiers;
        public required List<PoolPlayer> Pool;
        public required List<DrawPick> Picks;
        public required Dictionary<int, string?> Roles;
        public required Dictionary<int, string> Names;
        public required DateTime Now;

        public DrawStatus? Status => Draw?.Status;
        public bool Live => Draw?.Status == DrawStatus.Live;

        public string TeamName(int id) => Teams.FirstOrDefault(t => t.Id == id)?.Name ?? "a team";
        public PoolPlayer? PlayerOf(DrawPick pick) => Pool.FirstOrDefault(p => p.Id == pick.PoolPlayerId);
        public PoolTier? TierOf(PoolPlayer? p) => p?.TierId is int id ? Tiers.FirstOrDefault(t => t.Id == id) : null;

        public bool Landed(DrawPick p) => p.LandsAt <= Now;

        /// <summary>The name still spinning, if any (one at a time).</summary>
        public DrawPick? Pending => Picks.Where(p => !Landed(p)).OrderBy(p => p.Order).FirstOrDefault();

        /// <summary>In the pool, not withdrawn, not drawn yet.</summary>
        public List<PoolPlayer> Undrawn => Pool.Where(p => !p.IsWithdrawn && Picks.All(k => k.PoolPlayerId != p.Id)).ToList();

        /// <summary>The pots in drawing order: one per tier (top first, "No tier" last), or everyone for a draw at random.</summary>
        public List<(string Name, List<PoolPlayer> Players)> Pots(List<PoolPlayer> players)
        {
            if (Draw?.Kind == DrawKind.Random) return players.Count == 0 ? [] : [("Everyone left", players)];
            return players
                .GroupBy(p => TierOf(p)?.Order ?? int.MaxValue)
                .OrderBy(g => g.Key)
                .Select(g => (TierOf(g.First()) is { } tier ? $"Tier {tier.Name}" : "No tier", g.ToList()))
                .ToList();
        }

        public List<int> Order()
        {
            var ids = (Draw?.TeamOrder ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();
            var known = ids.Where(id => Teams.Any(t => t.Id == id)).ToList();
            // A team that wasn't there when the order was drawn goes last (teams are fixed mid-draw, so rarely).
            known.AddRange(Teams.Select(t => t.Id).Where(id => !known.Contains(id)));
            return known;
        }

        /// <summary>Everyone in each team now: the players already there and every name drawn for it (the spinning one too).</summary>
        public Dictionary<int, int> Counts() => Teams.ToDictionary(
            t => t.Id,
            t => KeptPlayers.Count(k => k.TeamId == t.Id) + Picks.Count(p => p.TeamId == t.Id));

        /// <summary>The team after which the next turn starts: where the last name was first drawn to.</summary>
        public int? LastTeam => Picks.OrderByDescending(p => p.Order).FirstOrDefault() is { } last ? last.MovedFromTeamId ?? last.TeamId : null;
    }

    private async Task<Book?> LoadAsync(int tournamentId, bool tracking)
    {
        var tournament = await _tournaments.FirstOrDefaultAsync(t => t.Id == tournamentId, tracking: false);
        if (tournament == null) return null;

        var draw = await _draws.FirstOrDefaultAsync(d => d.TournamentId == tournamentId, tracking: tracking);
        var poolRow = await _pools.FirstOrDefaultAsync(p => p.TournamentId == tournamentId, tracking: tracking);
        var auction = await _auctions.QueryFirstOrDefaultAsync(q => q.Where(a => a.TournamentId == tournamentId).Select(a => (AuctionStatus?)a.Status), tracking: false);
        // A lasting team's squad came with it (C-17, C-18): the draw deals only to the other teams.
        var teams = await _teams.QueryAsync(q => q.Where(t => t.TournamentId == tournamentId && t.ClubId == null).OrderBy(t => t.Name).Select(t => new TeamRow(t.Id, t.Name)), tracking: false);
        var kept = await _players.QueryAsync(q => q.Where(p => p.TournamentId == tournamentId).OrderBy(p => p.Id).Select(p => new Kept(p.Id, p.TeamId, p.Name, p.UserId)), tracking: false);
        var tiers = await _tiers.ListAsync(t => t.TournamentId == tournamentId, q => q.OrderBy(t => t.Order), tracking: false);
        var pool = await _pool.ListAsync(p => p.TournamentId == tournamentId, q => q.OrderBy(p => p.Order).ThenBy(p => p.Id), tracking: tracking);
        var picks = draw == null ? [] : await _picks.ListAsync(p => p.DrawId == draw.Id, q => q.OrderBy(p => p.Order), tracking: tracking);
        // Once confirmed, the drawn players are squad places too: count them once, as drawn.
        var made = pool.Where(p => p.PlayerId != null).Select(p => p.PlayerId!.Value).ToHashSet();
        kept = kept.Where(k => !made.Contains(k.Id)).ToList();

        var names = new Dictionary<int, string>();
        if (draw?.DrawerUserId is int drawer && await _users.GetByIdAsync(drawer) is { } user) names[drawer] = user.Name;

        return new Book
        {
            Tournament = tournament,
            Draw = draw,
            PoolRow = poolRow,
            Auction = auction,
            Teams = teams,
            KeptPlayers = kept,
            Tiers = tiers,
            Pool = pool,
            Picks = picks,
            Roles = await _poolService.RolesAsync(tournament.Sport, pool),
            Names = names,
            Now = Now
        };
    }

    private readonly List<(int UserId, string Team)> _pendingNotices = [];

    /// <summary>
    /// Runs a change with the tournament's lock held (the auction's lock: one change at a time per
    /// tournament): loads fresh, lands any name whose spin is over, applies the change, saves.
    /// </summary>
    private async Task<ServiceResult<DrawStateDto>> ChangeAsync(int tournamentId, Func<Book, Task<ServiceError?>> change)
    {
        using (await _locks.EnterAsync(tournamentId))
        {
            var book = await LoadAsync(tournamentId, tracking: true);
            if (book == null) return NotFound("Tournament not found.");

            var landed = LandDue(book);
            var error = await change(book);
            if (error is { } refused)
            {
                if (landed) await SaveAndTellAsync(book, nudge: true);
                return refused;
            }
            await SaveAndTellAsync(book, nudge: false); // owners' changes are nudged by the access filter
            return Success(Build(book));
        }
    }

    private async Task SaveAndTellAsync(Book b, bool nudge)
    {
        foreach (var (userId, team) in _pendingNotices)
            await _notifier.NotifyAsync(userId, NotificationTemplateCode.DrawPicked, Notifier.With(
                    ("TeamName", team), ("TournamentName", b.Tournament.Name)),
                new NotificationLink(b.Tournament.Id));
        _pendingNotices.Clear();
        await _unitOfWork.SaveChangesAsync();
        _clockWatch.Track(b.Tournament.Id, NextDue(b));
        if (nudge) await _live.TournamentChangedAsync(b.Tournament.Id);
    }

    /// <summary>When the clock job should next look: a name landing, or the next name drawn by itself.</summary>
    private static DateTime? NextDue(Book b)
    {
        if (!b.Live) return null;
        DateTime? due = b.Picks.Where(p => !p.Announced).Select(p => (DateTime?)p.LandsAt).Min();
        if (b.Draw!.NextAutoAt is { } auto && (due == null || auto < due)) due = auto;
        return due;
    }

    /// <summary>Tells each player whose name has landed (once). True if any did.</summary>
    private bool LandDue(Book b)
    {
        var any = false;
        foreach (var pick in b.Picks.Where(p => !p.Announced && b.Landed(p)))
        {
            pick.Announced = true;
            any = true;
            if (b.PlayerOf(pick) is { UserId: int person }) _pendingNotices.Add((person, b.TeamName(pick.TeamId)));
        }
        return any;
    }

    // =================================================================
    // Reading
    // =================================================================

    public async Task<ServiceResult<DrawStateDto>> GetAsync(int tournamentId)
    {
        // A name whose spin is over is landed (and its player told) by whoever looks first.
        var drawId = await _draws.QueryFirstOrDefaultAsync(q => q.Where(d => d.TournamentId == tournamentId && d.Status == DrawStatus.Live).Select(d => (int?)d.Id), tracking: false);
        if (drawId is int id)
        {
            var now = Now;
            if (await _picks.AnyAsync(p => p.DrawId == id && !p.Announced && p.LandsAt <= now)) await TickAsync(tournamentId);
        }
        var book = await LoadAsync(tournamentId, tracking: false);
        return book == null ? NotFound("Tournament not found.") : Success(Build(book));
    }

    private DrawStateDto Build(Book b)
    {
        var me = Viewer;
        var d = b.Draw;
        var owner = _context.IsOwner;
        var isDrawer = d?.DrawerUserId is int drawer && drawer == me;
        var pending = b.Pending;
        var undrawn = b.Undrawn;
        var landed = b.Picks.Where(b.Landed).ToList();
        var allDrawn = b.Live && undrawn.Count == 0 && pending == null;
        var started = b.Status is DrawStatus.Live or DrawStatus.Confirmed;
        var myPool = b.Pool.FirstOrDefault(p => p.UserId != null && p.UserId == me);

        var state = new DrawStateDto
        {
            Status = d?.Status.ToString() ?? "None",
            AllDrawn = allDrawn,
            ServerNow = b.Now,
            Method = b.PoolRow?.Method,
            Kind = d?.Kind ?? DrawKind.ByTier,
            AutoSeconds = d?.AutoSeconds,
            Times = d?.Times ?? 0,
            Drawer = d?.DrawerUserId is int du ? new PersonDto { UserId = du, Name = b.Names.GetValueOrDefault(du, "Someone") } : null,
            Me = new DrawMeDto
            {
                IsOwner = owner,
                IsDrawer = isDrawer,
                PoolPlayerId = myPool?.Id,
                DrawnForTeamId = myPool == null ? null : landed.FirstOrDefault(p => p.PoolPlayerId == myPool.Id)?.TeamId
            },
            Counts = new DrawCountsDto
            {
                Players = b.Pool.Count(p => !p.IsWithdrawn),
                Drawn = landed.Count,
                Left = undrawn.Count + (pending == null ? 0 : 1)
            }
        };

        // Teams: round the table once started, with the names landed in each.
        var order = started ? b.Order() : b.Teams.Select(t => t.Id).ToList();
        var counts = b.Counts();
        for (var i = 0; i < order.Count; i++)
        {
            var id = order[i];
            state.Teams.Add(new DrawTeamDto
            {
                TeamId = id,
                Name = b.TeamName(id),
                Turn = started ? i + 1 : null,
                Players = counts[id] - (pending?.TeamId == id ? 1 : 0),
                Kept = b.KeptPlayers.Where(k => k.TeamId == id).Select(k => k.Name).ToList(),
                Drawn = landed.Where(p => p.TeamId == id).Select(p => PickDto(b, p)).ToList(),
                IsDrawing = pending?.TeamId == id
            });
        }

        // The pot: the one spinning still counts as in it, so the list gives nothing away.
        var inPot = pending != null && b.PlayerOf(pending) is { } spinning ? undrawn.Append(spinning).ToList() : undrawn;
        var pots = b.Pots(inPot);
        if (b.Live && pots.Count > 0)
        {
            state.Pot = new DrawPotDto
            {
                Name = pots[0].Name,
                Players = pots[0].Players.OrderBy(p => p.Name).Select(p => new DrawPotPlayerDto { PoolPlayerId = p.Id, Name = p.Name, Role = b.Roles.GetValueOrDefault(p.Id) }).ToList(),
                NextName = pots.Count > 1 ? pots[1].Name : null,
                NextCount = pots.Count > 1 ? pots[1].Players.Count : 0
            };
        }

        if (pending != null)
        {
            state.Spin = new DrawSpinDto
            {
                PickId = pending.Id,
                TeamId = pending.TeamId,
                TeamName = b.TeamName(pending.TeamId),
                PickedAt = pending.PickedAt,
                LandsAt = pending.LandsAt,
                PotName = state.Pot?.Name ?? "",
                Names = (state.Pot?.Players.Select(p => p.Name) ?? []).Take(SpinNames).ToList()
            };
        }

        if (landed.OrderByDescending(p => p.Order).FirstOrDefault() is { } last) state.Last = PickDto(b, last);
        if (b.Live && pending == null && undrawn.Count > 0)
            state.NextTeamId = DrawRules.NextTeam(order, counts, b.LastTeam);

        if (!started) state.Preview = PreviewOf(b);
        state.StartProblem = started ? null : StartProblem(b);

        var open = !b.Tournament.IsStarted && !b.Tournament.IsClosed;
        var auctionStarted = b.Auction is not (null or AuctionStatus.Setup);
        var drawing = isDrawer && b.Live && pending == null && undrawn.Count > 0;
        state.Actions = new DrawActionsDto
        {
            CanChooseMethod = owner && open && !started && !auctionStarted,
            CanEditKind = owner && open && !started,
            CanEditPace = owner && open && (!started || (isDrawer && b.Live)),
            CanStart = owner && open && !started && state.StartProblem == null,
            CanQuickDraw = owner && open && (!started ? state.StartProblem == null : isDrawer && b.Live && pending == null),
            CanDrawNext = drawing,
            CanDrawRestOfPot = drawing && d!.Kind == DrawKind.ByTier,
            CanDrawAll = drawing,
            CanRestart = isDrawer && b.Live,
            CanTakeOver = owner && open && !isDrawer && b.Live,
            CanMove = isDrawer && b.Live && pending == null && landed.Count > 0,
            CanConfirm = isDrawer && allDrawn,
            ConfirmProblem = b.Live && !allDrawn
                ? pending != null ? "A name is still being drawn." : $"{Plural(undrawn.Count, "player")} still to be drawn."
                : null
        };
        return state;
    }

    private DrawPickDto PickDto(Book b, DrawPick p)
    {
        var player = b.PlayerOf(p);
        return new DrawPickDto
        {
            PickId = p.Id,
            PoolPlayerId = p.PoolPlayerId,
            Name = player?.Name ?? "",
            Role = player == null ? null : b.Roles.GetValueOrDefault(player.Id),
            TierName = b.TierOf(player)?.Name,
            TeamId = p.TeamId,
            TeamName = b.TeamName(p.TeamId),
            Order = p.Order,
            LandedAt = p.LandsAt,
            MovedFromTeamName = p.MovedFromTeamId is int from ? b.TeamName(from) : null,
            IsMe = player?.UserId != null && player.UserId == Viewer
        };
    }

    /// <summary>What a draw would give, before it starts: the same rule, the teams in alphabetical order.</summary>
    private static DrawPreviewDto PreviewOf(Book b)
    {
        var players = b.Pool.Where(p => !p.IsWithdrawn).ToList();
        var order = b.Teams.Select(t => t.Id).ToList();
        var has = b.Teams.ToDictionary(t => t.Id, t => b.KeptPlayers.Count(k => k.TeamId == t.Id));
        var preview = new DrawPreviewDto();
        if (order.Count == 0) return preview;

        var ends = DrawRules.Deal(order, has, null, players.Count);
        preview.Line = $"{Plural(players.Count, "player")} for {Plural(order.Count, "team")}. {DrawRules.Ending(ends.Values)}";
        // When the totals come out uneven, which teams get the extra one depends on the order drawn at
        // the start: any team not already above the lower total may end with either.
        var low = ends.Values.Min();
        var high = ends.Where(e => e.Value > has[e.Key] || e.Value == low).Select(e => e.Value).DefaultIfEmpty(low).Max();
        preview.Teams = b.Teams.Select(t =>
        {
            var either = high > low && has[t.Id] <= low;
            var endsWith = either ? low : ends[t.Id];
            return new DrawPreviewTeamDto
            {
                TeamId = t.Id, Name = t.Name, Has = has[t.Id], Gets = endsWith - has[t.Id], EndsWith = endsWith, OrOneMore = either
            };
        }).ToList();

        var fewest = has.Values.Min();
        var ahead = b.Teams.Where(t => has[t.Id] > fewest && players.Count > 0).ToList();
        if (ahead.Count == 1)
            preview.Note = $"{ahead[0].Name} already {(has[ahead[0].Id] == 1 ? "has" : "have")} {has[ahead[0].Id]}, so they sit out until the others catch up. Who each team gets is always random.";
        else if (ahead.Count > 1)
            preview.Note = $"{string.Join(", ", ahead.Take(ahead.Count - 1).Select(t => t.Name))} and {ahead[^1].Name} already have more players, so they sit out until the others catch up. Who each team gets is always random.";

        var bTiers = b.Draw?.Kind == DrawKind.Random ? [] : b.Pots(players);
        preview.Tiers = bTiers.Select(p => new DrawPreviewTierDto { Name = p.Name, Players = p.Players.Count }).ToList();
        return preview;
    }

    private static string? StartProblem(Book b)
    {
        if (b.Tournament.IsStarted) return "The tournament has started, so its teams are made.";
        if (b.Auction is not (null or AuctionStatus.Setup))
            return "The auction has started, so the teams are being bought. Cancel the auction first to draw them instead.";
        if (b.PoolRow?.Method == PoolMethod.Auction) return "The pool is set to fill the teams by auction. Choose the draw on the pool screen first.";
        if (b.Teams.Count < 2) return "A draw needs at least two teams. Add the teams (with their captains) first.";
        if (!b.Pool.Any(p => !p.IsWithdrawn)) return "There's nobody in the pool to draw. Add players first.";
        return null;
    }

    private static string Plural(int n, string word) => AuctionMath.Plural(n, word);

    // =================================================================
    // Setting it up and starting
    // =================================================================

    private ServiceError? Open(Book b) =>
        b.Tournament.IsStarted || b.Tournament.IsClosed ? Conflict("The tournament has started, so its teams are made.")
        : b.Status == DrawStatus.Confirmed ? Conflict("The teams are confirmed, so the draw is closed.")
        : null;

    private async Task<TeamDraw> EnsureDrawAsync(Book b)
    {
        if (b.Draw != null) return b.Draw;
        var draw = new TeamDraw { TournamentId = b.Tournament.Id, CreatedAt = Now };
        _draws.Add(draw);
        await _unitOfWork.SaveChangesAsync(); // its id, for the picks
        b.Draw = draw;
        return draw;
    }

    public Task<ServiceResult<DrawStateDto>> SettingsAsync(int tournamentId, DrawSettingsRequest r) => ChangeAsync(tournamentId, async b =>
    {
        if (Open(b) is { } closed) return closed;
        if (r.Kind != null && b.Live) return Conflict("The draw has started, so how names are drawn is fixed. Start again to change it.");
        if (b.Live && !IsDrawer(b)) return Forbidden(DrawerOnly(b));
        if (r.SetPace && r.AutoSeconds is int secs && secs is < MinAutoSeconds or > MaxAutoSeconds)
            return BadRequest($"Draw by itself every {MinAutoSeconds} to {MaxAutoSeconds} seconds, or tap for each name.");

        var d = await EnsureDrawAsync(b);
        if (r.Kind is { } kind) d.Kind = kind;
        if (r.SetPace)
        {
            d.AutoSeconds = r.AutoSeconds;
            // Switched on mid-draw: the next name comes a moment after the one spinning lands.
            d.NextAutoAt = b.Live && r.AutoSeconds != null && b.Undrawn.Count > 0
                ? (b.Pending?.LandsAt ?? Now).AddSeconds(2)
                : null;
        }
        return null;
    });

    public Task<ServiceResult<DrawStateDto>> StartAsync(int tournamentId) => ChangeAsync(tournamentId, async b =>
    {
        if (Open(b) is { } closed) return closed;
        if (b.Live) return Conflict("The draw has already started.");
        if (StartProblem(b) is { } problem) return Conflict(problem);

        await BeginAsync(b);
        var d = b.Draw!;
        if (d.AutoSeconds != null) d.NextAutoAt = Now.AddSeconds(3);

        // Everyone with a stake hears it has started (E-5): the pool, the teams' players, owners and scorers.
        var me = _currentUser.UserId;
        var told = new HashSet<int> { me };
        var inPool = b.Pool.Where(p => p.UserId != null && !p.IsWithdrawn).Select(p => p.UserId!.Value).ToHashSet();
        var members = await _members.QueryAsync(q => q.Where(m => m.TournamentId == b.Tournament.Id).Select(m => m.UserId), tracking: false);
        var people = inPool
            .Concat(b.KeptPlayers.Where(k => k.UserId != null).Select(k => k.UserId!.Value))
            .Concat(members)
            .Append(b.Tournament.CreatedByUserId);
        foreach (var person in people)
        {
            if (person <= 1 || !told.Add(person)) continue;
            await _notifier.NotifyAsync(person, NotificationTemplateCode.DrawStarted, Notifier.With(
                    ("TournamentName", b.Tournament.Name),
                    ("YourPart", inPool.Contains(person) ? "You're in the pool: watch which team you're drawn for." : "Watch the names land in each team.")),
                new NotificationLink(b.Tournament.Id));
        }
        return null;
    });

    /// <summary>Starts (or restarts) the draw: the order round the table is drawn, and it stays a draw from here (E-1).</summary>
    private async Task BeginAsync(Book b)
    {
        var d = await EnsureDrawAsync(b);
        var ids = b.Teams.Select(t => t.Id).ToArray();
        RandomNumberGenerator.Shuffle(ids.AsSpan());
        d.TeamOrder = string.Join(',', ids);
        d.Status = DrawStatus.Live;
        d.Times++;
        d.DrawerUserId = _currentUser.UserId;
        d.StartedAt = Now;
        b.Names[_currentUser.UserId] = _currentUser.Name;

        if (b.PoolRow == null)
        {
            b.PoolRow = new PlayerPool { TournamentId = b.Tournament.Id };
            _pools.Add(b.PoolRow);
        }
        b.PoolRow.Method = PoolMethod.Draw;
    }

    // =================================================================
    // Drawing
    // =================================================================

    private bool IsDrawer(Book b) => b.Draw?.DrawerUserId is int drawer && drawer == Viewer;

    private static string DrawerOnly(Book b) =>
        b.Draw?.DrawerUserId is int drawer
            ? $"{b.Names.GetValueOrDefault(drawer, "Someone else")} is drawing. Take over to draw yourself."
            : "Only the person drawing can do that.";

    /// <summary>The draw must be running and the caller drawing it.</summary>
    private ServiceError? Drawer(Book b)
    {
        if (Open(b) is { } closed) return closed;
        if (!b.Live) return Conflict("The draw hasn't started yet.");
        if (!IsDrawer(b)) return Forbidden(DrawerOnly(b));
        return null;
    }

    /// <summary>Draws one name: the team by the rule, the player at random from the current pot. Lands at <paramref name="landsAt"/>.</summary>
    private DrawPick? Pick(Book b, DateTime landsAt)
    {
        var undrawn = b.Undrawn;
        var pots = b.Pots(undrawn);
        if (pots.Count == 0) return null;
        var pot = pots[0].Players;
        var player = pot[RandomNumberGenerator.GetInt32(pot.Count)];
        var team = DrawRules.NextTeam(b.Order(), b.Counts(), b.LastTeam);
        var now = Now;
        var pick = new DrawPick
        {
            DrawId = b.Draw!.Id,
            TournamentId = b.Tournament.Id,
            PoolPlayerId = player.Id,
            TeamId = team,
            Order = b.Picks.Select(p => p.Order).DefaultIfEmpty(0).Max() + 1,
            TierId = player.TierId,
            PickedAt = now,
            LandsAt = landsAt
        };
        _picks.Add(pick);
        b.Picks.Add(pick);
        return pick;
    }

    private ServiceError? Busy(Book b)
    {
        if (b.Pending != null) return Conflict("A name is still being drawn. Wait for it to land.");
        if (b.Undrawn.Count == 0) return Conflict("Everyone has been drawn. Move anyone you need to, then confirm the teams.");
        return null;
    }

    public Task<ServiceResult<DrawStateDto>> NextAsync(int tournamentId) => ChangeAsync(tournamentId, b =>
    {
        if ((Drawer(b) ?? Busy(b)) is { } refused) return Task.FromResult<ServiceError?>(refused);
        var now = Now;
        Pick(b, now.AddMilliseconds(SpinMs));
        if (b.Draw!.AutoSeconds is int every) b.Draw.NextAutoAt = now.AddSeconds(every);
        return Task.FromResult<ServiceError?>(null);
    });

    public Task<ServiceResult<DrawStateDto>> RestAsync(int tournamentId, DrawRestRequest r) => ChangeAsync(tournamentId, b =>
    {
        if ((Drawer(b) ?? Busy(b)) is { } refused) return Task.FromResult<ServiceError?>(refused);
        DrawAtOnce(b, r.All ? null : b.Pots(b.Undrawn)[0].Name);
        return Task.FromResult<ServiceError?>(null);
    });

    /// <summary>Draws the rest of one pot (by name), or everyone left; each lands at once.</summary>
    private void DrawAtOnce(Book b, string? pot)
    {
        var now = Now;
        while (b.Pots(b.Undrawn) is { Count: > 0 } pots && (pot == null || pots[0].Name == pot))
        {
            var pick = Pick(b, now)!;
            pick.Announced = true;
            if (b.PlayerOf(pick) is { UserId: int person }) _pendingNotices.Add((person, b.TeamName(pick.TeamId)));
        }
        b.Draw!.NextAutoAt = null;
    }

    public Task<ServiceResult<DrawStateDto>> QuickAsync(int tournamentId) => ChangeAsync(tournamentId, async b =>
    {
        if (Open(b) is { } closed) return closed;
        if (b.Live)
        {
            // "Shuffle again": a fresh draw, in full view (the count of draws goes up).
            if (!IsDrawer(b)) return Forbidden(DrawerOnly(b));
            if (b.Pending != null) return Conflict("A name is still being drawn. Wait for it to land.");
            _picks.RemoveRange(b.Picks);
            b.Picks.Clear();
        }
        else if (StartProblem(b) is { } problem) return Conflict(problem);

        await BeginAsync(b);
        DrawAtOnce(b, null);
        return null;
    });

    public Task<ServiceResult<DrawStateDto>> RestartAsync(int tournamentId) => ChangeAsync(tournamentId, b =>
    {
        if (Drawer(b) is { } refused) return Task.FromResult<ServiceError?>(refused);
        _picks.RemoveRange(b.Picks);
        b.Picks.Clear();
        var d = b.Draw!;
        d.Status = DrawStatus.Setup;
        d.TeamOrder = string.Empty;
        d.DrawerUserId = null;
        d.NextAutoAt = null;
        d.StartedAt = null;
        return Task.FromResult<ServiceError?>(null);
    });

    public Task<ServiceResult<DrawStateDto>> TakeOverAsync(int tournamentId) => ChangeAsync(tournamentId, b =>
    {
        if (Open(b) is { } closed) return Task.FromResult<ServiceError?>(closed);
        if (!b.Live) return Task.FromResult<ServiceError?>(Conflict("There's no draw running to take over."));
        if (IsDrawer(b)) return Task.FromResult<ServiceError?>(Conflict("You're already drawing."));
        b.Draw!.DrawerUserId = _currentUser.UserId;
        b.Names[_currentUser.UserId] = _currentUser.Name;
        return Task.FromResult<ServiceError?>(null);
    });

    public async Task TickAsync(int tournamentId)
    {
        using (await _locks.EnterAsync(tournamentId))
        {
            var b = await LoadAsync(tournamentId, tracking: true);
            if (b?.Draw == null) return;
            var changed = LandDue(b);

            // Drawing by itself: the next name once it's due and the last one has landed.
            var d = b.Draw;
            if (b.Live && d.AutoSeconds is int every && d.NextAutoAt is { } due && due <= b.Now)
            {
                if (b.Undrawn.Count == 0) d.NextAutoAt = null;
                else if (b.Pending is { } spinning) d.NextAutoAt = spinning.LandsAt.AddSeconds(1);
                else
                {
                    Pick(b, b.Now.AddMilliseconds(SpinMs));
                    d.NextAutoAt = b.Now.AddSeconds(every);
                }
                changed = true;
            }
            if (changed) await SaveAndTellAsync(b, nudge: true);
            else _clockWatch.Track(tournamentId, NextDue(b));
        }
    }

    // =================================================================
    // After the draw: moving, swapping, confirming
    // =================================================================

    /// <summary>The landed pick of a pool player, or why it can't be moved.</summary>
    private (DrawPick? Pick, ServiceError? Error) Landed(Book b, int poolPlayerId)
    {
        var pick = b.Picks.FirstOrDefault(p => p.PoolPlayerId == poolPlayerId);
        if (pick == null || !b.Landed(pick)) return (null, NotFound("That player hasn't been drawn yet."));
        return (pick, null);
    }

    private static void MoveTo(DrawPick pick, int teamId)
    {
        var drawnTo = pick.MovedFromTeamId ?? pick.TeamId;
        pick.TeamId = teamId;
        pick.MovedFromTeamId = drawnTo == teamId ? null : drawnTo;
    }

    public Task<ServiceResult<DrawStateDto>> MoveAsync(int tournamentId, DrawMoveRequest r) => ChangeAsync(tournamentId, b =>
    {
        if (Drawer(b) is { } refused) return Task.FromResult<ServiceError?>(refused);
        if (b.Pending != null) return Task.FromResult<ServiceError?>(Conflict("A name is still being drawn. Wait for it to land."));
        var (pick, error) = Landed(b, r.PoolPlayerId);
        if (error != null) return Task.FromResult<ServiceError?>(error);
        var team = b.Teams.FirstOrDefault(t => t.Id == r.TeamId);
        if (team == null) return Task.FromResult<ServiceError?>(NotFound("That team isn't in this tournament."));
        if (pick!.TeamId == team.Id) return Task.FromResult<ServiceError?>(Conflict($"{b.PlayerOf(pick)?.Name} is already in {team.Name}."));
        MoveTo(pick, team.Id);
        return Task.FromResult<ServiceError?>(null);
    });

    public Task<ServiceResult<DrawStateDto>> SwapAsync(int tournamentId, DrawSwapRequest r) => ChangeAsync(tournamentId, b =>
    {
        if (Drawer(b) is { } refused) return Task.FromResult<ServiceError?>(refused);
        if (b.Pending != null) return Task.FromResult<ServiceError?>(Conflict("A name is still being drawn. Wait for it to land."));
        var (one, e1) = Landed(b, r.PoolPlayerId);
        var (other, e2) = Landed(b, r.OtherPoolPlayerId);
        if ((e1 ?? e2) is { } error) return Task.FromResult<ServiceError?>(error);
        if (one!.TeamId == other!.TeamId) return Task.FromResult<ServiceError?>(Conflict("Those two are in the same team. Pick a player from another team."));
        var (a, c) = (one.TeamId, other.TeamId);
        MoveTo(one, c);
        MoveTo(other, a);
        return Task.FromResult<ServiceError?>(null);
    });

    public Task<ServiceResult<DrawStateDto>> ConfirmAsync(int tournamentId) => ChangeAsync(tournamentId, async b =>
    {
        if (Drawer(b) is { } refused) return refused;
        if (b.Pending != null) return Conflict("A name is still being drawn. Wait for it to land.");
        if (b.Undrawn.Count is > 0 and var left) return Conflict($"{Plural(left, "player")} still to be drawn. Draw them first.");

        var d = b.Draw!;
        d.Status = DrawStatus.Confirmed;
        d.ConfirmedAt = Now;
        d.NextAutoAt = null;
        var places = b.Picks.OrderBy(p => p.TeamId).ThenBy(p => p.Order)
            .Select(p => (Pool: b.PlayerOf(p), p.TeamId))
            .Where(x => x.Pool != null && !x.Pool.IsWithdrawn)
            .Select(x => (x.Pool!, x.TeamId))
            .ToList();
        return await _squads.MakeAsync(b.Tournament, places) is { } problem ? Conflict(problem) : null;
    });

    public async Task<string?> StartProblemAsync(int tournamentId)
    {
        var status = await _draws.QueryFirstOrDefaultAsync(q => q.Where(d => d.TournamentId == tournamentId).Select(d => (DrawStatus?)d.Status), tracking: false);
        return status == DrawStatus.Live
            ? "The team draw isn't finished. Finish it and confirm the teams before starting the tournament."
            : null;
    }
}

/// <summary>
/// When each running draw next needs the clock job: a name landing (its player is told, every screen
/// nudged) or the next name drawn by itself. In memory, filled from the database at start-up.
/// </summary>
public sealed class DrawClock
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, DateTime> _due = new();

    public void Track(int tournamentId, DateTime? due)
    {
        if (due is { } at) _due[tournamentId] = at;
        else _due.TryRemove(tournamentId, out _);
    }

    public List<int> DueAt(DateTime now) => _due.Where(d => d.Value <= now).Select(d => d.Key).ToList();
}
