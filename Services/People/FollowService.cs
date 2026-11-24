using TournamentScheduler.Api.Auth;
using TournamentScheduler.Api.Data;
using TournamentScheduler.Api.Data.DataServices;
using TournamentScheduler.Api.Models;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Models.Cricket;
using TournamentScheduler.Api.Models.Messaging;
using TournamentScheduler.Api.Models.People;
using TournamentScheduler.Api.Pictures;
using TournamentScheduler.Api.Services.Notifications;

namespace TournamentScheduler.Api.Services.People;

public interface IFollowService
{
    /// <summary>People who play (linked to a squad place), by name: three letters or more, 20 at most.</summary>
    Task<ServiceResult<List<PersonRowDto>>> SearchAsync(string? query);

    Task<ServiceResult<FollowResultDto>> FollowAsync(int userId);
    Task<ServiceResult<FollowResultDto>> UnfollowAsync(int userId);

    /// <summary>Stops someone following you. They aren't told.</summary>
    Task<ServiceResult<PersonListDto>> RemoveFollowerAsync(int userId);

    Task<ServiceResult<PersonListDto>> FollowersAsync(int userId);
    Task<ServiceResult<PersonListDto>> FollowingAsync(int userId);

    /// <summary>The counts and how the viewer and this person follow each other, for a profile.</summary>
    Task<FollowSummaryDto> SummaryAsync(int userId, int viewer);

    /// <summary>Matches this person is in now that the viewer may watch, for a profile's "Playing now".</summary>
    Task<List<PlayingNowDto>> PlayingNowAsync(int userId, int viewer);

    /// <summary>
    /// A match was just set up (cricket) or kicked off (football): tells the followers of everyone in it who may watch
    /// it, once per match, one notification naming everyone they follow there. Saves its own change.
    /// </summary>
    Task MatchStartedAsync(IScoredMatch match);

    /// <summary>Account deletion: removes everything this person follows and everyone following them. Stages.</summary>
    Task ForgetAsync(int userId);
}

/// <summary>
/// Following people (2026-11-23, item 6; LLD 4.22). Works at once, with no request (the user's choice). Only people with
/// a profile can be found or followed — accounts linked to a squad place, as profiles are — and nobody is told who
/// searched. "Is playing" news goes only to followers who may watch that match (the tournament is public, or they're in
/// it), so a private tournament stays private and a tap always opens something.
/// </summary>
public class FollowService(
    IDataService<UserFollow> follows,
    IDataService<User> users,
    IDataService<Player> players,
    IDataService<Tournament> tournaments,
    IDataService<TournamentMember> members,
    IDataService<TeamAdmin> teamAdmins,
    IDataService<MatchPlayer> footballLineUps,
    IDataService<CricketMatchPlayer> cricketLineUps,
    IDataService<Match> matches,
    IDataService<CricketMatch> cricketMatches,
    INotifier notifier,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    PictureLinks pictureLinks,
    TimeProvider clock) : ServiceBase, IFollowService
{
    public const int SearchMin = 3;
    public const int SearchMax = 20;

    /// <summary>A list shows this many; nobody here has more followers than that yet.</summary>
    public const int ListMax = 500;

    private int Me => currentUser.UserId;

    // -----------------------------------------------------------------
    // Finding and following
    // -----------------------------------------------------------------

    public async Task<ServiceResult<List<PersonRowDto>>> SearchAsync(string? query)
    {
        var q = (query ?? "").Trim().ToLowerInvariant();
        if (q.Length < SearchMin) return Success(new List<PersonRowDto>());

        // Names first (a few hundred at most), then only those who play: an account with no squad place has no profile.
        var named = await users.QueryAsync(x => x
            .Where(u => u.DeletedOn == null && !u.IsDisabled && u.UserId != SystemUser.Id && u.Name.ToLower().Contains(q))
            .OrderBy(u => u.Name)
            .Take(300)
            .Select(u => new { u.UserId, u.Name }), tracking: false);
        var ids = named.Select(u => u.UserId).ToList();
        var playing = (await players.QueryAsync(x => x.Where(p => p.UserId != null && ids.Contains(p.UserId.Value)).Select(p => p.UserId!.Value).Distinct(), tracking: false)).ToHashSet();
        return Success(await RowsAsync(named.Where(u => playing.Contains(u.UserId)).Take(SearchMax).Select(u => u.UserId).ToList()));
    }

    public async Task<ServiceResult<FollowResultDto>> FollowAsync(int userId)
    {
        if (userId == Me) return BadRequest("You can't follow yourself.");
        var person = await PersonAsync(userId);
        if (person == null) return NotFound("Profile not found.");

        if (!await follows.AnyAsync(f => f.FollowerUserId == Me && f.FollowedUserId == userId))
        {
            follows.Add(new UserFollow { FollowerUserId = Me, FollowedUserId = userId, Crd = clock.GetUtcNow().UtcDateTime });
            // Once per pair: following, stopping and following again doesn't keep telling them.
            var key = $"followed:{Me}:{userId}";
            if (!await notifier.AlreadySentAsync(key))
            {
                var myName = await users.QueryFirstOrDefaultAsync(x => x.Where(u => u.UserId == Me).Select(u => u.Name), tracking: false) ?? "Someone";
                await notifier.NotifyAsync(userId, NotificationTemplateCode.Followed, Notifier.With(("ActorName", myName)), NotificationLink.ToProfile(Me), key);
            }
            try
            {
                await unitOfWork.SaveChangesAsync();
            }
            catch (DuplicateEntryException)
            {
                // Two taps at once: the first one did it.
            }
        }
        return Success(new FollowResultDto { UserId = userId, Follow = await SummaryAsync(userId, Me) });
    }

    public async Task<ServiceResult<FollowResultDto>> UnfollowAsync(int userId)
    {
        var row = await follows.FirstOrDefaultAsync(f => f.FollowerUserId == Me && f.FollowedUserId == userId);
        if (row != null)
        {
            follows.Remove(row);
            await unitOfWork.SaveChangesAsync();
        }
        return Success(new FollowResultDto { UserId = userId, Follow = await SummaryAsync(userId, Me) });
    }

    public async Task<ServiceResult<PersonListDto>> RemoveFollowerAsync(int userId)
    {
        var row = await follows.FirstOrDefaultAsync(f => f.FollowerUserId == userId && f.FollowedUserId == Me);
        if (row != null)
        {
            follows.Remove(row);
            await unitOfWork.SaveChangesAsync();
        }
        return await FollowersAsync(Me);
    }

    public Task<ServiceResult<PersonListDto>> FollowersAsync(int userId) => ListAsync(userId, followers: true);
    public Task<ServiceResult<PersonListDto>> FollowingAsync(int userId) => ListAsync(userId, followers: false);

    private async Task<ServiceResult<PersonListDto>> ListAsync(int userId, bool followers)
    {
        // Your own lists open even before you play anywhere; someone else's only with a profile.
        var person = userId == Me
            ? (await users.QueryFirstOrDefaultAsync(x => x.Where(u => u.UserId == Me).Select(u => u.Name), tracking: false) is { } myName ? (Me, myName) : null)
            : await PersonAsync(userId);
        if (person is not { } found) return NotFound("Profile not found.");

        var ids = followers
            ? await follows.QueryAsync(x => x.Where(f => f.FollowedUserId == userId).OrderByDescending(f => f.Crd).Take(ListMax).Select(f => f.FollowerUserId), tracking: false)
            : await follows.QueryAsync(x => x.Where(f => f.FollowerUserId == userId).OrderByDescending(f => f.Crd).Take(ListMax).Select(f => f.FollowedUserId), tracking: false);
        var summary = await SummaryAsync(userId, Me);
        return Success(new PersonListDto
        {
            UserId = found.UserId,
            Name = found.Name,
            Followers = summary.Followers,
            Following = summary.Following,
            IsMine = userId == Me,
            Items = await RowsAsync(ids)
        });
    }

    public async Task<FollowSummaryDto> SummaryAsync(int userId, int viewer) => new()
    {
        Followers = await follows.CountAsync(f => f.FollowedUserId == userId),
        Following = await follows.CountAsync(f => f.FollowerUserId == userId),
        IsFollowing = viewer != userId && await follows.AnyAsync(f => f.FollowerUserId == viewer && f.FollowedUserId == userId),
        FollowsMe = viewer != userId && await follows.AnyAsync(f => f.FollowerUserId == userId && f.FollowedUserId == viewer)
    };

    public async Task ForgetAsync(int userId)
    {
        var rows = await follows.ListAsync(f => f.FollowerUserId == userId || f.FollowedUserId == userId);
        if (rows.Count > 0) follows.RemoveRange(rows);
    }

    /// <summary>Someone with a profile: not deleted, blocked or the System account, and linked to a squad place.</summary>
    private async Task<(int UserId, string Name)?> PersonAsync(int userId)
    {
        var user = await users.QueryFirstOrDefaultAsync(x => x
            .Where(u => u.UserId == userId && u.DeletedOn == null && !u.IsDisabled && u.UserId != SystemUser.Id)
            .Select(u => new { u.UserId, u.Name }), tracking: false);
        if (user == null || !await players.AnyAsync(p => p.UserId == userId)) return null;
        return (user.UserId, user.Name);
    }

    /// <summary>The people, in the order given, with their latest team and how they and the viewer follow each other.</summary>
    private async Task<List<PersonRowDto>> RowsAsync(List<int> ids)
    {
        if (ids.Count == 0) return [];
        var people = (await users.QueryAsync(x => x
                .Where(u => ids.Contains(u.UserId) && u.DeletedOn == null)
                .Select(u => new { u.UserId, u.Name, u.PhotoId }), tracking: false))
            .ToDictionary(u => u.UserId);
        var latest = (await players.QueryAsync(x => x
                .Where(p => p.UserId != null && ids.Contains(p.UserId.Value))
                .Select(p => new { UserId = p.UserId!.Value, p.Id, Team = p.Team!.Name, p.Team.Tournament!.Sport }), tracking: false))
            .GroupBy(p => p.UserId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.Id).First());
        var iFollow = (await follows.QueryAsync(x => x.Where(f => f.FollowerUserId == Me && ids.Contains(f.FollowedUserId)).Select(f => f.FollowedUserId), tracking: false)).ToHashSet();
        var followMe = (await follows.QueryAsync(x => x.Where(f => f.FollowedUserId == Me && ids.Contains(f.FollowerUserId)).Select(f => f.FollowerUserId), tracking: false)).ToHashSet();

        return ids.Where(people.ContainsKey).Select(id =>
        {
            var u = people[id];
            return new PersonRowDto
            {
                UserId = id,
                Name = u.Name,
                PhotoUrl = pictureLinks.For(u.PhotoId),
                Line = latest.TryGetValue(id, out var p) ? $"{p.Sport} · {p.Team}" : null,
                IsFollowing = iFollow.Contains(id),
                FollowsMe = followMe.Contains(id),
                IsMe = id == Me
            };
        }).ToList();
    }

    // -----------------------------------------------------------------
    // Playing now
    // -----------------------------------------------------------------

    private sealed record LiveMatch(int TournamentId, Sport Sport, int MatchId, string Home, string Away, List<int> UserIds);

    public async Task<List<PlayingNowDto>> PlayingNowAsync(int userId, int viewer)
    {
        var places = await players.QueryAsync(x => x.Where(p => p.UserId == userId).Select(p => p.Id), tracking: false);
        if (places.Count == 0) return [];

        var live = new List<LiveMatch>();
        var football = await footballLineUps.QueryAsync(x => x
            .Where(l => places.Contains(l.PlayerId) && (l.Match!.Status == MatchStatus.InProgress || l.Match.Status == MatchStatus.Paused || l.Match.Status == MatchStatus.PenaltyShootout))
            .Select(l => new { l.Match!.TournamentId, l.MatchId, l.Match.HomeTeamName, l.Match.AwayTeamName }), tracking: false);
        live.AddRange(football.DistinctBy(m => m.MatchId).Select(m => new LiveMatch(m.TournamentId, Sport.Football, m.MatchId, m.HomeTeamName, m.AwayTeamName, [userId])));
        // A cricket match counts from its set-up (the XI picked) until it is over.
        var cricket = await cricketLineUps.QueryAsync(x => x
            .Where(l => places.Contains(l.PlayerId) && l.Match!.Status != CricketMatchStatus.Completed && l.Match.Status != CricketMatchStatus.Abandoned)
            .Select(l => new { l.Match!.TournamentId, l.CricketMatchId, l.Match.HomeTeamName, l.Match.AwayTeamName }), tracking: false);
        live.AddRange(cricket.DistinctBy(m => m.CricketMatchId).Select(m => new LiveMatch(m.TournamentId, Sport.Cricket, m.CricketMatchId, m.HomeTeamName, m.AwayTeamName, [userId])));
        if (live.Count == 0) return [];

        var watchable = await WatchableAsync(live.Select(m => m.TournamentId).Distinct().ToList(), viewer);
        var names = await TournamentNamesAsync(live.Select(m => m.TournamentId).Distinct().ToList());
        return live.Where(m => watchable.Contains(m.TournamentId))
            .Select(m => new PlayingNowDto { TournamentId = m.TournamentId, TournamentName = names.GetValueOrDefault(m.TournamentId, ""), Sport = m.Sport, MatchId = m.MatchId, Match = $"{m.Home} v {m.Away}" })
            .ToList();
    }

    public async Task MatchStartedAsync(IScoredMatch match)
    {
        var sport = match is CricketMatch ? Sport.Cricket : Sport.Football;
        var (home, away) = match is CricketMatch c ? (c.HomeTeamName, c.AwayTeamName) : match is Match f ? (f.HomeTeamName, f.AwayTeamName) : ("", "");
        if (home.Length == 0)
        {
            // The context's match may be a bare one; read the names.
            (home, away) = sport == Sport.Cricket
                ? (await cricketMatches.QueryFirstOrDefaultAsync(x => x.Where(m => m.Id == match.Id).Select(m => new { m.HomeTeamName, m.AwayTeamName }), tracking: false)) is { } cm ? (cm.HomeTeamName, cm.AwayTeamName) : ("", "")
                : (await matches.QueryFirstOrDefaultAsync(x => x.Where(m => m.Id == match.Id).Select(m => new { m.HomeTeamName, m.AwayTeamName }), tracking: false)) is { } fm ? (fm.HomeTeamName, fm.AwayTeamName) : ("", "");
        }

        var placeIds = sport == Sport.Cricket
            ? await cricketLineUps.QueryAsync(x => x.Where(l => l.CricketMatchId == match.Id).Select(l => l.PlayerId), tracking: false)
            : await footballLineUps.QueryAsync(x => x.Where(l => l.MatchId == match.Id).Select(l => l.PlayerId), tracking: false);
        if (placeIds.Count == 0) return;
        var inIt = await players.QueryAsync(x => x
            .Where(p => placeIds.Contains(p.Id) && p.UserId != null)
            .Select(p => new { UserId = p.UserId!.Value, p.User!.Name }), tracking: false);
        if (inIt.Count == 0) return;
        var playerIds = inIt.Select(p => p.UserId).Distinct().ToList();

        var pairs = await follows.QueryAsync(x => x.Where(f => playerIds.Contains(f.FollowedUserId)).Select(f => new { f.FollowerUserId, f.FollowedUserId }), tracking: false);
        // Someone playing in it already knows.
        var followers = pairs.Select(p => p.FollowerUserId).Distinct().Where(id => !playerIds.Contains(id)).ToList();
        if (followers.Count == 0) return;

        var tournamentName = await notifier.TournamentNameAsync(match.TournamentId);
        var isPublic = await tournaments.AnyAsync(t => t.Id == match.TournamentId && t.IsPublic);
        var names = inIt.GroupBy(p => p.UserId).ToDictionary(g => g.Key, g => g.First().Name);
        foreach (var follower in followers)
        {
            if (!isPublic && !(await WatchableAsync([match.TournamentId], follower)).Contains(match.TournamentId)) continue;
            var key = $"playing:{sport}:{match.Id}:{follower}";
            if (await notifier.AlreadySentAsync(key)) continue;
            var who = pairs.Where(p => p.FollowerUserId == follower).Select(p => names[p.FollowedUserId]).Distinct().OrderBy(n => n).ToList();
            await notifier.NotifyAsync(follower, NotificationTemplateCode.FollowedPlaying,
                Notifier.With(("PlayerNames", JoinNames(who)), ("IsAre", who.Count == 1 ? "is" : "are"), ("Match", $"{home} v {away}"), ("TournamentName", tournamentName)),
                new NotificationLink(match.TournamentId, sport, match.Id), key);
        }
        await unitOfWork.SaveChangesAsync();
    }

    /// <summary>"Rohan", "Neha and Rohit", "Amit, Neha and Rohit".</summary>
    public static string JoinNames(IReadOnlyList<string> names) => names.Count switch
    {
        0 => "",
        1 => names[0],
        _ => $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}"
    };

    /// <summary>
    /// The tournaments this person may watch: public ones, and ones they're in — created, an owner or scorer, a squad
    /// place, or a team's admin. The same people tournament access lets look, less followers of a public one (it's
    /// public then anyway).
    /// </summary>
    private async Task<HashSet<int>> WatchableAsync(List<int> ids, int viewer)
    {
        var open = (await tournaments.QueryAsync(x => x.Where(t => ids.Contains(t.Id) && (t.IsPublic || t.CreatedByUserId == viewer)).Select(t => t.Id), tracking: false)).ToHashSet();
        var rest = ids.Where(id => !open.Contains(id)).ToList();
        if (rest.Count == 0) return open;
        open.UnionWith(await members.QueryAsync(x => x.Where(m => rest.Contains(m.TournamentId) && m.UserId == viewer && !m.IsDisabled).Select(m => m.TournamentId), tracking: false));
        open.UnionWith(await players.QueryAsync(x => x.Where(p => rest.Contains(p.TournamentId) && p.UserId == viewer).Select(p => p.TournamentId), tracking: false));
        open.UnionWith(await teamAdmins.QueryAsync(x => x.Where(a => rest.Contains(a.TournamentId) && a.UserId == viewer && !a.IsDisabled).Select(a => a.TournamentId), tracking: false));
        return open;
    }

    private async Task<Dictionary<int, string>> TournamentNamesAsync(List<int> ids) =>
        (await tournaments.QueryAsync(x => x.Where(t => ids.Contains(t.Id)).Select(t => new { t.Id, t.Name }), tracking: false)).ToDictionary(t => t.Id, t => t.Name);
}
