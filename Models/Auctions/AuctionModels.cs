using System.Text.Json.Serialization;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Models.Cricket;

namespace TournamentScheduler.Api.Models.Auctions;

// The player pool and the auction (2026-11-17, docs/player-pool-and-auction-plan.md). Before a
// tournament starts, its organiser can put players in a pool instead of straight into teams; each
// team's bidder then buys them live with a fixed purse. "Confirm teams" turns every sale into an
// ordinary squad place, so scoring, stats and profiles never know there was an auction.
//
// The bids are the ledger: a team's purse and squad are worked out from the sold lots every time,
// never kept as running totals, so undoing a bid or a sale is just "mark it and work it out again".

// --- The pool -------------------------------------------------------------------

/// <summary>A tournament's pool settings: the code players register with, and whether it works now.</summary>
public class PlayerPool
{
    public int TournamentId { get; set; }

    /// <summary>
    /// Seven characters a player types under "Join" to register (team codes are six, so one field
    /// takes both). Never serialised: owners see it in the pool's answer.
    /// </summary>
    public string? JoinCode { get; set; }

    /// <summary>Registration by code is open. Owners can add players either way.</summary>
    public bool IsOpen { get; set; }

    /// <summary>Draw or auction (null = not chosen yet). Fixed once that way starts (E-1).</summary>
    public PoolMethod? Method { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>An ordered group of pool players with a base price ("Marquee", "A", "B").</summary>
public class PoolTier
{
    public int Id { get; set; }
    public int TournamentId { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>1 is the top tier; the auction opens tiers in this order.</summary>
    public int Order { get; set; }

    /// <summary>The first bid on a player of this tier.</summary>
    public int BasePrice { get; set; }

    /// <summary>The most players one team may buy from this tier; null = no limit (agreed 2026-11-17, D-9).</summary>
    public int? MaxPerTeam { get; set; }
}

/// <summary>
/// Someone in the pool, waiting for a team. Linked to an account the same way a squad place is
/// (by email or number, or registering with the pool's code), and becomes a squad place when the
/// teams are confirmed.
/// </summary>
public class PoolPlayer
{
    public int Id { get; set; }
    public int TournamentId { get; set; }

    /// <summary>The name to show; a linked player's is kept equal to their account's.</summary>
    public string Name { get; set; } = string.Empty;

    public int? UserId { get; set; }
    public string? EmailId { get; set; }
    public string? PhoneNumber { get; set; }

    /// <summary>Football: a free-text position.</summary>
    public string? Position { get; set; }

    /// <summary>Cricket: what they are, for someone without an account (a linked player's comes from their profile).</summary>
    public CricketRole? CricketRole { get; set; }

    public int? TierId { get; set; }

    /// <summary>Their place in the pool list (the auction's order when it goes "in the order of the list").</summary>
    public int Order { get; set; }

    public bool JoinedByCode { get; set; }

    /// <summary>Pulled out by the organiser: skipped by the auction, kept in the list.</summary>
    public bool IsWithdrawn { get; set; }

    /// <summary>The squad place made for them when the teams were confirmed.</summary>
    public int? PlayerId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int CreatedByUserId { get; set; }
}

// --- The auction -----------------------------------------------------------------

public enum AuctionStatus
{
    /// <summary>Rules and bidders being set; the pool can change.</summary>
    Setup = 0,
    /// <summary>Running: the auctioneer opens players one at a time.</summary>
    Live = 1,
    /// <summary>The clock is stopped; the auctioneer may still record a bid from the room.</summary>
    Paused = 2,
    /// <summary>Every player in the round has been opened; the auctioneer chooses another round or the end.</summary>
    RoundOver = 3,
    /// <summary>No more rounds: players left unsold may be assigned by hand, then the teams confirmed.</summary>
    Finished = 4,
    /// <summary>Squads made. Nothing here changes any more.</summary>
    Confirmed = 5
}

public enum AuctionOrder
{
    /// <summary>Top tier first, shuffled inside each tier (the default).</summary>
    ByTier = 0,
    /// <summary>The pool list as the organiser ordered it.</summary>
    ListOrder = 1
}

public enum UnsoldPlan
{
    /// <summary>Offer another round of the unsold players first.</summary>
    AnotherRound = 0,
    /// <summary>Offer ending and assigning them by hand first.</summary>
    AssignByHand = 1
}

/// <summary>One tournament's auction: its rules, where it stands, and who runs it.</summary>
public class Auction
{
    public int Id { get; set; }
    public int TournamentId { get; set; }

    public AuctionStatus Status { get; set; } = AuctionStatus.Setup;

    /// <summary>"pts" or "₹" — only a label; the app never handles money (D-5).</summary>
    public string Unit { get; set; } = "pts";

    public int Purse { get; set; } = 10_000;

    /// <summary>Squad sizes counting the players already in a team (the captain).</summary>
    public int MinSquad { get; set; } = 11;
    public int MaxSquad { get; set; } = 15;

    /// <summary>What each player already in a team before the auction costs its purse (default nothing).</summary>
    public int KeptPlayerPrice { get; set; }

    /// <summary>The base price of a pool player in no tier.</summary>
    public int NoTierBasePrice { get; set; } = 100;

    /// <summary>The bid ladder, as text: "1000:50,3000:100,0:250" (up to 1,000 by 50 … above by 250). See <c>BidLadder</c>.</summary>
    public string BidSteps { get; set; } = "1000:50,3000:100,0:250";

    public bool ClockOn { get; set; } = true;
    public int FirstBidSeconds { get; set; } = 20;
    public int NextBidSeconds { get; set; } = 10;

    public AuctionOrder Order { get; set; } = AuctionOrder.ByTier;
    public UnsoldPlan UnsoldPlan { get; set; } = UnsoldPlan.AnotherRound;

    /// <summary>1 for everyone, then one more for each round of the unsold.</summary>
    public int Round { get; set; }

    /// <summary>Whoever runs it now (an owner who isn't a bidder): only they open, sell and undo.</summary>
    public int? AuctioneerUserId { get; set; }

    /// <summary>The player under the hammer.</summary>
    public int? OpenLotId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public DateTime? ConfirmedAt { get; set; }
}

/// <summary>A team in the auction: who bids for it, and how many players it had before.</summary>
public class AuctionTeam
{
    public int Id { get; set; }
    public int AuctionId { get; set; }

    /// <summary>The auction's tournament, copied so "may this bidder see the tournament" is one indexed read.</summary>
    public int TournamentId { get; set; }

    /// <summary>No foreign key: the tournament already cascades to teams and (through the auction) to this.</summary>
    public int TeamId { get; set; }

    /// <summary>Bids from their own phone. Null: the auctioneer records this team's bids from the room.</summary>
    public int? BidderUserId { get; set; }

    /// <summary>Players already in the team when the auction started (they count towards its squad).</summary>
    public int KeptPlayers { get; set; }
}

public enum AuctionLotStatus
{
    /// <summary>Still to be opened in its round.</summary>
    Waiting = 0,
    /// <summary>Under the hammer.</summary>
    Open = 1,
    Sold = 2,
    Unsold = 3,
    /// <summary>A sale undone, or the player withdrawn: kept for the record, worth nothing.</summary>
    Returned = 4
}

/// <summary>A pool player offered once, in one round. A sale is the lot's last bid standing.</summary>
public class AuctionLot
{
    public int Id { get; set; }
    public int AuctionId { get; set; }
    public int PoolPlayerId { get; set; }
    public int Round { get; set; }

    /// <summary>Its turn in the round.</summary>
    public int Order { get; set; }

    /// <summary>The first bid, fixed for this round (a later round may lower it, never raise it — D-7).</summary>
    public int BasePrice { get; set; }

    public AuctionLotStatus Status { get; set; }

    public DateTime? OpenedAt { get; set; }

    /// <summary>When the bid clock sells it; null with the clock off, or while paused.</summary>
    public DateTime? ClosesAt { get; set; }

    /// <summary>What was left on the clock when the auction paused.</summary>
    public int? PausedMsLeft { get; set; }

    public DateTime? ClosedAt { get; set; }

    /// <summary>Sold: the buyer and the price (the winning bid, copied when the lot closed).</summary>
    public int? SoldTeamId { get; set; }
    public int? Price { get; set; }

    /// <summary>Given to a team by hand after the rounds, not bid for.</summary>
    public bool Assigned { get; set; }

    /// <summary>Moved or swapped to another team after the auction (E-4): the team that bought them.</summary>
    public int? MovedFromTeamId { get; set; }

    /// <summary>Dealt to a team by "Fill the gaps by draw" (plan 9.7), not bid for or picked by hand.</summary>
    public bool ByDraw { get; set; }

    /// <summary>
    /// The auction was cancelled after this lot (plan 9.7): kept with its bids as history, but no
    /// longer part of the auction. Every read of the auction skips these.
    /// </summary>
    public DateTime? CancelledAt { get; set; }
}

/// <summary>One bid. Undoing it marks it; nothing is deleted, so the log says who bid what.</summary>
public class AuctionBid
{
    public long Id { get; set; }
    public int LotId { get; set; }
    public int TeamId { get; set; }
    public int Amount { get; set; }

    /// <summary>Who tapped: the team's bidder, or the auctioneer recording a bid from the room.</summary>
    public int ByUserId { get; set; }
    public bool FromRoom { get; set; }

    public DateTime At { get; set; }
    public bool IsUndone { get; set; }
}

// --- Requests -------------------------------------------------------------------

public class PoolPlayerRequest
{
    /// <summary>An email or a number with an account adds that person (their name comes from it).</summary>
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Name { get; set; }
    public string? Position { get; set; }
    public CricketRole? CricketRole { get; set; }
    public int? TierId { get; set; }
}

public class PoolPlayerUpdateRequest
{
    public string? Name { get; set; }
    public string? Position { get; set; }
    public CricketRole? CricketRole { get; set; }

    /// <summary>Changes the tier when <see cref="SetTier"/> is true (null = "No tier").</summary>
    public int? TierId { get; set; }
    public bool SetTier { get; set; }

    public bool? Withdrawn { get; set; }
}

public class PoolMoveRequest
{
    public List<int> PlayerIds { get; set; } = [];

    /// <summary>The tier to move them to; null = "No tier".</summary>
    public int? TierId { get; set; }
}

public class PoolTierInput
{
    /// <summary>An existing tier; null for a new one.</summary>
    public int? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int BasePrice { get; set; }
    public int? MaxPerTeam { get; set; }
}

public class PoolTiersRequest
{
    /// <summary>Every tier, top first. A tier left out is deleted; its players go to "No tier".</summary>
    public List<PoolTierInput> Tiers { get; set; } = [];
}

public class PoolRegistrationRequest
{
    public bool Open { get; set; }
}

public class PoolLookupRequest
{
    public string Contact { get; set; } = string.Empty;
}

public class PoolJoinRequest
{
    public string Code { get; set; } = string.Empty;
}

public class BidStepDto
{
    /// <summary>The step applies to bids below this amount; null for "above the last".</summary>
    public int? UpTo { get; set; }
    public int Step { get; set; }
}

public class AuctionSettingsRequest
{
    public string? Unit { get; set; }
    public int? Purse { get; set; }
    public int? MinSquad { get; set; }
    public int? MaxSquad { get; set; }
    public int? KeptPlayerPrice { get; set; }
    public int? NoTierBasePrice { get; set; }
    public List<BidStepDto>? BidSteps { get; set; }
    public bool? ClockOn { get; set; }
    public int? FirstBidSeconds { get; set; }
    public int? NextBidSeconds { get; set; }
    public AuctionOrder? Order { get; set; }
    public UnsoldPlan? UnsoldPlan { get; set; }
}

public class AuctionBidderRequest
{
    public int TeamId { get; set; }

    /// <summary>One of the team's players with an account (the captain, usually).</summary>
    public int? PlayerId { get; set; }

    /// <summary>Or anyone with an account, by email or number.</summary>
    public string? Contact { get; set; }

    /// <summary>No bidder: the auctioneer records this team's bids from the room.</summary>
    public bool None { get; set; }
}

public class AuctionNextRequest
{
    /// <summary>A particular waiting player; the next in order when null.</summary>
    public int? LotId { get; set; }
}

public class AuctionBidRequest
{
    public int TeamId { get; set; }

    /// <summary>
    /// The amount the button showed. Refused if someone else bid in between ("Tigers bid 650 first.
    /// The next bid is 700."), so a tap never pays more than its owner saw.
    /// </summary>
    public int Amount { get; set; }
}

public class AuctionLotRequest
{
    public int LotId { get; set; }
}

public class AuctionRoundPrice
{
    /// <summary>The tier; null = players in no tier.</summary>
    public int? TierId { get; set; }
    public int Price { get; set; }
}

public class AuctionRoundRequest
{
    /// <summary>The new round's base price per tier: the same as before or lower (D-7). Tiers not listed keep theirs.</summary>
    public List<AuctionRoundPrice> Prices { get; set; } = [];
}

public class AuctionAssignRequest
{
    public int PoolPlayerId { get; set; }
    public int TeamId { get; set; }

    /// <summary>Their base price, or lower down to 0 to fit the team's purse (D-8). Never above what the team has left.</summary>
    public int Price { get; set; }
}

// --- Answers ----------------------------------------------------------------------

public class PoolDto
{
    /// <summary>The registration code, for owners only.</summary>
    public string? JoinCode { get; set; }
    public bool RegistrationOpen { get; set; }

    /// <summary>Players can be added, removed and moved between tiers (the auction hasn't started).</summary>
    public bool CanEdit { get; set; }

    /// <summary>Players can still be added (also while the auction runs: they go into the next round).</summary>
    public bool CanAdd { get; set; }

    public List<PoolTierDto> Tiers { get; set; } = [];
    public List<PoolPlayerDto> Players { get; set; } = [];

    /// <summary>Teams and the players already in them (captains): they aren't in the pool.</summary>
    public int Teams { get; set; }
    public int KeptPlayers { get; set; }

    /// <summary>The caller's own pool place, if they're in it.</summary>
    public int? MyPoolPlayerId { get; set; }

    /// <summary>Draw or auction (null = not chosen yet), and whether the owner may still switch.</summary>
    public PoolMethod? Method { get; set; }
    public bool CanChooseMethod { get; set; }

    /// <summary>"None", "Setup", "Live", "Confirmed": where the draw stands.</summary>
    public string DrawStatus { get; set; } = "None";

    /// <summary>"None" or the auction's status ("Confirmed" once its teams are made): with <see cref="DrawStatus"/>, whether there are results to show.</summary>
    public string AuctionStatus { get; set; } = "None";
}

public class PoolTierDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Order { get; set; }
    public int BasePrice { get; set; }
    public int? MaxPerTeam { get; set; }
    public int Players { get; set; }
}

public class PoolPlayerDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>"Batter", "All-rounder", "Defender" — what they play, if known.</summary>
    public string? Role { get; set; }
    public string? Position { get; set; }
    public CricketRole? CricketRole { get; set; }

    public int? TierId { get; set; }
    public bool IsLinked { get; set; }
    public bool JoinedByCode { get; set; }
    public bool IsWithdrawn { get; set; }
    public bool IsMe { get; set; }

    /// <summary>Owners only.</summary>
    public string? Email { get; set; }
    public string? Phone { get; set; }

    /// <summary>"Available", "InAuction", "Sold", "Unsold", "Drawn".</summary>
    public string Status { get; set; } = "Available";
    public int? TeamId { get; set; }
    public string? TeamName { get; set; }
    public int? Price { get; set; }
}

public class PoolJoinPreviewDto
{
    public int TournamentId { get; set; }
    public string TournamentName { get; set; } = string.Empty;
    public Sport Sport { get; set; }
    public int Players { get; set; }
}

public class PoolJoinResult
{
    public int TournamentId { get; set; }
    public string TournamentName { get; set; } = string.Empty;
    public Sport Sport { get; set; }
    public int PoolPlayerId { get; set; }
}

/// <summary>Everything any screen of the auction draws, as the caller sees it. One small read, fetched on every nudge.</summary>
public class AuctionStateDto
{
    /// <summary>"None" before an auction exists, then <see cref="AuctionStatus"/>.</summary>
    public string Status { get; set; } = "None";

    /// <summary>The pool's chosen way of filling the teams (null = not chosen): a "Draw" means no auction.</summary>
    public PoolMethod? Method { get; set; }

    /// <summary>The server's clock: phones count down to <c>lot.closesAt</c> from this, not their own.</summary>
    public DateTime ServerNow { get; set; }

    public AuctionSettingsDto Settings { get; set; } = new();
    public int Round { get; set; }
    public PersonDto? Auctioneer { get; set; }
    public AuctionMeDto Me { get; set; } = new();
    public List<AuctionTeamDto> Teams { get; set; } = [];
    public AuctionLotDto? Lot { get; set; }

    /// <summary>The next few players in the round's order.</summary>
    public List<AuctionUpNextDto> UpNext { get; set; } = [];

    /// <summary>The latest sale (or the latest "unsold"), so every phone can show it as it lands.</summary>
    public AuctionResultDto? Last { get; set; }

    public AuctionCountsDto Counts { get; set; } = new();
    public AuctionActionsDto Actions { get; set; } = new();

    /// <summary>Every sale, once a round is over (results, assigning by hand).</summary>
    public List<AuctionSaleDto>? Sales { get; set; }

    /// <summary>Players left unsold, once a round is over.</summary>
    public List<AuctionUnsoldDto>? Unsold { get; set; }

    /// <summary>Said before the start: what the pool's size means ("At least 8 players won't get a team").</summary>
    public string? StartProblem { get; set; }
    public string? StartWarning { get; set; }

    /// <summary>The pool's tiers (names, prices, limits), for setup and the next round's prices.</summary>
    public List<PoolTierDto> Tiers { get; set; } = [];
}

public class AuctionSettingsDto
{
    public string Unit { get; set; } = "pts";
    public int Purse { get; set; }
    public int MinSquad { get; set; }
    public int MaxSquad { get; set; }
    public int KeptPlayerPrice { get; set; }
    public int NoTierBasePrice { get; set; }
    public List<BidStepDto> BidSteps { get; set; } = [];
    public bool ClockOn { get; set; }
    public int FirstBidSeconds { get; set; }
    public int NextBidSeconds { get; set; }
    public AuctionOrder Order { get; set; }
    public UnsoldPlan UnsoldPlan { get; set; }
}

public class AuctionMeDto
{
    public bool IsOwner { get; set; }
    public bool IsAuctioneer { get; set; }

    /// <summary>The team the caller bids for, if any.</summary>
    public int? BidderForTeamId { get; set; }

    /// <summary>The caller's own pool place, if they're in the pool.</summary>
    public int? PoolPlayerId { get; set; }
}

public class AuctionTeamDto
{
    public int TeamId { get; set; }
    public string Name { get; set; } = string.Empty;
    public PersonDto? Bidder { get; set; }

    /// <summary>The bidder is the team's captain (the default).</summary>
    public bool BidderIsCaptain { get; set; }

    /// <summary>The captain, when the team has one: who bids by default.</summary>
    public string? CaptainName { get; set; }

    /// <summary>The captain's squad place, to make them the bidder again (<c>AUCTION_BIDDER_SET { playerId }</c>).</summary>
    public int? CaptainPlayerId { get; set; }

    /// <summary>The captain has no account, so can't bid from a phone.</summary>
    public bool CaptainWithoutAccount { get; set; }

    public int Players { get; set; }
    public int Spent { get; set; }
    public int PurseLeft { get; set; }

    /// <summary>Players still needed to reach the minimum squad.</summary>
    public int Needs { get; set; }
    public bool IsFull { get; set; }

    /// <summary>The most it may bid now and still afford its minimum squad (4.4).</summary>
    public int MaxBid { get; set; }

    /// <summary>It may bid on the player under the hammer at the next amount.</summary>
    public bool CanBid { get; set; }

    /// <summary>Why not, in words: "Full", "At its Marquee limit", "Can bid up to 450".</summary>
    public string? CantBidReason { get; set; }

    public bool IsHighest { get; set; }
}

public class AuctionLotDto
{
    public int LotId { get; set; }
    public AuctionPlayerDto Player { get; set; } = new();
    public int BasePrice { get; set; }

    /// <summary>The highest bid standing, and whose.</summary>
    public int? CurrentBid { get; set; }
    public int? CurrentTeamId { get; set; }
    public string? CurrentTeamName { get; set; }

    /// <summary>What the next bid must be (the base price for the first).</summary>
    public int NextBid { get; set; }

    /// <summary>The ladder above the next bid, for "+2 steps" and "+5 steps".</summary>
    public int TwoStepsBid { get; set; }
    public int FiveStepsBid { get; set; }

    public DateTime? OpenedAt { get; set; }
    public DateTime? ClosesAt { get; set; }

    /// <summary>Paused: what's left on the clock.</summary>
    public int? SecondsLeftWhenPaused { get; set; }

    /// <summary>"Player 11 of 40" in this round.</summary>
    public int Number { get; set; }
    public int OfTotal { get; set; }

    /// <summary>Newest first, the last few.</summary>
    public List<AuctionBidDto> Bids { get; set; } = [];
}

public class AuctionPlayerDto
{
    public int PoolPlayerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Role { get; set; }
    public string? TierName { get; set; }
    public bool IsLinked { get; set; }
    public bool IsMe { get; set; }
}

public class AuctionBidDto
{
    public long BidId { get; set; }
    public int TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public int Amount { get; set; }
    public DateTime At { get; set; }
    public bool FromRoom { get; set; }
}

public class AuctionUpNextDto
{
    public int LotId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Role { get; set; }
    public string? TierName { get; set; }
    public int BasePrice { get; set; }
}

public class AuctionResultDto
{
    public int LotId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public bool Sold { get; set; }
    public int? TeamId { get; set; }
    public string? TeamName { get; set; }
    public int? Price { get; set; }
    public DateTime At { get; set; }

    /// <summary>The player is the caller.</summary>
    public bool IsMe { get; set; }
}

public class AuctionCountsDto
{
    /// <summary>Players in the auction (not withdrawn).</summary>
    public int Players { get; set; }
    public int Sold { get; set; }
    public int Unsold { get; set; }

    /// <summary>Still to be opened in this round (not counting the one under the hammer).</summary>
    public int Waiting { get; set; }
}

public class AuctionActionsDto
{
    public bool CanEditSettings { get; set; }

    /// <summary>The bid clock may be changed (setup, or while paused).</summary>
    public bool CanEditClock { get; set; }

    /// <summary>Bidders may be set (setup) or replaced (paused).</summary>
    public bool CanSetBidders { get; set; }

    public bool CanStart { get; set; }
    public bool CanOpenNext { get; set; }
    public bool CanSell { get; set; }
    public bool CanMarkUnsold { get; set; }
    public bool CanUndoBid { get; set; }
    public bool CanUndoSale { get; set; }
    public bool CanPause { get; set; }
    public bool CanResume { get; set; }
    public bool CanTakeOver { get; set; }

    /// <summary>The round is over and some went unsold: another round may start.</summary>
    public bool CanStartRound { get; set; }
    public bool CanFinish { get; set; }
    public bool CanAssign { get; set; }
    public bool CanConfirm { get; set; }
    public bool CanCancel { get; set; }

    /// <summary>Bought players may be moved or swapped between teams (finished, before confirming: E-4).</summary>
    public bool CanMoveSales { get; set; }

    /// <summary>Teams are short of the minimum squad and players are unsold: "Fill the gaps by draw" (plan 9.7).</summary>
    public bool CanFill { get; set; }

    /// <summary>"Confirm teams" is off until this is fixed: "Sharks need 2 more players".</summary>
    public string? ConfirmProblem { get; set; }
}

public class AuctionSaleDto
{
    public int LotId { get; set; }
    public int PoolPlayerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Role { get; set; }
    public string? TierName { get; set; }
    public int TeamId { get; set; }
    public int Price { get; set; }
    public int Round { get; set; }
    public bool Assigned { get; set; }

    /// <summary>Dealt by "Fill the gaps by draw".</summary>
    public bool ByDraw { get; set; }

    /// <summary>Moved or swapped after the auction: the team that bought them.</summary>
    public string? MovedFromTeamName { get; set; }
}

public class AuctionUnsoldDto
{
    public int PoolPlayerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Role { get; set; }
    public int? TierId { get; set; }
    public string? TierName { get; set; }

    /// <summary>Their base price in the last round they were offered (or their tier's, if never offered).</summary>
    public int BasePrice { get; set; }
}

/// <summary>
/// What "Fill the gaps by draw" will do (plan 9.7): how many each team short of the minimum squad
/// gets. Which players go where is drawn only when it's done, so the preview can't be gamed.
/// </summary>
public class AuctionFillPreviewDto
{
    /// <summary>"5 unsold players for 2 teams short of 11: Sharks get 2, Tigers get 1."</summary>
    public string Line { get; set; } = string.Empty;

    /// <summary>The prices, and anyone left over: "2 stay unsold."</summary>
    public string Note { get; set; } = string.Empty;

    public List<AuctionFillTeamDto> Teams { get; set; } = [];

    /// <summary>Unsold players still unsold afterwards.</summary>
    public int Left { get; set; }
}

public class AuctionFillTeamDto
{
    public int TeamId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Has { get; set; }
    public int Gets { get; set; }
    public int PurseLeft { get; set; }
}
