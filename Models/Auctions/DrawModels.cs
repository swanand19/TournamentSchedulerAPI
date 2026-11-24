using TournamentScheduler.Api.Models.Accounts;

namespace TournamentScheduler.Api.Models.Auctions;

// The team draw (2026-11-17, plan sections 3 and 9): the other way to fill teams from the pool. Each
// turn names the team with the fewest players (round the table), then a player is drawn for it at
// random from the pot (the top tier still holding someone, or everyone). The server picks at the
// tap and keeps the name to itself until it "lands" a few seconds later on every screen at once.

/// <summary>How a tournament fills its teams from the pool. Changeable until either way starts (E-1).</summary>
public enum PoolMethod
{
    Draw = 1,
    Auction = 2
}

public enum DrawKind
{
    /// <summary>Top tier first, then the next, "No tier" last (plan 3.1).</summary>
    ByTier = 0,
    /// <summary>Anyone left, any time.</summary>
    Random = 1
}

public enum DrawStatus
{
    /// <summary>Being set up; nothing drawn.</summary>
    Setup = 0,
    /// <summary>Started: names being drawn, or all drawn and waiting to be confirmed.</summary>
    Live = 1,
    /// <summary>Squads made. Nothing here changes any more.</summary>
    Confirmed = 2
}

/// <summary>One tournament's team draw: how it's drawn, where it stands, and who draws.</summary>
public class TeamDraw
{
    public int Id { get; set; }
    public int TournamentId { get; set; }

    public DrawKind Kind { get; set; } = DrawKind.ByTier;
    public DrawStatus Status { get; set; } = DrawStatus.Setup;

    /// <summary>The order round the table, drawn at the start: team ids, comma-separated.</summary>
    public string TeamOrder { get; set; } = string.Empty;

    /// <summary>Whoever draws now (an owner); only they draw, move and confirm. Another owner can take over.</summary>
    public int? DrawerUserId { get; set; }

    /// <summary>Draw by itself every this many seconds; null = the drawer taps for each name.</summary>
    public int? AutoSeconds { get; set; }

    /// <summary>When the next name is drawn by itself (with <see cref="AutoSeconds"/> on).</summary>
    public DateTime? NextAutoAt { get; set; }

    /// <summary>How many times it has been drawn (Start again / Shuffle again), shown so a redraw is never hidden.</summary>
    public int Times { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? ConfirmedAt { get; set; }
}

/// <summary>One name drawn: a pool player placed in a team. Hidden from everyone until <see cref="LandsAt"/>.</summary>
public class DrawPick
{
    public int Id { get; set; }
    public int DrawId { get; set; }

    /// <summary>The draw's tournament, copied so a pool player's status is one indexed read.</summary>
    public int TournamentId { get; set; }

    public int PoolPlayerId { get; set; }

    /// <summary>No foreign key: the tournament already cascades to teams and (through the draw) to this.</summary>
    public int TeamId { get; set; }

    /// <summary>1, 2, 3… in the order drawn.</summary>
    public int Order { get; set; }

    /// <summary>The pot it came from: the player's tier when drawn (null = no tier, or a draw at random).</summary>
    public int? TierId { get; set; }

    public DateTime PickedAt { get; set; }

    /// <summary>When the name is shown: the names spin on every screen until then.</summary>
    public DateTime LandsAt { get; set; }

    /// <summary>The player has been told ("You were drawn for Lions"), at landing.</summary>
    public bool Announced { get; set; }

    /// <summary>Moved by hand after the draw: the team it first went to (null = where it was drawn).</summary>
    public int? MovedFromTeamId { get; set; }
}

// --- Requests -------------------------------------------------------------------

public class PoolMethodRequest
{
    public PoolMethod Method { get; set; }
}

public class DrawSettingsRequest
{
    public DrawKind? Kind { get; set; }

    /// <summary>Sets <see cref="AutoSeconds"/> when true (null = tap for each name).</summary>
    public bool SetPace { get; set; }
    public int? AutoSeconds { get; set; }
}

public class DrawRestRequest
{
    /// <summary>Everyone left; otherwise the rest of the current tier.</summary>
    public bool All { get; set; }
}

public class DrawMoveRequest
{
    public int PoolPlayerId { get; set; }
    public int TeamId { get; set; }
}

public class DrawSwapRequest
{
    public int PoolPlayerId { get; set; }
    public int OtherPoolPlayerId { get; set; }
}

public class AuctionMoveRequest
{
    public int LotId { get; set; }
    public int TeamId { get; set; }

    /// <summary>What the new team pays: the price paid, or lower (down to 0) to fit its purse.</summary>
    public int? Price { get; set; }
}

public class AuctionSwapRequest
{
    public int LotId { get; set; }
    public int OtherLotId { get; set; }
}

// --- Answers ----------------------------------------------------------------------

/// <summary>Everything any screen of the draw shows, as the caller sees it. One small read, fetched on every nudge.</summary>
public class DrawStateDto
{
    /// <summary>"None" before a draw exists, then <see cref="DrawStatus"/>.</summary>
    public string Status { get; set; } = "None";

    /// <summary>Everyone has been drawn (and landed): only moves and confirming are left.</summary>
    public bool AllDrawn { get; set; }

    /// <summary>The server's clock: phones spin until <c>spin.landsAt</c> by this, not their own.</summary>
    public DateTime ServerNow { get; set; }

    /// <summary>"Draw", "Auction" or null (not chosen yet): the pool's way of filling the teams.</summary>
    public PoolMethod? Method { get; set; }

    public DrawKind Kind { get; set; }
    public int? AutoSeconds { get; set; }
    public int Times { get; set; }
    public PersonDto? Drawer { get; set; }
    public DrawMeDto Me { get; set; } = new();

    /// <summary>In the order round the table once started (alphabetical before).</summary>
    public List<DrawTeamDto> Teams { get; set; } = [];

    /// <summary>A name being drawn: the team and the pot are known, the name isn't until it lands.</summary>
    public DrawSpinDto? Spin { get; set; }

    /// <summary>The latest name to land, so every phone can show it as it does.</summary>
    public DrawPickDto? Last { get; set; }

    /// <summary>The names still in the current pot (the one being drawn from included until it lands).</summary>
    public DrawPotDto? Pot { get; set; }

    /// <summary>The team whose turn is next ("Next: Falcons draw").</summary>
    public int? NextTeamId { get; set; }

    public DrawCountsDto Counts { get; set; } = new();

    /// <summary>Before the start: what will happen ("23 players for 4 teams. Every team ends with 7.").</summary>
    public DrawPreviewDto? Preview { get; set; }

    public string? StartProblem { get; set; }
    public DrawActionsDto Actions { get; set; } = new();
}

public class DrawMeDto
{
    public bool IsOwner { get; set; }
    public bool IsDrawer { get; set; }
    public int? PoolPlayerId { get; set; }

    /// <summary>The team the caller has been drawn for, once it has landed.</summary>
    public int? DrawnForTeamId { get; set; }
}

public class DrawTeamDto
{
    public int TeamId { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>1, 2, 3… round the table once started.</summary>
    public int? Turn { get; set; }

    /// <summary>Everyone in it now: the players already there and the names landed.</summary>
    public int Players { get; set; }

    /// <summary>Players in the team before the draw (the captain, anyone added by hand).</summary>
    public List<string> Kept { get; set; } = [];

    /// <summary>Names landed in it, in the order drawn.</summary>
    public List<DrawPickDto> Drawn { get; set; } = [];

    /// <summary>A name is being drawn for this team right now.</summary>
    public bool IsDrawing { get; set; }
}

public class DrawPickDto
{
    public int PickId { get; set; }
    public int PoolPlayerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Role { get; set; }
    public string? TierName { get; set; }
    public int TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public int Order { get; set; }
    public DateTime LandedAt { get; set; }
    public string? MovedFromTeamName { get; set; }
    public bool IsMe { get; set; }
}

public class DrawSpinDto
{
    public int PickId { get; set; }
    public int TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public DateTime PickedAt { get; set; }
    public DateTime LandsAt { get; set; }
    public string PotName { get; set; } = string.Empty;

    /// <summary>The names the spinner shows: everyone in the pot when it was drawn, the one drawn among them.</summary>
    public List<string> Names { get; set; } = [];
}

public class DrawPotDto
{
    /// <summary>"Tier A", "No tier", or "Everyone left" in a draw at random.</summary>
    public string Name { get; set; } = string.Empty;
    public List<DrawPotPlayerDto> Players { get; set; } = [];

    /// <summary>The pot after this one, if any: "Tier B", 8.</summary>
    public string? NextName { get; set; }
    public int NextCount { get; set; }
}

public class DrawPotPlayerDto
{
    public int PoolPlayerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Role { get; set; }
}

public class DrawCountsDto
{
    /// <summary>In the draw (the pool, not withdrawn).</summary>
    public int Players { get; set; }

    /// <summary>Landed so far.</summary>
    public int Drawn { get; set; }

    /// <summary>Still to draw (the one spinning included).</summary>
    public int Left { get; set; }
}

public class DrawPreviewDto
{
    /// <summary>"23 players for 4 teams. Every team ends with 7."</summary>
    public string Line { get; set; } = string.Empty;

    /// <summary>A team that already has more than others: "Lions already have 2, so they sit out a turn until the others catch up."</summary>
    public string? Note { get; set; }

    public List<DrawPreviewTeamDto> Teams { get; set; } = [];
    public List<DrawPreviewTierDto> Tiers { get; set; } = [];
}

public class DrawPreviewTeamDto
{
    public int TeamId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Has { get; set; }
    public int Gets { get; set; }
    public int EndsWith { get; set; }

    /// <summary>It may get one more than this (the totals are uneven and the order decides who).</summary>
    public bool OrOneMore { get; set; }
}

public class DrawPreviewTierDto
{
    public string Name { get; set; } = string.Empty;
    public int Players { get; set; }
}

public class DrawActionsDto
{
    /// <summary>Draw or Auction may be chosen (neither has started).</summary>
    public bool CanChooseMethod { get; set; }
    public bool CanEditKind { get; set; }
    public bool CanEditPace { get; set; }
    public bool CanStart { get; set; }
    public bool CanQuickDraw { get; set; }
    public bool CanDrawNext { get; set; }
    public bool CanDrawRestOfPot { get; set; }
    public bool CanDrawAll { get; set; }
    public bool CanRestart { get; set; }
    public bool CanTakeOver { get; set; }
    public bool CanMove { get; set; }
    public bool CanConfirm { get; set; }

    /// <summary>Why "Confirm teams" is off: "5 players are still to be drawn."</summary>
    public string? ConfirmProblem { get; set; }
}
