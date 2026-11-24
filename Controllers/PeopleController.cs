using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TournamentScheduler.Api.Auth.Permissions;
using TournamentScheduler.Api.Auth.TournamentAccess;
using TournamentScheduler.Api.Gateway;
using TournamentScheduler.Api.Http;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Models.People;
using TournamentScheduler.Api.Services.People;

namespace TournamentScheduler.Api.Controllers;

/// <summary>
/// Following people (2026-11-23, item 6): find someone who plays, follow them or stop, see anyone's followers and who
/// they follow, and remove one of your own followers. A person is {userId}; nothing here is about one tournament.
/// </summary>
[ApiController]
[DefaultRoles(RoleIds.EndUser)]
[NotTournamentScoped]
public class PeopleController : ApiController
{
    private readonly IFollowService _follows;

    public PeopleController(IFollowService follows) => _follows = follows;

    // GET api/people/search?q=roh — people who play, by name (3+ letters); limited per address like the public pages
    [HttpGet("api/people/search")]
    [ServiceRequestId("PEOPLE_SEARCH")]
    [EnableRateLimiting(PublicRateLimit.Policy)]
    public async Task<ActionResult<ApiResponse<List<PersonRowDto>>>> Search([FromQuery] string? q) =>
        Respond(await _follows.SearchAsync(q));

    // POST api/people/12/follow — at once, no request; they're told once
    [HttpPost("api/people/{userId}/follow")]
    [ServiceRequestId("PERSON_FOLLOW")]
    public async Task<ActionResult<ApiResponse<FollowResultDto>>> Follow(int userId) =>
        Respond(await _follows.FollowAsync(userId));

    // DELETE api/people/12/follow — stop following
    [HttpDelete("api/people/{userId}/follow")]
    [ServiceRequestId("PERSON_UNFOLLOW")]
    public async Task<ActionResult<ApiResponse<FollowResultDto>>> Unfollow(int userId) =>
        Respond(await _follows.UnfollowAsync(userId));

    // DELETE api/people/followers/12 — one of your followers stops following you (not told); answers your followers
    [HttpDelete("api/people/followers/{userId}")]
    [ServiceRequestId("PERSON_FOLLOWER_REMOVE")]
    public async Task<ActionResult<ApiResponse<PersonListDto>>> RemoveFollower(int userId) =>
        Respond(await _follows.RemoveFollowerAsync(userId));

    // GET api/people/12/followers
    [HttpGet("api/people/{userId}/followers")]
    [ServiceRequestId("PERSON_FOLLOWERS")]
    public async Task<ActionResult<ApiResponse<PersonListDto>>> Followers(int userId) =>
        Respond(await _follows.FollowersAsync(userId));

    // GET api/people/12/following
    [HttpGet("api/people/{userId}/following")]
    [ServiceRequestId("PERSON_FOLLOWING")]
    public async Task<ActionResult<ApiResponse<PersonListDto>>> Following(int userId) =>
        Respond(await _follows.FollowingAsync(userId));
}
