using Microsoft.AspNetCore.Mvc;
using TournamentScheduler.Api.Auth.Permissions;
using TournamentScheduler.Api.Auth.TournamentAccess;
using TournamentScheduler.Api.Gateway;
using TournamentScheduler.Api.Http;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Models.Auctions;
using TournamentScheduler.Api.Services.Access;
using TournamentScheduler.Api.Services.Auctions;

namespace TournamentScheduler.Api.Controllers;

/// <summary>
/// A tournament's player pool (2026-11-17): everyone who can see the tournament sees it; owners add
/// players (by email or number, or a name), sort them into tiers and open registration by code.
/// Every change answers with the whole pool.
/// </summary>
[ApiController]
[Route("api/tournaments/{id}/pool")]
[DefaultRoles(RoleIds.EndUser)]
public class PoolController(IPoolService pool) : ApiController
{
    // GET api/tournaments/5/pool
    [HttpGet]
    [ServiceRequestId("POOL_GET")]
    [TournamentAccess(TournamentPermission.View)]
    public async Task<ActionResult<ApiResponse<PoolDto>>> Get(int id) => Respond(await pool.GetAsync(id));

    // POST api/tournaments/5/pool/lookup { contact } — who an email or number belongs to
    [HttpPost("lookup")]
    [ServiceRequestId("POOL_LOOKUP")]
    [TournamentAccess(TournamentPermission.Edit)]
    public async Task<ActionResult<ApiResponse<PlayerLookupDto>>> Lookup(int id, [FromBody] PoolLookupRequest request) =>
        Respond(await pool.LookupAsync(id, request));

    // POST api/tournaments/5/pool/players { email | phone | name, position, cricketRole, tierId }
    [HttpPost("players")]
    [ServiceRequestId("POOL_PLAYER_ADD")]
    [TournamentAccess(TournamentPermission.Edit)]
    public async Task<ActionResult<ApiResponse<PoolDto>>> Add(int id, [FromBody] PoolPlayerRequest request) =>
        Respond(await pool.AddAsync(id, request));

    // PUT api/tournaments/5/pool/players/9 { name, position, cricketRole, tierId + setTier, withdrawn }
    [HttpPut("players/{poolPlayerId}")]
    [ServiceRequestId("POOL_PLAYER_UPDATE")]
    [TournamentAccess(TournamentPermission.Edit)]
    public async Task<ActionResult<ApiResponse<PoolDto>>> Update(int id, int poolPlayerId, [FromBody] PoolPlayerUpdateRequest request) =>
        Respond(await pool.UpdateAsync(id, poolPlayerId, request));

    // DELETE api/tournaments/5/pool/players/9 (before the auction starts; withdraw after)
    [HttpDelete("players/{poolPlayerId}")]
    [ServiceRequestId("POOL_PLAYER_REMOVE")]
    [TournamentAccess(TournamentPermission.Edit)]
    public async Task<ActionResult<ApiResponse<PoolDto>>> Remove(int id, int poolPlayerId) =>
        Respond(await pool.RemoveAsync(id, poolPlayerId));

    // POST api/tournaments/5/pool/move { playerIds, tierId } — several players to one tier
    [HttpPost("move")]
    [ServiceRequestId("POOL_MOVE")]
    [TournamentAccess(TournamentPermission.Edit)]
    public async Task<ActionResult<ApiResponse<PoolDto>>> Move(int id, [FromBody] PoolMoveRequest request) =>
        Respond(await pool.MoveAsync(id, request));

    // PUT api/tournaments/5/pool/tiers { tiers: [{ id?, name, basePrice, maxPerTeam }] } — the whole set, top first
    [HttpPut("tiers")]
    [ServiceRequestId("POOL_TIERS_SAVE")]
    [TournamentAccess(TournamentPermission.Edit)]
    public async Task<ActionResult<ApiResponse<PoolDto>>> Tiers(int id, [FromBody] PoolTiersRequest request) =>
        Respond(await pool.SaveTiersAsync(id, request));

    // POST api/tournaments/5/pool/registration { open } — players register themselves with the pool's code
    [HttpPost("registration")]
    [ServiceRequestId("POOL_REGISTRATION")]
    [TournamentAccess(TournamentPermission.Edit)]
    public async Task<ActionResult<ApiResponse<PoolDto>>> Registration(int id, [FromBody] PoolRegistrationRequest request) =>
        Respond(await pool.RegistrationAsync(id, request));

    // POST api/tournaments/5/pool/method { method: Draw | Auction } — until either way starts
    [HttpPost("method")]
    [ServiceRequestId("POOL_METHOD_SET")]
    [TournamentAccess(TournamentPermission.Edit)]
    public async Task<ActionResult<ApiResponse<PoolDto>>> Method(int id, [FromBody] PoolMethodRequest request) =>
        Respond(await pool.MethodAsync(id, request));
}

/// <summary>
/// Registering for a player pool with its code. Not tournament-scoped: the person isn't in the
/// tournament yet — the code is what finds it.
/// </summary>
[ApiController]
[Route("api/pool/join")]
[DefaultRoles(RoleIds.EndUser)]
[NotTournamentScoped]
public class PoolJoinController(IPoolService pool) : ApiController
{
    // POST api/pool/join/preview { code } → the tournament
    [HttpPost("preview")]
    [ServiceRequestId("POOL_JOIN_PREVIEW")]
    public async Task<ActionResult<ApiResponse<PoolJoinPreviewDto>>> Preview([FromBody] PoolJoinRequest request) =>
        Respond(await pool.JoinPreviewAsync(request));

    // POST api/pool/join { code }
    [HttpPost]
    [ServiceRequestId("POOL_JOIN")]
    public async Task<ActionResult<ApiResponse<PoolJoinResult>>> Join([FromBody] PoolJoinRequest request) =>
        Respond(await pool.JoinAsync(request));
}

/// <summary>
/// The auction (2026-11-17). Anyone who can see the tournament watches it; a team's bidder bids
/// (with View: they may be no member at all); the auctioneer, an owner who bids for no team, runs
/// it. Every action answers with the whole state as the caller sees it.
/// </summary>
[ApiController]
[Route("api/tournaments/{id}/auction")]
[DefaultRoles(RoleIds.EndUser)]
public class AuctionController(IAuctionService auction) : ApiController
{
    // GET api/tournaments/5/auction — the state; "None" before one is set up
    [HttpGet]
    [ServiceRequestId("AUCTION_GET")]
    [TournamentAccess(TournamentPermission.View)]
    public async Task<ActionResult<ApiResponse<AuctionStateDto>>> Get(int id) => Respond(await auction.GetAsync(id));

    // PUT api/tournaments/5/auction/settings — before the start; only the clock while paused
    [HttpPut("settings")]
    [ServiceRequestId("AUCTION_SETTINGS")]
    [TournamentAccess(TournamentPermission.Edit)]
    public async Task<ActionResult<ApiResponse<AuctionStateDto>>> Settings(int id, [FromBody] AuctionSettingsRequest request) =>
        Respond(await auction.SettingsAsync(id, request));

    // POST api/tournaments/5/auction/bidder { teamId, playerId | contact | none } — before the start, or replacing one while paused
    [HttpPost("bidder")]
    [ServiceRequestId("AUCTION_BIDDER_SET")]
    [TournamentAccess(TournamentPermission.Edit)]
    public async Task<ActionResult<ApiResponse<AuctionStateDto>>> Bidder(int id, [FromBody] AuctionBidderRequest request) =>
        Respond(await auction.BidderAsync(id, request));

    // POST api/tournaments/5/auction/start — the caller becomes the auctioneer
    [HttpPost("start")]
    [ServiceRequestId("AUCTION_START")]
    [TournamentAccess(TournamentPermission.Edit)]
    public async Task<ActionResult<ApiResponse<AuctionStateDto>>> Start(int id) => Respond(await auction.StartAsync(id));

    // POST api/tournaments/5/auction/next { lotId? } — the next player under the hammer
    [HttpPost("next")]
    [ServiceRequestId("AUCTION_NEXT")]
    [TournamentAccess(TournamentPermission.Edit)]
    public async Task<ActionResult<ApiResponse<AuctionStateDto>>> Next(int id, [FromBody] AuctionNextRequest request) =>
        Respond(await auction.NextAsync(id, request));

    // POST api/tournaments/5/auction/bid { teamId, amount } — the team's bidder, or the auctioneer for a bid in the room
    [HttpPost("bid")]
    [ServiceRequestId("AUCTION_BID")]
    [TournamentAccess(TournamentPermission.View)]
    public async Task<ActionResult<ApiResponse<AuctionStateDto>>> Bid(int id, [FromBody] AuctionBidRequest request) =>
        Respond(await auction.BidAsync(id, request));

    // POST api/tournaments/5/auction/sell
    [HttpPost("sell")]
    [ServiceRequestId("AUCTION_SELL")]
    [TournamentAccess(TournamentPermission.Edit)]
    public async Task<ActionResult<ApiResponse<AuctionStateDto>>> Sell(int id) => Respond(await auction.SellAsync(id));

    // POST api/tournaments/5/auction/unsold
    [HttpPost("unsold")]
    [ServiceRequestId("AUCTION_UNSOLD")]
    [TournamentAccess(TournamentPermission.Edit)]
    public async Task<ActionResult<ApiResponse<AuctionStateDto>>> Unsold(int id) => Respond(await auction.UnsoldAsync(id));

    // POST api/tournaments/5/auction/undo-bid
    [HttpPost("undo-bid")]
    [ServiceRequestId("AUCTION_UNDO_BID")]
    [TournamentAccess(TournamentPermission.Edit)]
    public async Task<ActionResult<ApiResponse<AuctionStateDto>>> UndoBid(int id) => Respond(await auction.UndoBidAsync(id));

    // POST api/tournaments/5/auction/undo-sale { lotId }
    [HttpPost("undo-sale")]
    [ServiceRequestId("AUCTION_UNDO_SALE")]
    [TournamentAccess(TournamentPermission.Edit)]
    public async Task<ActionResult<ApiResponse<AuctionStateDto>>> UndoSale(int id, [FromBody] AuctionLotRequest request) =>
        Respond(await auction.UndoSaleAsync(id, request));

    // POST api/tournaments/5/auction/pause
    [HttpPost("pause")]
    [ServiceRequestId("AUCTION_PAUSE")]
    [TournamentAccess(TournamentPermission.Edit)]
    public async Task<ActionResult<ApiResponse<AuctionStateDto>>> Pause(int id) => Respond(await auction.PauseAsync(id));

    // POST api/tournaments/5/auction/resume
    [HttpPost("resume")]
    [ServiceRequestId("AUCTION_RESUME")]
    [TournamentAccess(TournamentPermission.Edit)]
    public async Task<ActionResult<ApiResponse<AuctionStateDto>>> Resume(int id) => Respond(await auction.ResumeAsync(id));

    // POST api/tournaments/5/auction/take-over — another owner runs it from here
    [HttpPost("take-over")]
    [ServiceRequestId("AUCTION_TAKE_OVER")]
    [TournamentAccess(TournamentPermission.Edit)]
    public async Task<ActionResult<ApiResponse<AuctionStateDto>>> TakeOver(int id) => Respond(await auction.TakeOverAsync(id));

    // POST api/tournaments/5/auction/round { prices: [{ tierId, price }] } — another round of the unsold
    [HttpPost("round")]
    [ServiceRequestId("AUCTION_ROUND_START")]
    [TournamentAccess(TournamentPermission.Edit)]
    public async Task<ActionResult<ApiResponse<AuctionStateDto>>> Round(int id, [FromBody] AuctionRoundRequest request) =>
        Respond(await auction.StartRoundAsync(id, request));

    // POST api/tournaments/5/auction/finish — no more rounds
    [HttpPost("finish")]
    [ServiceRequestId("AUCTION_FINISH")]
    [TournamentAccess(TournamentPermission.Edit)]
    public async Task<ActionResult<ApiResponse<AuctionStateDto>>> Finish(int id) => Respond(await auction.FinishAsync(id));

    // POST api/tournaments/5/auction/assign { poolPlayerId, teamId, price } — an unsold player, by hand
    [HttpPost("assign")]
    [ServiceRequestId("AUCTION_ASSIGN")]
    [TournamentAccess(TournamentPermission.Edit)]
    public async Task<ActionResult<ApiResponse<AuctionStateDto>>> Assign(int id, [FromBody] AuctionAssignRequest request) =>
        Respond(await auction.AssignAsync(id, request));

    // POST api/tournaments/5/auction/confirm — every sale becomes a squad place
    [HttpPost("confirm")]
    [ServiceRequestId("AUCTION_CONFIRM")]
    [TournamentAccess(TournamentPermission.Edit)]
    public async Task<ActionResult<ApiResponse<AuctionStateDto>>> Confirm(int id) => Respond(await auction.ConfirmAsync(id));

    // POST api/tournaments/5/auction/cancel — everyone back to the pool
    [HttpPost("cancel")]
    [ServiceRequestId("AUCTION_CANCEL")]
    [TournamentAccess(TournamentPermission.Edit)]
    public async Task<ActionResult<ApiResponse<AuctionStateDto>>> Cancel(int id) => Respond(await auction.CancelAsync(id));

    // POST api/tournaments/5/auction/move { lotId, teamId, price? } — a bought player to another team (finished, before confirming)
    [HttpPost("move")]
    [ServiceRequestId("AUCTION_MOVE")]
    [TournamentAccess(TournamentPermission.Edit)]
    public async Task<ActionResult<ApiResponse<AuctionStateDto>>> Move(int id, [FromBody] AuctionMoveRequest request) =>
        Respond(await auction.MoveAsync(id, request));

    // POST api/tournaments/5/auction/swap { lotId, otherLotId } — two bought players change teams, keeping their prices
    [HttpPost("swap")]
    [ServiceRequestId("AUCTION_SWAP")]
    [TournamentAccess(TournamentPermission.Edit)]
    public async Task<ActionResult<ApiResponse<AuctionStateDto>>> Swap(int id, [FromBody] AuctionSwapRequest request) =>
        Respond(await auction.SwapAsync(id, request));

    // GET api/tournaments/5/auction/fill — what "Fill the gaps by draw" will do: how many each short team gets (plan 9.7)
    [HttpGet("fill")]
    [ServiceRequestId("AUCTION_FILL_PREVIEW")]
    [TournamentAccess(TournamentPermission.Edit)]
    public async Task<ActionResult<ApiResponse<AuctionFillPreviewDto>>> FillPreview(int id) =>
        Respond(await auction.FillPreviewAsync(id));

    // POST api/tournaments/5/auction/fill — deal the unsold players to the teams short of the minimum, by the draw's rule
    [HttpPost("fill")]
    [ServiceRequestId("AUCTION_FILL")]
    [TournamentAccess(TournamentPermission.Edit)]
    public async Task<ActionResult<ApiResponse<AuctionStateDto>>> Fill(int id) =>
        Respond(await auction.FillAsync(id));
}

/// <summary>
/// The team draw (2026-11-17): the other way to fill teams from the pool. Anyone who can see the
/// tournament watches; an owner draws (one at a time, like the auctioneer). Each name is picked by the
/// server at the tap and stays hidden until it lands. Every action answers with the whole state.
/// </summary>
[ApiController]
[Route("api/tournaments/{id}/draw")]
[DefaultRoles(RoleIds.EndUser)]
public class DrawController(IDrawService draw) : ApiController
{
    // GET api/tournaments/5/draw — the state; "None" before one is set up
    [HttpGet]
    [ServiceRequestId("DRAW_GET")]
    [TournamentAccess(TournamentPermission.View)]
    public async Task<ActionResult<ApiResponse<DrawStateDto>>> Get(int id) => Respond(await draw.GetAsync(id));

    // PUT api/tournaments/5/draw/settings { kind?, setPace, autoSeconds? } — kind before the start; pace any time
    [HttpPut("settings")]
    [ServiceRequestId("DRAW_SETTINGS")]
    [TournamentAccess(TournamentPermission.Edit)]
    public async Task<ActionResult<ApiResponse<DrawStateDto>>> Settings(int id, [FromBody] DrawSettingsRequest request) =>
        Respond(await draw.SettingsAsync(id, request));

    // POST api/tournaments/5/draw/start — the live draw; the caller draws
    [HttpPost("start")]
    [ServiceRequestId("DRAW_START")]
    [TournamentAccess(TournamentPermission.Edit)]
    public async Task<ActionResult<ApiResponse<DrawStateDto>>> Start(int id) => Respond(await draw.StartAsync(id));

    // POST api/tournaments/5/draw/next — the next name (lands a few seconds later)
    [HttpPost("next")]
    [ServiceRequestId("DRAW_NEXT")]
    [TournamentAccess(TournamentPermission.Edit)]
    public async Task<ActionResult<ApiResponse<DrawStateDto>>> Next(int id) => Respond(await draw.NextAsync(id));

    // POST api/tournaments/5/draw/rest { all } — the rest of this tier, or everyone left, at once
    [HttpPost("rest")]
    [ServiceRequestId("DRAW_REST")]
    [TournamentAccess(TournamentPermission.Edit)]
    public async Task<ActionResult<ApiResponse<DrawStateDto>>> Rest(int id, [FromBody] DrawRestRequest request) =>
        Respond(await draw.RestAsync(id, request));

    // POST api/tournaments/5/draw/quick — the whole draw in one go ("Shuffle again" once drawn)
    [HttpPost("quick")]
    [ServiceRequestId("DRAW_QUICK")]
    [TournamentAccess(TournamentPermission.Edit)]
    public async Task<ActionResult<ApiResponse<DrawStateDto>>> Quick(int id) => Respond(await draw.QuickAsync(id));

    // POST api/tournaments/5/draw/restart — everyone back in the pool
    [HttpPost("restart")]
    [ServiceRequestId("DRAW_RESTART")]
    [TournamentAccess(TournamentPermission.Edit)]
    public async Task<ActionResult<ApiResponse<DrawStateDto>>> Restart(int id) => Respond(await draw.RestartAsync(id));

    // POST api/tournaments/5/draw/take-over — another owner draws from here
    [HttpPost("take-over")]
    [ServiceRequestId("DRAW_TAKE_OVER")]
    [TournamentAccess(TournamentPermission.Edit)]
    public async Task<ActionResult<ApiResponse<DrawStateDto>>> TakeOver(int id) => Respond(await draw.TakeOverAsync(id));

    // POST api/tournaments/5/draw/move { poolPlayerId, teamId } — marked "moved" for everyone
    [HttpPost("move")]
    [ServiceRequestId("DRAW_MOVE")]
    [TournamentAccess(TournamentPermission.Edit)]
    public async Task<ActionResult<ApiResponse<DrawStateDto>>> Move(int id, [FromBody] DrawMoveRequest request) =>
        Respond(await draw.MoveAsync(id, request));

    // POST api/tournaments/5/draw/swap { poolPlayerId, otherPoolPlayerId }
    [HttpPost("swap")]
    [ServiceRequestId("DRAW_SWAP")]
    [TournamentAccess(TournamentPermission.Edit)]
    public async Task<ActionResult<ApiResponse<DrawStateDto>>> Swap(int id, [FromBody] DrawSwapRequest request) =>
        Respond(await draw.SwapAsync(id, request));

    // POST api/tournaments/5/draw/confirm — every name drawn becomes a squad place
    [HttpPost("confirm")]
    [ServiceRequestId("DRAW_CONFIRM")]
    [TournamentAccess(TournamentPermission.Edit)]
    public async Task<ActionResult<ApiResponse<DrawStateDto>>> Confirm(int id) => Respond(await draw.ConfirmAsync(id));
}
