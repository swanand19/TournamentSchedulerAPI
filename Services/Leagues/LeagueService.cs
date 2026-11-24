using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using TournamentScheduler.Api.Auth;
using TournamentScheduler.Api.Data;
using TournamentScheduler.Api.Data.DataServices;
using TournamentScheduler.Api.Data.Queries;
using TournamentScheduler.Api.Models;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Models.Cricket;
using TournamentScheduler.Api.Models.Leagues;
using TournamentScheduler.Api.Models.Messaging;
using TournamentScheduler.Api.Models.Public;
using TournamentScheduler.Api.Pictures;
using TournamentScheduler.Api.Services.Access;
using TournamentScheduler.Api.Services.Accounts;
using TournamentScheduler.Api.Services.Cricket;
using TournamentScheduler.Api.Services.Knockouts;
using TournamentScheduler.Api.Services.Notifications;
using TournamentScheduler.Api.Services.Public;

namespace TournamentScheduler.Api.Services.Leagues;

public interface ILeagueService
{
    /// <summary>The leagues the caller runs, for "Part of a league?" and Home.</summary>
    Task<ServiceResult<List<LeagueListItemDto>>> ListMineAsync(Sport? sport);

    /// <summary>The caller's own tournaments of this sport in no league: what can be ticked in as earlier seasons.</summary>
    Task<ServiceResult<List<LeagueCandidateDto>>> CandidatesAsync(Sport sport);

    Task<ServiceResult<LeagueDto>> CreateAsync(CreateLeagueRequest request);
    Task<ServiceResult<LeagueDto>> GetAsync(int leagueId);
    Task<ServiceResult<LeagueDto>> UpdateAsync(int leagueId, UpdateLeagueRequest request);
    Task<ServiceResult<bool>> DeleteAsync(int leagueId);
    Task<ServiceResult<LeagueDto>> AddOrganiserAsync(int leagueId, AddLeagueOrganiserRequest request);
    Task<ServiceResult<LeagueDto>> RemoveOrganiserAsync(int leagueId, int userId);
    Task<ServiceResult<LeagueDto>> AddSeasonAsync(int leagueId, LeagueSeasonRequest request);
    Task<ServiceResult<LeagueDto>> RemoveSeasonAsync(int leagueId, int tournamentId);
    Task<ServiceResult<LeagueStatsDto>> StatsAsync(int leagueId, int? season);
    Task<ServiceResult<LeagueTeamsDto>> TeamsAsync(int leagueId);

    /// <summary>"Bring back teams" on a season's Teams page: every earlier season's teams (the tournament's owners).</summary>
    Task<ServiceResult<BringBackOptionsDto>> BringBackOptionsAsync(int tournamentId);
    Task<ServiceResult<BringBackResultDto>> BringBackAsync(int tournamentId, BringBackTeamsRequest request);

    /// <summary>The signed-out pages at /leagues/&lt;code&gt;: names and results only.</summary>
    Task<ServiceResult<JsonNode>> PublicAsync(string code);
    Task<ServiceResult<JsonNode>> PublicStatsAsync(string code, int? season);
    Task<ServiceResult<JsonNode>> PublicTeamsAsync(string code);

    /// <summary>Why the caller can't start a season of this league with a new tournament of this sport; null when they can.</summary>
    Task<ServiceError?> SeasonProblemAsync(int leagueId, Sport sport);

    /// <summary>"Season 3 of Corporate Premier League" for a tournament's answer; null for a one-off.</summary>
    Task<LeagueBadgeDto?> BadgeAsync(int tournamentId, bool withLine);

    /// <summary>The same, for many tournaments at once (Home's list): tournament id → badge, for those in a league.</summary>
    Task<Dictionary<int, LeagueBadgeDto>> BadgesAsync(IReadOnlyCollection<int> tournamentIds);

    /// <summary>An account being deleted: its organiser seats go, handed on to the latest season's creator if it was the last.</summary>
    Task ForgetUserAsync(int userId);
}

/// <summary>
/// Leagues and seasons (2026-11-24, canvas "Leagues and seasons", the user's decisions). A league groups tournaments that
/// are the same event run again; each stays an ordinary tournament with <see cref="Tournament.LeagueId"/>. Seasons are
/// numbered in date order on every read. Players join up across seasons only by their account; a team carries its record
/// only when brought back (<see cref="Team.BroughtBackFromTeamId"/>) or when it's the same lasting team (club). Totals
/// count every season; a season an admin hid is left out for everyone but the organisers.
/// </summary>
public class LeagueService : ServiceBase, ILeagueService
{
    private readonly IDataService<League> _leagues;
    private readonly IDataService<LeagueOrganiser> _organisers;
    private readonly IDataService<Tournament> _tournaments;
    private readonly IDataService<TournamentMember> _members;
    private readonly IDataService<TournamentFollow> _follows;
    private readonly IDataService<Team> _teams;
    private readonly IDataService<Player> _players;
    private readonly IDataService<TeamAdmin> _teamAdmins;
    private readonly IDataService<User> _users;
    private readonly IDataService<Match> _matches;
    private readonly IDataService<CricketMatch> _cricketMatches;
    private readonly IStatsService _footballStats;
    private readonly ICricketStatsService _cricketStats;
    private readonly IPlayerLinkService _links;
    private readonly INotifier _notifier;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly PublicOptions _public;
    private readonly SmsOptions _sms;
    private readonly PictureLinks _pictureLinks;
    private readonly IPictureStore _pictureStore;
    private readonly TimeProvider _clock;

    public LeagueService(
        IDataService<League> leagues,
        IDataService<LeagueOrganiser> organisers,
        IDataService<Tournament> tournaments,
        IDataService<TournamentMember> members,
        IDataService<TournamentFollow> follows,
        IDataService<Team> teams,
        IDataService<Player> players,
        IDataService<TeamAdmin> teamAdmins,
        IDataService<User> users,
        IDataService<Match> matches,
        IDataService<CricketMatch> cricketMatches,
        IStatsService footballStats,
        ICricketStatsService cricketStats,
        IPlayerLinkService links,
        INotifier notifier,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        IOptions<PublicOptions> publicOptions,
        IOptions<SmsOptions> sms,
        PictureLinks pictureLinks,
        IPictureStore pictureStore,
        TimeProvider clock)
    {
        _leagues = leagues;
        _organisers = organisers;
        _tournaments = tournaments;
        _members = members;
        _follows = follows;
        _teams = teams;
        _players = players;
        _teamAdmins = teamAdmins;
        _users = users;
        _matches = matches;
        _cricketMatches = cricketMatches;
        _footballStats = footballStats;
        _cricketStats = cricketStats;
        _links = links;
        _notifier = notifier;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _public = publicOptions.Value;
        _sms = sms.Value;
        _pictureLinks = pictureLinks;
        _pictureStore = pictureStore;
        _clock = clock;
    }

    private int Me => _currentUser.IsSignedIn ? _currentUser.UserId : 0; // 0: the signed-out pages
    private DateTime Now => _clock.GetUtcNow().UtcDateTime;
    private string ActorName => string.IsNullOrWhiteSpace(_currentUser.Name) ? "Someone" : _currentUser.Name;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private const string Gone = "That league doesn't exist any more.";
    private const string OrganisersOnly = "Only this league's organisers can do that.";
    private const string NotAPublicLeague = "This league's page isn't here. Check the link with whoever sent it.";

    private Task<bool> IsOrganiserAsync(int leagueId) => _organisers.AnyAsync(o => o.LeagueId == leagueId && o.UserId == Me && !o.IsDisabled);

    /// <summary>The league, tracked, if the caller runs it.</summary>
    private async Task<(League? League, ServiceError? Error)> ManagedAsync(int leagueId)
    {
        var league = await _leagues.FirstOrDefaultAsync(l => l.LeagueId == leagueId && !l.IsDisabled);
        if (league == null) return (null, NotFound(Gone));
        if (!await IsOrganiserAsync(leagueId)) return (null, Forbidden(OrganisersOnly));
        return (league, null);
    }

    // ---------------------------------------------------------------------------------------------------------------------
    // Seasons: a league's tournaments in date order
    // ---------------------------------------------------------------------------------------------------------------------

    private sealed record Season(
        int Number, int TournamentId, string Name, Sport Sport, TournamentStatus Status, DateOnly? StartDate, DateOnly? EndDate,
        string? LogoId, string? PublicCode, bool IsPublic, int CreatedByUserId);

    /// <summary>
    /// Every season of the league, numbered from 1 in date order: by its start date; a season with no dates yet goes after
    /// all the dated ones (it's the one "Start Season N" just made, which must stay Season N even when the earlier ones
    /// are dated ahead), until its dates are chosen with its schedule. Ties by the day it was made, then by id.
    /// </summary>
    private async Task<List<Season>> SeasonsAsync(int leagueId)
    {
        var rows = await _tournaments.QueryAsync(q => q
            .Where(t => t.LeagueId == leagueId)
            .Select(t => new
            {
                t.Id, t.Name, t.Sport, t.Status, t.StartDate, t.EndDate, t.CreatedAt, t.LogoId, t.PublicCode, t.IsPublic, t.CreatedByUserId
            }), tracking: false);
        return rows
            .OrderBy(t => t.StartDate == null)
            .ThenBy(t => t.StartDate)
            .ThenBy(t => t.CreatedAt)
            .ThenBy(t => t.Id)
            .Select((t, i) => new Season(i + 1, t.Id, t.Name, t.Sport, t.Status, t.StartDate, t.EndDate, t.LogoId, t.PublicCode, t.IsPublic,
                t.CreatedByUserId))
            .ToList();
    }

    /// <summary>The seasons this caller may see: all of them for its organisers, the ones an admin hasn't hidden for everyone else.</summary>
    private static List<Season> Visible(List<Season> seasons, bool organiser) => organiser ? seasons : seasons.Where(s => s.IsPublic).ToList();

    // ---------------------------------------------------------------------------------------------------------------------
    // Champions
    // ---------------------------------------------------------------------------------------------------------------------

    private sealed record Champion(int TeamId, string Name, string How);

    /// <summary>
    /// Who won a season: the Final's winner once it's played; otherwise, for a completed season of one group, the top of
    /// its table. Null while nobody has. How says it in words: "beat Falcons in the final", "top of the table".
    /// </summary>
    private async Task<Champion?> ChampionAsync(Season season, Dictionary<int, Champion?> cache)
    {
        if (cache.TryGetValue(season.TournamentId, out var known)) return known;
        Champion? result = null;
        if (season.Sport == Sport.Cricket)
        {
            var final = await _cricketMatches.FirstOrDefaultAsync(m => m.TournamentId == season.TournamentId && m.KnockoutFixtureId != null
                && m.KnockoutFixture!.Round == Models.Knockouts.KnockoutRound.Final, tracking: false);
            if (final != null && KnockoutResults.WinnerOf(final) is int w)
                result = new Champion(w, w == final.HomeTeamId ? final.HomeTeamName : final.AwayTeamName,
                    $"beat {(w == final.HomeTeamId ? final.AwayTeamName : final.HomeTeamName)} in the final");
            else if (season.Status == TournamentStatus.Completed && await OneGroupAsync(season))
            {
                var stats = await _cricketStats.BuildAsync(season.TournamentId);
                if (stats?.PointsTable.FirstOrDefault()?.Rows.FirstOrDefault() is { } top)
                    result = new Champion(top.TeamId, top.TeamName, "top of the table");
            }
        }
        else
        {
            var final = await _matches.FirstOrDefaultAsync(m => m.TournamentId == season.TournamentId && m.KnockoutFixtureId != null
                && m.KnockoutFixture!.Round == Models.Knockouts.KnockoutRound.Final, tracking: false);
            if (final != null && KnockoutResults.WinnerOf(final) is int w)
                result = new Champion(w, w == final.HomeTeamId ? final.HomeTeamName : final.AwayTeamName,
                    $"beat {(w == final.HomeTeamId ? final.AwayTeamName : final.HomeTeamName)} in the final");
            else if (season.Status == TournamentStatus.Completed && await OneGroupAsync(season))
            {
                var stats = await _footballStats.BuildAsync(season.TournamentId);
                if (stats?.Standings.FirstOrDefault() is { } top)
                    result = new Champion(top.TeamId, top.TeamName, "top of the table");
            }
        }
        cache[season.TournamentId] = result;
        return result;
    }

    /// <summary>A season whose group matches were all in one group, so its table has one top.</summary>
    private async Task<bool> OneGroupAsync(Season season)
    {
        var groups = season.Sport == Sport.Cricket
            ? await _cricketMatches.QueryAsync(q => q.Where(m => m.TournamentId == season.TournamentId && m.KnockoutFixtureId == null)
                .Select(m => m.GroupName).Distinct(), tracking: false)
            : await _matches.QueryAsync(q => q.Where(m => m.TournamentId == season.TournamentId && m.KnockoutFixtureId == null)
                .Select(m => m.GroupName).Distinct(), tracking: false);
        return groups.Count == 1;
    }

    // ---------------------------------------------------------------------------------------------------------------------
    // Teams across seasons
    // ---------------------------------------------------------------------------------------------------------------------

    private sealed record TeamRow(int TeamId, string Name, int TournamentId, int? ClubId, int? BroughtBackFromTeamId);

    /// <summary>
    /// Which teams of the league's seasons are the same team (the user's rule): one brought back from another, or squads of
    /// the same lasting team. Never by name — a new team with an old name starts its own record.
    /// </summary>
    private sealed class Identities
    {
        private readonly Dictionary<int, int> _parent = new();

        public Identities(IEnumerable<TeamRow> teams)
        {
            var list = teams.ToList();
            foreach (var t in list) _parent[t.TeamId] = t.TeamId;
            foreach (var t in list)
                if (t.BroughtBackFromTeamId is int from && _parent.ContainsKey(from)) Union(t.TeamId, from);
            foreach (var club in list.Where(t => t.ClubId != null).GroupBy(t => t.ClubId))
            {
                var first = club.First().TeamId;
                foreach (var t in club) Union(first, t.TeamId);
            }
        }

        public int Of(int teamId)
        {
            if (!_parent.TryGetValue(teamId, out var p)) return teamId;
            if (p == teamId) return teamId;
            var root = Of(p);
            _parent[teamId] = root;
            return root;
        }

        private void Union(int a, int b)
        {
            var ra = Of(a);
            var rb = Of(b);
            if (ra != rb) _parent[Math.Max(ra, rb)] = Math.Min(ra, rb);
        }
    }

    private async Task<List<TeamRow>> TeamsOfAsync(IReadOnlyCollection<int> tournamentIds) =>
        await _teams.QueryAsync(q => q
            .Where(t => tournamentIds.Contains(t.TournamentId))
            .Select(t => new TeamRow(t.Id, t.Name, t.TournamentId, t.ClubId, t.BroughtBackFromTeamId)), tracking: false);

    /// <summary>A team's name across seasons: the one it has in its latest season.</summary>
    private static Dictionary<int, string> LatestNames(List<TeamRow> teams, Identities ids, Dictionary<int, int> numberOf) =>
        teams
            .GroupBy(t => ids.Of(t.TeamId))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(t => numberOf.GetValueOrDefault(t.TournamentId)).First().Name);

    // ---------------------------------------------------------------------------------------------------------------------
    // My leagues, making one, the page
    // ---------------------------------------------------------------------------------------------------------------------

    public async Task<ServiceResult<List<LeagueListItemDto>>> ListMineAsync(Sport? sport)
    {
        var mine = await _organisers.QueryAsync(q => q.Where(o => o.UserId == Me && !o.IsDisabled).Select(o => o.LeagueId), tracking: false);
        if (mine.Count == 0) return Success(new List<LeagueListItemDto>());
        var leagues = await _leagues.ListAsync(l => mine.Contains(l.LeagueId) && !l.IsDisabled && (sport == null || l.Sport == sport), tracking: false);

        var result = new List<LeagueListItemDto>();
        var champions = new Dictionary<int, Champion?>();
        foreach (var league in leagues.OrderBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var seasons = await SeasonsAsync(league.LeagueId);
            var last = seasons.LastOrDefault();
            var line = seasons.Count == 0 ? "No seasons yet" : Plural(seasons.Count, "season");
            if (last != null)
            {
                var champion = last.Status == TournamentStatus.Completed ? await ChampionAsync(last, champions) : null;
                line += champion != null ? $" · last: {last.Name}, {champion.Name} won" : $" · last: {last.Name}";
            }
            result.Add(new LeagueListItemDto
            {
                LeagueId = league.LeagueId,
                Name = league.Name,
                ShortName = league.ShortName,
                Sport = league.Sport,
                LogoUrl = _pictureLinks.For(league.LogoId),
                Seasons = seasons.Count,
                Line = line
            });
        }
        return Success(result);
    }

    /// <summary>Tournaments the caller owns (made, or an owner of), of this sport, in no league yet.</summary>
    private async Task<HashSet<int>> OwnedAsync()
    {
        var owned = (await _tournaments.QueryAsync(q => q.Where(t => t.CreatedByUserId == Me).Select(t => t.Id), tracking: false)).ToHashSet();
        owned.UnionWith(await _members.QueryAsync(q => q
            .Where(m => m.UserId == Me && !m.IsDisabled && m.MemberRole == TournamentMemberRole.Owner)
            .Select(m => m.TournamentId), tracking: false));
        return owned;
    }

    public async Task<ServiceResult<List<LeagueCandidateDto>>> CandidatesAsync(Sport sport)
    {
        var owned = await OwnedAsync();
        var rows = await _tournaments.QueryAsync(q => q
            .Where(t => owned.Contains(t.Id) && t.Sport == sport && t.LeagueId == null && t.Status != TournamentStatus.Cancelled)
            .Select(t => new { t.Id, t.Name, t.Sport, t.Status, t.StartDate, t.EndDate, t.CreatedAt, t.LogoId, t.PublicCode, t.IsPublic, t.CreatedByUserId }),
            tracking: false);

        var champions = new Dictionary<int, Champion?>();
        var result = new List<LeagueCandidateDto>();
        foreach (var t in rows.OrderBy(t => t.StartDate ?? DateOnly.FromDateTime(t.CreatedAt)).ThenBy(t => t.Id))
        {
            var season = new Season(0, t.Id, t.Name, t.Sport, t.Status, t.StartDate, t.EndDate, t.LogoId, t.PublicCode, t.IsPublic, t.CreatedByUserId);
            var when = (t.StartDate ?? DateOnly.FromDateTime(t.CreatedAt)).ToString("MMM yyyy", Inv);
            var champion = t.Status == TournamentStatus.Completed ? await ChampionAsync(season, champions) : null;
            result.Add(new LeagueCandidateDto
            {
                TournamentId = t.Id,
                Name = t.Name,
                Status = t.Status,
                StartDate = t.StartDate,
                Line = champion != null ? $"{when} · {champion.Name} won" : $"{when} · {StatusWord(t.Status)}"
            });
        }
        return Success(result);
    }

    private static string StatusWord(TournamentStatus status) => status switch
    {
        TournamentStatus.Live => "being played",
        TournamentStatus.Upcoming => "not started",
        TournamentStatus.Completed => "completed",
        _ => "cancelled"
    };

    private static (string? Name, string? ShortName, string? Area, string? Problem) Describe(string? name, string? shortName, string? area)
    {
        var n = (name ?? "").Trim();
        if (n.Length == 0) return (null, null, null, "Give the league a name.");
        if (n.Length > League.NameMax) return (null, null, null, $"Keep the league's name to {League.NameMax} characters.");
        var s = (shortName ?? "").Trim();
        if (s.Length == 0) s = Initials(n);
        if (s.Length > League.ShortNameMax) return (null, null, null, $"Keep the short name to {League.ShortNameMax} characters, like CPL.");
        var a = (area ?? "").Trim();
        if (a.Length > League.AreaMax) return (null, null, null, $"Keep the area to {League.AreaMax} characters.");
        return (n, s, a.Length == 0 ? null : a, null);
    }

    /// <summary>"Corporate Premier League" → "CPL": what the short name is when none is typed.</summary>
    private static string Initials(string name)
    {
        var letters = name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => char.IsLetterOrDigit(w[0]))
            .Select(w => char.ToUpperInvariant(w[0]))
            .Take(League.ShortNameMax)
            .ToArray();
        return letters.Length >= 2 ? new string(letters) : name[..Math.Min(name.Length, 4)].ToUpperInvariant();
    }

    /// <summary>Why this tournament can't become a season of a <paramref name="sport"/> league the caller runs; null when it can.</summary>
    private async Task<string?> SeasonCandidateProblemAsync(Tournament? tournament, Sport sport, int? leagueId, HashSet<int> owned)
    {
        if (tournament == null) return "That tournament doesn't exist any more.";
        if (!owned.Contains(tournament.Id)) return $"{tournament.Name} isn't yours to add: only tournaments you own can become seasons.";
        if (tournament.Sport != sport)
            return $"{tournament.Name} is {(tournament.Sport == Sport.Cricket ? "cricket" : "football")}, and this league is {(sport == Sport.Cricket ? "cricket" : "football")}.";
        if (tournament.LeagueId is int other)
            return other == leagueId
                ? $"{tournament.Name} is a season of this league already."
                : $"{tournament.Name} is a season of another league. Take it out of that one first.";
        if (tournament.Status == TournamentStatus.Cancelled) return $"{tournament.Name} was cancelled, so it can't be a season.";
        await Task.CompletedTask;
        return null;
    }

    public async Task<ServiceResult<LeagueDto>> CreateAsync(CreateLeagueRequest request)
    {
        var (name, shortName, area, problem) = Describe(request.Name, request.ShortName, request.Area);
        if (problem != null) return BadRequest(problem);
        if (!Enum.IsDefined(request.Sport)) return BadRequest("Choose football or cricket. A league plays one sport.");
        if (!await _users.AnyAsync(u => u.UserId == Me && u.DeletedOn == null)) return NotFound("Account not found.");

        var ids = (request.TournamentIds ?? []).Distinct().ToList();
        var owned = await OwnedAsync();
        var earlier = await _tournaments.ListAsync(t => ids.Contains(t.Id));
        foreach (var id in ids)
            if (await SeasonCandidateProblemAsync(earlier.FirstOrDefault(t => t.Id == id), request.Sport, null, owned) is { } why)
                return Conflict(why);

        var league = new League { Name = name!, ShortName = shortName!, Area = area, Sport = request.Sport, PublicCode = await NewCodeAsync() };
        league.Created(Now, Me);
        var organiser = new LeagueOrganiser { League = league, UserId = Me };
        organiser.Created(Now, Me);
        league.Organisers.Add(organiser);
        _leagues.Add(league);
        foreach (var t in earlier) t.LeagueRow = league;
        await _unitOfWork.SaveChangesAsync();
        return await GetAsync(league.LeagueId);
    }

    private async Task<string> NewCodeAsync()
    {
        while (true)
        {
            var code = PublicCodes.New();
            if (!await _leagues.AnyAsync(l => l.PublicCode == code)) return code;
        }
    }

    public async Task<ServiceResult<LeagueDto>> GetAsync(int leagueId)
    {
        var league = await _leagues.FirstOrDefaultAsync(l => l.LeagueId == leagueId && !l.IsDisabled, tracking: false);
        if (league == null) return NotFound(Gone);
        return Success(await PageAsync(league));
    }

    /// <summary>The league's page for whoever is asking (no one, on the signed-out page).</summary>
    private async Task<LeagueDto> PageAsync(League league)
    {
        var organisers = await _organisers.QueryAsync(q => q
            .Where(o => o.LeagueId == league.LeagueId && !o.IsDisabled)
            .OrderBy(o => o.Crd).ThenBy(o => o.LeagueOrganiserId)
            .Select(o => new { o.UserId, o.User!.Name }), tracking: false);
        var isOrganiser = Me != 0 && organisers.Any(o => o.UserId == Me);

        var all = await SeasonsAsync(league.LeagueId);
        var seasons = Visible(all, isOrganiser);
        var ids = seasons.Select(s => s.TournamentId).ToList();
        var teams = await TeamsOfAsync(ids);
        var identities = new Identities(teams);
        var open = await OpenableAsync(ids);
        var champions = new Dictionary<int, Champion?>();

        var dto = new LeagueDto
        {
            LeagueId = league.LeagueId,
            Name = league.Name,
            ShortName = league.ShortName,
            Sport = league.Sport,
            Area = league.Area,
            LogoUrl = _pictureLinks.For(league.LogoId),
            RunBy = organisers.Select(o => o.Name).ToList(),
            Organisers = organisers.Select(o => new LeagueOrganiserDto
            {
                UserId = o.UserId,
                Name = o.Name,
                IsMe = o.UserId == Me,
                CanRemove = (isOrganiser || o.UserId == Me) && organisers.Count > 1
            }).ToList(),
            CanManage = isOrganiser,
            NextSeason = all.Count + 1,
            Share = ShareOf(league.PublicCode)
        };

        var championIds = new HashSet<int>();
        var completedMatches = 0;
        foreach (var s in seasons.OrderByDescending(s => s.Number))
        {
            var teamCount = teams.Count(t => t.TournamentId == s.TournamentId);
            var (total, over) = await PlayedAsync(s);
            completedMatches += over;
            var champion = s.Status is TournamentStatus.Completed or TournamentStatus.Live ? await ChampionAsync(s, champions) : null;
            if (champion != null) championIds.Add(identities.Of(champion.TeamId));

            var (section, title, line) = s.Status switch
            {
                TournamentStatus.Live => ("live", s.Name, $"{Plural(teamCount, "team")} · {over} of {total} played"),
                TournamentStatus.Upcoming => ("upcoming", s.Name, s.StartDate is { } d
                    ? $"Starts {d.ToString("d MMM", Inv)} · {Plural(teamCount, "team")}"
                    : $"{Plural(teamCount, "team")} · not started"),
                TournamentStatus.Cancelled => ("past", s.Name, "Cancelled"),
                _ => champion != null ? ("past", champion.Name, $"{s.Name} · {champion.How}") : ("past", s.Name, "Completed")
            };
            dto.Seasons.Add(new LeagueSeasonDto
            {
                Number = s.Number,
                TournamentId = s.TournamentId,
                Name = s.Name,
                Status = s.Status,
                StartDate = s.StartDate,
                EndDate = s.EndDate,
                LogoUrl = _pictureLinks.For(s.LogoId),
                Section = section,
                Title = title,
                Line = line,
                ChampionName = s.Status == TournamentStatus.Completed ? champion?.Name : null,
                Teams = teamCount,
                CanOpen = open.Contains(s.TournamentId),
                LiveCode = s.IsPublic ? s.PublicCode : null,
                Hidden = !s.IsPublic
            });
        }

        dto.Totals = new LeagueTotalsDto
        {
            Seasons = seasons.Count,
            Teams = teams.Select(t => identities.Of(t.TeamId)).Distinct().Count(),
            Matches = completedMatches,
            Champions = championIds.Count
        };
        return dto;
    }

    private PublicShareDto ShareOf(string code)
    {
        var site = _public.WebAppUrl.Trim().TrimEnd('/');
        return new PublicShareDto { Code = code, Url = site.Length == 0 ? null : $"{site}/leagues/{code}" };
    }

    /// <summary>A season's matches: how many, and how many are over.</summary>
    private async Task<(int Total, int Over)> PlayedAsync(Season s)
    {
        if (s.Sport == Sport.Cricket)
        {
            var statuses = await _cricketMatches.QueryAsync(q => q.Where(m => m.TournamentId == s.TournamentId).Select(m => m.Status), tracking: false);
            return (statuses.Count, statuses.Count(x => x is CricketMatchStatus.Completed or CricketMatchStatus.Abandoned));
        }
        var football = await _matches.QueryAsync(q => q.Where(m => m.TournamentId == s.TournamentId).Select(m => m.Status), tracking: false);
        return (football.Count, football.Count(x => x == MatchStatus.Completed));
    }

    /// <summary>Which of these seasons the caller is in already (owns, scores, plays, runs a team, follows): those open straight away.</summary>
    private async Task<HashSet<int>> OpenableAsync(List<int> ids)
    {
        if (Me == 0 || ids.Count == 0) return [];
        var open = (await _tournaments.QueryAsync(q => q.Where(t => ids.Contains(t.Id) && t.CreatedByUserId == Me).Select(t => t.Id), tracking: false)).ToHashSet();
        open.UnionWith(await _members.QueryAsync(q => q.Where(m => m.UserId == Me && !m.IsDisabled && ids.Contains(m.TournamentId)).Select(m => m.TournamentId), tracking: false));
        open.UnionWith(await _players.QueryAsync(q => q.Where(p => p.UserId == Me && ids.Contains(p.TournamentId)).Select(p => p.TournamentId), tracking: false));
        open.UnionWith(await _teamAdmins.QueryAsync(q => q.Where(a => a.UserId == Me && !a.IsDisabled && ids.Contains(a.TournamentId)).Select(a => a.TournamentId), tracking: false));
        open.UnionWith(await _follows.QueryAsync(q => q.Where(f => f.UserId == Me && ids.Contains(f.TournamentId)).Select(f => f.TournamentId), tracking: false));
        return open;
    }

    public async Task<ServiceResult<LeagueDto>> UpdateAsync(int leagueId, UpdateLeagueRequest request)
    {
        var (league, error) = await ManagedAsync(leagueId);
        if (error is { } e) return e;
        var (name, shortName, area, problem) = Describe(request.Name, request.ShortName, request.Area);
        if (problem != null) return BadRequest(problem);
        league!.Name = name!;
        league.ShortName = shortName!;
        league.Area = area;
        league.Modified(Now, Me);
        await _unitOfWork.SaveChangesAsync();
        return await GetAsync(leagueId);
    }

    /// <summary>The league only: its tournaments stay, just not grouped any more.</summary>
    public async Task<ServiceResult<bool>> DeleteAsync(int leagueId)
    {
        var (league, error) = await ManagedAsync(leagueId);
        if (error is { } e) return e;
        foreach (var t in await _tournaments.ListAsync(t => t.LeagueId == leagueId)) t.LeagueId = null;
        var logo = league!.LogoId;
        _leagues.Remove(league); // its organisers go with it
        await _unitOfWork.SaveChangesAsync();
        _pictureStore.Delete(logo);
        return Success(true);
    }

    public async Task<ServiceResult<LeagueDto>> AddOrganiserAsync(int leagueId, AddLeagueOrganiserRequest request)
    {
        var (league, error) = await ManagedAsync(leagueId);
        if (error is { } e) return e;

        var (contact, problem) = Contacts.Read(request.Contact, _sms.IsEnabled);
        if (contact is not { } to) return BadRequest(problem!);
        var user = await _links.AccountWithAsync(to);
        if (user == null)
            return NotFound(to.IsEmail
                ? "No one has signed up with that email yet. Ask them to sign up first, then add them."
                : "No one has signed up with that number yet. Ask them to sign up first, then add them.");
        if (user.IsDisabled) return Conflict("That account has been blocked, so it can't run a league.");
        if (await _organisers.AnyAsync(o => o.LeagueId == leagueId && o.UserId == user.UserId))
            return Conflict($"{user.Name} runs {league!.Name} already.");

        var organiser = new LeagueOrganiser { LeagueId = leagueId, UserId = user.UserId };
        organiser.Created(Now, Me);
        _organisers.Add(organiser);
        if (user.UserId != Me)
            await _notifier.NotifyAsync(user.UserId, NotificationTemplateCode.MadeLeagueOrganiser,
                Notifier.With(("ActorName", ActorName), ("LeagueName", league!.Name)), NotificationLink.ToLeague(leagueId));
        try
        {
            await _unitOfWork.SaveChangesAsync();
        }
        catch (DuplicateEntryException)
        {
            return Conflict($"{user.Name} was added a moment ago. Reload the league.");
        }
        return await GetAsync(leagueId);
    }

    /// <summary>An organiser takes another off, or steps down. A league always keeps one.</summary>
    public async Task<ServiceResult<LeagueDto>> RemoveOrganiserAsync(int leagueId, int userId)
    {
        if (!await _leagues.AnyAsync(l => l.LeagueId == leagueId && !l.IsDisabled)) return NotFound(Gone);
        if (!await IsOrganiserAsync(leagueId)) return Forbidden(OrganisersOnly);

        var organisers = await _organisers.ListAsync(o => o.LeagueId == leagueId);
        var organiser = organisers.FirstOrDefault(o => o.UserId == userId);
        if (organiser == null) return NotFound("They don't run this league.");
        if (organisers.Count == 1)
            return Conflict(userId == Me
                ? "A league needs an organiser. Add someone else before you step down, or delete the league."
                : "A league needs an organiser.");
        _organisers.Remove(organiser);
        await _unitOfWork.SaveChangesAsync();
        return await GetAsync(leagueId);
    }

    /// <summary>Ticks a tournament the caller owns in as one of the league's seasons; it takes its place in date order.</summary>
    public async Task<ServiceResult<LeagueDto>> AddSeasonAsync(int leagueId, LeagueSeasonRequest request)
    {
        var (league, error) = await ManagedAsync(leagueId);
        if (error is { } e) return e;
        var tournament = await _tournaments.FirstOrDefaultAsync(t => t.Id == request.TournamentId);
        if (await SeasonCandidateProblemAsync(tournament, league!.Sport, leagueId, await OwnedAsync()) is { } why) return Conflict(why);
        tournament!.LeagueId = leagueId;
        await _unitOfWork.SaveChangesAsync();
        return await GetAsync(leagueId);
    }

    /// <summary>Takes a season out of the league: the tournament stays, still public, and the others renumber.</summary>
    public async Task<ServiceResult<LeagueDto>> RemoveSeasonAsync(int leagueId, int tournamentId)
    {
        var (_, error) = await ManagedAsync(leagueId);
        if (error is { } e) return e;
        var tournament = await _tournaments.FirstOrDefaultAsync(t => t.Id == tournamentId && t.LeagueId == leagueId);
        if (tournament == null) return NotFound("That tournament isn't a season of this league.");
        tournament.LeagueId = null;
        await _unitOfWork.SaveChangesAsync();
        return await GetAsync(leagueId);
    }

    // ---------------------------------------------------------------------------------------------------------------------
    // Starting a season, and what a season's own answers show
    // ---------------------------------------------------------------------------------------------------------------------

    public async Task<ServiceError?> SeasonProblemAsync(int leagueId, Sport sport)
    {
        var league = await _leagues.FirstOrDefaultAsync(l => l.LeagueId == leagueId && !l.IsDisabled, tracking: false);
        if (league == null) return NotFound(Gone);
        if (!await IsOrganiserAsync(leagueId)) return Forbidden("Only the league's organisers can start its seasons.");
        if (league.Sport != sport)
            return Conflict($"{league.Name} is a {(league.Sport == Sport.Cricket ? "cricket" : "football")} league, so its seasons are {(league.Sport == Sport.Cricket ? "cricket" : "football")} tournaments.");
        return null;
    }

    public async Task<LeagueBadgeDto?> BadgeAsync(int tournamentId, bool withLine)
    {
        var leagueId = await _tournaments.QueryAsync(q => q.Where(t => t.Id == tournamentId).Select(t => t.LeagueId), tracking: false);
        if (leagueId.FirstOrDefault() is not int id) return null;
        var league = await _leagues.FirstOrDefaultAsync(l => l.LeagueId == id && !l.IsDisabled, tracking: false);
        if (league == null) return null;

        var seasons = await SeasonsAsync(id);
        var me = seasons.FirstOrDefault(s => s.TournamentId == tournamentId);
        if (me == null) return null;
        var badge = new LeagueBadgeDto
        {
            LeagueId = id,
            Name = league.Name,
            ShortName = league.ShortName,
            Season = me.Number,
            LogoUrl = _pictureLinks.For(league.LogoId),
            LeagueCode = league.PublicCode
        };
        if (withLine)
        {
            var before = seasons.Where(s => s.Number < me.Number && s.IsPublic && s.Status == TournamentStatus.Completed).LastOrDefault();
            var champion = before == null ? null : await ChampionAsync(before, new Dictionary<int, Champion?>());
            badge.Line = champion != null ? $"Last season: {champion.Name} won · all-time stats" : "Every season and all-time stats";
        }
        return badge;
    }

    public async Task<Dictionary<int, LeagueBadgeDto>> BadgesAsync(IReadOnlyCollection<int> tournamentIds)
    {
        var rows = await _tournaments.QueryAsync(q => q
            .Where(t => tournamentIds.Contains(t.Id) && t.LeagueId != null)
            .Select(t => new { t.Id, LeagueId = t.LeagueId!.Value }), tracking: false);
        var result = new Dictionary<int, LeagueBadgeDto>();
        foreach (var group in rows.GroupBy(r => r.LeagueId))
        {
            var league = await _leagues.FirstOrDefaultAsync(l => l.LeagueId == group.Key && !l.IsDisabled, tracking: false);
            if (league == null) continue;
            var seasons = await SeasonsAsync(group.Key);
            foreach (var row in group)
                if (seasons.FirstOrDefault(s => s.TournamentId == row.Id) is { } s)
                    result[row.Id] = new LeagueBadgeDto
                    {
                        LeagueId = league.LeagueId,
                        Name = league.Name,
                        ShortName = league.ShortName,
                        Season = s.Number,
                        LogoUrl = _pictureLinks.For(league.LogoId),
                        LeagueCode = league.PublicCode
                    };
        }
        return result;
    }

    // ---------------------------------------------------------------------------------------------------------------------
    // Stats across seasons
    // ---------------------------------------------------------------------------------------------------------------------

    public async Task<ServiceResult<LeagueStatsDto>> StatsAsync(int leagueId, int? season)
    {
        var league = await _leagues.FirstOrDefaultAsync(l => l.LeagueId == leagueId && !l.IsDisabled, tracking: false);
        if (league == null) return NotFound(Gone);
        return await StatsOfAsync(league, season);
    }

    /// <summary>One person across the seasons: their account when they have one, else the squad place alone (the user's rule).</summary>
    private sealed class Person
    {
        public string Key = "";
        public int? UserId;
        public int PlayerId;
        public string Name = "";
        public int LatestSeason;
        public readonly List<(int Season, string Team)> Teams = new();
        public readonly SortedSet<int> Seasons = new();
    }

    private sealed record Place(int PlayerId, int? UserId, string Name, int TeamId, int TournamentId);

    private async Task<ServiceResult<LeagueStatsDto>> StatsOfAsync(League league, int? season)
    {
        var all = Visible(await SeasonsAsync(league.LeagueId), Me != 0 && await IsOrganiserAsync(league.LeagueId));
        if (season is int n && all.All(s => s.Number != n)) return NotFound($"{league.Name} has no season {n}.");
        var counted = season is int only ? all.Where(s => s.Number == only).ToList() : all;
        var ids = counted.Select(s => s.TournamentId).ToList();
        var numberOf = all.ToDictionary(s => s.TournamentId, s => s.Number);

        var places = await _players.QueryAsync(q => q
            .Where(p => ids.Contains(p.TournamentId))
            .Select(p => new Place(p.Id, p.UserId, p.Name, p.TeamId, p.TournamentId)), tracking: false);
        var placeOf = places.ToDictionary(p => p.PlayerId);
        var teamNames = (await TeamsOfAsync(ids)).ToDictionary(t => t.TeamId, t => t.Name);
        var people = new Dictionary<string, Person>();

        Person? PersonOf(int playerId, int tournamentId)
        {
            if (!placeOf.TryGetValue(playerId, out var place)) return null;
            var key = place.UserId is int u ? $"u{u}" : $"p{place.PlayerId}";
            if (!people.TryGetValue(key, out var p)) people[key] = p = new Person { Key = key, UserId = place.UserId };
            var number = numberOf.GetValueOrDefault(tournamentId);
            if (number >= p.LatestSeason)
            {
                p.LatestSeason = number;
                p.Name = place.Name;
                p.PlayerId = place.PlayerId;
            }
            p.Seasons.Add(number);
            var team = teamNames.GetValueOrDefault(place.TeamId, "");
            if (!p.Teams.Any(t => t.Team == team)) p.Teams.Add((number, team));
            return p;
        }

        var dto = new LeagueStatsDto
        {
            LeagueId = league.LeagueId,
            Sport = league.Sport,
            Seasons = all.Select(s => new LeagueSeasonChipDto { Number = s.Number, Name = s.Name }).ToList(),
            Season = season,
            Basis = counted.Count == 0 ? "No seasons yet"
                : season is int one ? $"Season {one} · completed matches"
                : counted.Count == 1 ? $"Season {counted[0].Number} · completed matches"
                : $"Seasons {counted.Min(s => s.Number)}–{counted.Max(s => s.Number)} · completed matches"
        };

        if (league.Sport == Sport.Cricket) await CricketStatsAsync(dto, counted, numberOf, PersonOf, all.Count);
        else await FootballStatsAsync(dto, counted, PersonOf, all.Count);
        return Success(dto);
    }

    private sealed record Entry(Person Person, double Primary, double Secondary, string Value, string Line);

    /// <summary>
    /// A leaderboard: ranked on the primary figure (higher or lower is better), then the secondary one (lower first), equal
    /// entries sharing a rank (1, 2, 2, 4), as the tournament boards do.
    /// </summary>
    private static LeagueBoardDto Board(string key, string title, string valueLabel, IEnumerable<Entry> entries, bool higherIsBetter,
        string? qualification = null, int take = 25)
    {
        var list = entries.ToList();
        var sorted = (higherIsBetter ? list.OrderByDescending(e => e.Primary) : list.OrderBy(e => e.Primary))
            .ThenBy(e => e.Secondary)
            .ThenBy(e => e.Person.Name, StringComparer.OrdinalIgnoreCase)
            .Take(take)
            .ToList();
        var rows = new List<LeagueBoardRowDto>();
        for (var i = 0; i < sorted.Count; i++)
        {
            var e = sorted[i];
            var tied = i > 0 && sorted[i - 1].Primary.Equals(e.Primary) && sorted[i - 1].Secondary.Equals(e.Secondary);
            rows.Add(new LeagueBoardRowDto
            {
                Rank = tied ? rows[^1].Rank : i + 1,
                Name = e.Person.Name,
                Line = e.Line,
                Value = e.Value,
                UserId = e.Person.UserId,
                PlayerId = e.Person.PlayerId
            });
        }
        return new LeagueBoardDto { Key = key, Title = title, ValueLabel = valueLabel, Qualification = qualification, Total = list.Count, Rows = rows };
    }

    /// <summary>"Falcons · 3 seasons · 24 innings", "Falcons, then Wakad Warriors · 21 innings", "Baner Blasters · Season 3 only".</summary>
    private static string LineOf(Person p, int seasonsInLeague, string? measure)
    {
        var teams = p.Teams.OrderBy(t => t.Season).Select(t => t.Team).Where(t => t.Length > 0).ToList();
        var parts = new List<string>();
        if (teams.Count == 1) parts.Add(teams[0]);
        else if (teams.Count == 2) parts.Add($"{teams[0]}, then {teams[1]}");
        else if (teams.Count > 2) parts.Add($"{teams[^1]} and {teams.Count - 1} other teams");
        if (p.Seasons.Count == 1 && seasonsInLeague > 1) parts.Add($"Season {p.Seasons.Min} only");
        else if (p.Seasons.Count > 1 && teams.Count <= 1) parts.Add($"{p.Seasons.Count} seasons");
        if (!string.IsNullOrEmpty(measure)) parts.Add(measure);
        return string.Join(" · ", parts);
    }

    private static string F2(double? v) => v is double d ? d.ToString("0.00", Inv) : "—";
    private static string F1(double? v) => v is double d ? d.ToString("0.0", Inv) : "—";
    private static string N(int v) => v.ToString("N0", Inv);
    private static string Plural(int n, string word, string? plural = null) => n == 1 ? $"1 {word}" : $"{n} {plural ?? word + "s"}";

    private static string OversText(double overs)
    {
        var whole = (int)Math.Floor(overs + 1e-9);
        var rest = (int)Math.Round((overs - whole) * 6);
        return rest == 0 ? $"{whole}" : $"{whole}.{rest}";
    }

    private static string OversOf(int legalBalls, int ballsPerOver) =>
        legalBalls % ballsPerOver == 0 ? $"{legalBalls / ballsPerOver}" : $"{legalBalls / ballsPerOver}.{legalBalls % ballsPerOver}";

    private async Task CricketStatsAsync(LeagueStatsDto dto, List<Season> counted, Dictionary<int, int> numberOf,
        Func<int, int, Person?> personOf, int seasonsInLeague)
    {
        var figures = new Dictionary<Person, CricketPlayerFigures>();
        var innings = new List<(Person P, int Season, CricketInningsFigure I)>();
        var spells = new List<(Person P, int Season, CricketSpellFigure S)>();

        foreach (var s in counted)
        {
            var season = await _cricketStats.PlayerFiguresAsync(s.TournamentId);
            foreach (var (playerId, f) in season.Players)
            {
                if (personOf(playerId, s.TournamentId) is not { } p) continue;
                if (!figures.TryGetValue(p, out var total)) figures[p] = total = new CricketPlayerFigures();
                total.Add(f);
            }
            foreach (var i in season.Innings) if (personOf(i.PlayerId, s.TournamentId) is { } p) innings.Add((p, s.Number, i));
            foreach (var x in season.Spells) if (personOf(x.PlayerId, s.TournamentId) is { } p) spells.Add((p, s.Number, x));
        }

        string Bat(Person p, CricketPlayerFigures f) => LineOf(p, seasonsInLeague, $"{f.Innings} innings");
        string Bowl(Person p, CricketPlayerFigures f) => LineOf(p, seasonsInLeague, $"{OversText(f.Overs)} overs");
        string Played(Person p, CricketPlayerFigures f) => LineOf(p, seasonsInLeague, Plural(f.Matches, "match", "matches"));
        var bat = figures.Where(x => x.Value.Innings > 0).ToList();
        var bowl = figures.Where(x => x.Value.LegalBalls > 0).ToList();

        dto.Groups.Add(new LeagueStatGroupDto
        {
            Key = "batting",
            Title = "Batting",
            Boards =
            [
                Board("mostRuns", "Most runs", "Runs",
                    bat.Where(x => x.Value.Runs > 0).Select(x => new Entry(x.Key, x.Value.Runs, x.Value.BallsFaced, N(x.Value.Runs), Bat(x.Key, x.Value))), true),
                Board("highestScore", "Highest score", "Runs",
                    innings.Select(x => new Entry(x.P, x.I.Runs + (x.I.NotOut ? 0.5 : 0), x.I.Balls, $"{x.I.Runs}{(x.I.NotOut ? "*" : "")}",
                        $"Season {x.Season} · v {x.I.Opponent} · {Plural(x.I.Balls, "ball")}")), true),
                Board("bestAverage", "Best average", "Avg",
                    bat.Where(x => x.Value.Innings >= CricketStatsService.AverageMinimumInnings && x.Value.Dismissals > 0)
                        .Select(x => new Entry(x.Key, x.Value.Average!.Value, -x.Value.Runs, F2(x.Value.Average), Bat(x.Key, x.Value))), true,
                    $"At least {CricketStatsService.AverageMinimumInnings} innings and one dismissal"),
                Board("bestStrikeRate", "Best strike rate", "SR",
                    bat.Where(x => x.Value.BallsFaced >= CricketStatsService.StrikeRateMinimumBalls)
                        .Select(x => new Entry(x.Key, x.Value.StrikeRate!.Value, -x.Value.Runs, F2(x.Value.StrikeRate), Bat(x.Key, x.Value))), true,
                    $"At least {CricketStatsService.StrikeRateMinimumBalls} balls faced"),
                Board("mostSixes", "Most sixes", "6s",
                    bat.Where(x => x.Value.Sixes > 0).Select(x => new Entry(x.Key, x.Value.Sixes, x.Value.BallsFaced, N(x.Value.Sixes), Bat(x.Key, x.Value))), true),
                Board("mostFours", "Most fours", "4s",
                    bat.Where(x => x.Value.Fours > 0).Select(x => new Entry(x.Key, x.Value.Fours, x.Value.BallsFaced, N(x.Value.Fours), Bat(x.Key, x.Value))), true),
                Board("mostFifties", "Most fifties", "50s",
                    bat.Where(x => x.Value.Fifties > 0).Select(x => new Entry(x.Key, x.Value.Fifties, -x.Value.Runs, N(x.Value.Fifties), Bat(x.Key, x.Value))), true,
                    "Scores of 50 to 99"),
                Board("mostHundreds", "Most hundreds", "100s",
                    bat.Where(x => x.Value.Hundreds > 0).Select(x => new Entry(x.Key, x.Value.Hundreds, -x.Value.Runs, N(x.Value.Hundreds), Bat(x.Key, x.Value))), true),
            ]
        });

        dto.Groups.Add(new LeagueStatGroupDto
        {
            Key = "bowling",
            Title = "Bowling",
            Boards =
            [
                Board("mostWickets", "Most wickets", "Wkts",
                    bowl.Where(x => x.Value.Wickets > 0).Select(x => new Entry(x.Key, x.Value.Wickets, x.Value.RunsConceded, N(x.Value.Wickets), Bowl(x.Key, x.Value))), true),
                Board("bestFigures", "Best figures", "Figures",
                    spells.Where(x => x.S.Wickets > 0).Select(x => new Entry(x.P, x.S.Wickets, x.S.Runs, $"{x.S.Wickets}/{x.S.Runs}",
                        $"Season {x.Season} · v {x.S.Opponent} · {OversOf(x.S.LegalBalls, x.S.BallsPerOver)} overs")), true),
                Board("bestAverage", "Best average", "Avg",
                    bowl.Where(x => x.Value.Wickets >= CricketStatsService.BowlingRateMinimumWickets)
                        .Select(x => new Entry(x.Key, x.Value.BowlingAverage!.Value, -x.Value.Wickets, F2(x.Value.BowlingAverage), Bowl(x.Key, x.Value))), false,
                    $"At least {CricketStatsService.BowlingRateMinimumWickets} wickets"),
                Board("bestStrikeRate", "Best strike rate", "Balls/wkt",
                    bowl.Where(x => x.Value.Wickets >= CricketStatsService.BowlingRateMinimumWickets)
                        .Select(x => new Entry(x.Key, x.Value.LegalBalls / (double)x.Value.Wickets, -x.Value.Wickets,
                            F1(x.Value.LegalBalls / (double)x.Value.Wickets), Bowl(x.Key, x.Value))), false,
                    $"At least {CricketStatsService.BowlingRateMinimumWickets} wickets"),
                Board("bestEconomy", "Best economy", "Econ",
                    bowl.Where(x => x.Value.Overs >= CricketStatsService.EconomyMinimumOvers)
                        .Select(x => new Entry(x.Key, x.Value.Economy!.Value, -x.Value.Overs, F2(x.Value.Economy), Bowl(x.Key, x.Value))), false,
                    $"At least {CricketStatsService.EconomyMinimumOvers:0} overs"),
                Board("mostMaidens", "Most maidens", "Mdns",
                    bowl.Where(x => x.Value.Maidens > 0).Select(x => new Entry(x.Key, x.Value.Maidens, x.Value.RunsConceded, N(x.Value.Maidens), Bowl(x.Key, x.Value))), true),
                Board("mostDots", "Most dot balls", "Dots",
                    bowl.Where(x => x.Value.Dots > 0).Select(x => new Entry(x.Key, x.Value.Dots, x.Value.LegalBalls, N(x.Value.Dots), Bowl(x.Key, x.Value))), true),
            ]
        });

        var field = figures.Where(x => x.Value.Catches + x.Value.Stumpings + x.Value.RunOuts > 0).ToList();
        dto.Groups.Add(new LeagueStatGroupDto
        {
            Key = "fielding",
            Title = "Fielding",
            Boards =
            [
                Board("mostDismissals", "Most dismissals", "Dis",
                    field.Select(x => new Entry(x.Key, x.Value.Catches + x.Value.Stumpings + x.Value.RunOuts, x.Value.Matches,
                        N(x.Value.Catches + x.Value.Stumpings + x.Value.RunOuts), Played(x.Key, x.Value))), true),
                Board("mostCatches", "Most catches", "Ct",
                    field.Where(x => x.Value.Catches > 0).Select(x => new Entry(x.Key, x.Value.Catches, x.Value.Matches, N(x.Value.Catches), Played(x.Key, x.Value))), true),
                Board("mostRunOuts", "Most run-outs", "RO",
                    field.Where(x => x.Value.RunOuts > 0).Select(x => new Entry(x.Key, x.Value.RunOuts, x.Value.Matches, N(x.Value.RunOuts), Played(x.Key, x.Value))), true),
                Board("mostStumpings", "Most stumpings", "St",
                    field.Where(x => x.Value.Stumpings > 0).Select(x => new Entry(x.Key, x.Value.Stumpings, x.Value.Matches, N(x.Value.Stumpings), Played(x.Key, x.Value))), true),
            ]
        });

        dto.Groups.Add(new LeagueStatGroupDto
        {
            Key = "mvp",
            Title = "MVP",
            Boards =
            [
                Board("mvp", "Most valuable player", "Points",
                    figures.Where(x => x.Value.MvpPoints > 0)
                        .Select(x => new Entry(x.Key, x.Value.MvpPoints, x.Value.Matches, N(x.Value.MvpPoints), Played(x.Key, x.Value))), true)
            ]
        });

        dto.Notes.Add("Completed matches only; super overs are left out, as on a tournament's own stats.");
        dto.Notes.Add("A player counts across seasons through their account. Someone without one counts only in the season they played.");
        dto.Notes.Add("MVP points follow the same fantasy table as a tournament's MVP.");
    }

    private async Task FootballStatsAsync(LeagueStatsDto dto, List<Season> counted, Func<int, int, Person?> personOf, int seasonsInLeague)
    {
        var figures = new Dictionary<Person, FootballPlayerFigures>();
        foreach (var s in counted)
            foreach (var (playerId, f) in await _footballStats.PlayerFiguresAsync(s.TournamentId))
            {
                if (personOf(playerId, s.TournamentId) is not { } p) continue;
                if (!figures.TryGetValue(p, out var t)) figures[p] = t = new FootballPlayerFigures { PlayerId = p.PlayerId };
                t.Goals += f.Goals;
                t.Assists += f.Assists;
                t.Appearances += f.Appearances;
                t.Starts += f.Starts;
                t.MinutesPlayed += f.MinutesPlayed;
                t.YellowCards += f.YellowCards;
                t.StraightReds += f.StraightReds;
                t.SecondYellowReds += f.SecondYellowReds;
                t.ShootoutTaken += f.ShootoutTaken;
                t.ShootoutScored += f.ShootoutScored;
            }

        string Games(Person p, FootballPlayerFigures f) => LineOf(p, seasonsInLeague, Plural(f.Appearances, "game"));
        var played = figures.Where(x => x.Value.Appearances > 0).ToList();

        dto.Groups.Add(new LeagueStatGroupDto
        {
            Key = "scoring",
            Title = "Scoring",
            Boards =
            [
                Board("goals", "Most goals", "Goals",
                    played.Where(x => x.Value.Goals > 0).Select(x => new Entry(x.Key, x.Value.Goals, x.Value.Appearances, N(x.Value.Goals), Games(x.Key, x.Value))), true),
                Board("assists", "Most assists", "Assists",
                    played.Where(x => x.Value.Assists > 0).Select(x => new Entry(x.Key, x.Value.Assists, x.Value.Appearances, N(x.Value.Assists), Games(x.Key, x.Value))), true),
                Board("goalsAndAssists", "Goals and assists", "G+A",
                    played.Where(x => x.Value.Goals + x.Value.Assists > 0).Select(x => new Entry(x.Key, x.Value.Goals + x.Value.Assists,
                        x.Value.Appearances, N(x.Value.Goals + x.Value.Assists), Games(x.Key, x.Value))), true),
            ]
        });
        dto.Groups.Add(new LeagueStatGroupDto
        {
            Key = "playing",
            Title = "Playing",
            Boards =
            [
                Board("appearances", "Most games", "Games",
                    played.Select(x => new Entry(x.Key, x.Value.Appearances, -x.Value.MinutesPlayed, N(x.Value.Appearances),
                        LineOf(x.Key, seasonsInLeague, $"{N(x.Value.MinutesPlayed)} minutes"))), true),
                Board("minutes", "Most minutes", "Mins",
                    played.Select(x => new Entry(x.Key, x.Value.MinutesPlayed, x.Value.Appearances, N(x.Value.MinutesPlayed), Games(x.Key, x.Value))), true),
            ]
        });
        dto.Groups.Add(new LeagueStatGroupDto
        {
            Key = "discipline",
            Title = "Discipline",
            Boards =
            [
                Board("cards", "Most cards", "Cards",
                    played.Where(x => x.Value.YellowCards + x.Value.TotalReds > 0).Select(x => new Entry(x.Key, x.Value.DisciplinePoints,
                        x.Value.Appearances, N(x.Value.YellowCards + x.Value.TotalReds),
                        LineOf(x.Key, seasonsInLeague, $"{x.Value.YellowCards} yellow · {x.Value.TotalReds} red"))), true,
                    "A red counts as three yellows when ranking")
            ]
        });

        dto.Notes.Add("Completed matches only; shootout kicks aren't goals.");
        dto.Notes.Add("A player counts across seasons through their account. Someone without one counts only in the season they played.");
    }

    // ---------------------------------------------------------------------------------------------------------------------
    // Teams across seasons
    // ---------------------------------------------------------------------------------------------------------------------

    public async Task<ServiceResult<LeagueTeamsDto>> TeamsAsync(int leagueId)
    {
        var league = await _leagues.FirstOrDefaultAsync(l => l.LeagueId == leagueId && !l.IsDisabled, tracking: false);
        if (league == null) return NotFound(Gone);
        return Success(await TeamsOfLeagueAsync(league));
    }

    private async Task<LeagueTeamsDto> TeamsOfLeagueAsync(League league)
    {
        var seasons = Visible(await SeasonsAsync(league.LeagueId), Me != 0 && await IsOrganiserAsync(league.LeagueId));
        var ids = seasons.Select(s => s.TournamentId).ToList();
        var numberOf = seasons.ToDictionary(s => s.TournamentId, s => s.Number);
        var teams = await TeamsOfAsync(ids);
        var identities = new Identities(teams);
        var names = LatestNames(teams, identities, numberOf);

        var records = names.Keys.ToDictionary(k => k, _ => new LeagueTeamRecordDto());
        foreach (var group in teams.GroupBy(t => identities.Of(t.TeamId)))
        {
            records[group.Key].Name = names[group.Key];
            records[group.Key].Seasons = group.Select(t => t.TournamentId).Distinct().Count();
        }

        void Count(int? home, int? away, int? winner)
        {
            foreach (var side in new[] { home, away })
            {
                if (side is not int id) continue;
                var r = records.GetValueOrDefault(identities.Of(id));
                if (r == null) continue;
                r.Played++;
                if (winner == null) r.Drawn++;
                else if (winner == id) r.Won++;
                else r.Lost++;
            }
        }

        if (league.Sport == Sport.Cricket)
            foreach (var m in await _cricketMatches.ListAsync(m => ids.Contains(m.TournamentId)
                         && (m.Status == CricketMatchStatus.Completed || m.Status == CricketMatchStatus.Abandoned), tracking: false))
                Count(m.HomeTeamId, m.AwayTeamId, KnockoutResults.WinnerOf(m));
        else
            foreach (var m in await _matches.ListAsync(m => ids.Contains(m.TournamentId) && m.Status == MatchStatus.Completed, tracking: false))
                Count(m.HomeTeamId, m.AwayTeamId, KnockoutResults.WinnerOf(m));

        var titles = new Dictionary<int, List<int>>();
        var champions = new Dictionary<int, Champion?>();
        foreach (var s in seasons.Where(s => s.Status == TournamentStatus.Completed))
            if (await ChampionAsync(s, champions) is { } c)
            {
                var id = identities.Of(c.TeamId);
                if (!titles.TryGetValue(id, out var list)) titles[id] = list = [];
                list.Add(s.Number);
            }
        foreach (var (id, won) in titles) if (records.TryGetValue(id, out var r)) r.Titles = won.Count;

        foreach (var r in records.Values) r.WinPercent = r.Played == 0 ? 0 : (int)Math.Round(r.Won * 100.0 / r.Played);
        return new LeagueTeamsDto
        {
            Titles = titles
                .Select(t => new LeagueTitleDto { Name = names.GetValueOrDefault(t.Key, ""), Seasons = t.Value.Order().ToList(), Count = t.Value.Count })
                .OrderByDescending(t => t.Count).ThenByDescending(t => t.Seasons.Max())
                .ToList(),
            Teams = records.Values
                .OrderByDescending(r => r.Won).ThenByDescending(r => r.WinPercent).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),
            Note = "A team carries its record from season to season when the organisers bring it back, or when it's the same lasting team entering again. A new team with an old name starts its own record."
        };
    }

    // ---------------------------------------------------------------------------------------------------------------------
    // Bringing teams back
    // ---------------------------------------------------------------------------------------------------------------------

    private async Task<(Tournament? Tournament, List<Season> Seasons, ServiceError? Error)> SeasonForTeamsAsync(int tournamentId)
    {
        var tournament = await _tournaments.FirstOrDefaultAsync(t => t.Id == tournamentId, tracking: false);
        if (tournament == null) return (null, [], NotFound("Tournament not found."));
        if (tournament.LeagueId is not int leagueId) return (null, [], Conflict("This tournament isn't a season of a league, so there are no earlier teams to bring back."));
        if (tournament.IsStarted || tournament.IsClosed) return (null, [], Conflict("Teams come back before a season starts. Add any more as new teams."));
        return (tournament, await SeasonsAsync(leagueId), null);
    }

    public async Task<ServiceResult<BringBackOptionsDto>> BringBackOptionsAsync(int tournamentId)
    {
        var (tournament, seasons, error) = await SeasonForTeamsAsync(tournamentId);
        if (error is { } e) return e;

        var ids = seasons.Select(s => s.TournamentId).ToList();
        var teams = await TeamsOfAsync(ids);
        var identities = new Identities(teams);
        var here = teams.Where(t => t.TournamentId == tournamentId).ToList();
        var hereIds = here.Select(t => identities.Of(t.TeamId)).ToHashSet();
        var hereNames = here.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var squads = await _players.QueryAsync(q => q
            .Where(p => ids.Contains(p.TournamentId) && p.LeftOn == null)
            .Select(p => new { p.TeamId, Linked = p.UserId != null }), tracking: false);
        var champions = new Dictionary<int, Champion?>();

        var result = new BringBackOptionsDto();
        foreach (var s in seasons.Where(s => s.TournamentId != tournamentId).OrderByDescending(s => s.Number))
        {
            var champion = s.Status == TournamentStatus.Completed ? await ChampionAsync(s, champions) : null;
            var season = new BringBackSeasonDto { TournamentId = s.TournamentId, Number = s.Number, Name = s.Name };
            foreach (var t in teams.Where(t => t.TournamentId == s.TournamentId).OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var players = squads.Count(p => p.TeamId == t.TeamId);
                var without = squads.Count(p => p.TeamId == t.TeamId && !p.Linked);
                var parts = new List<string>();
                if (champion?.TeamId == t.TeamId) parts.Add($"Won Season {s.Number}");
                parts.Add(Plural(players, "player"));
                if (without > 0) parts.Add($"{without} without an account");
                season.Teams.Add(new BringBackTeamDto
                {
                    TeamId = t.TeamId,
                    Name = t.Name,
                    Players = players,
                    Line = string.Join(" · ", parts),
                    Reason = hereIds.Contains(identities.Of(t.TeamId)) ? $"{t.Name} is in {tournament!.Name} already."
                        : hereNames.Contains(t.Name) ? $"A team called {t.Name} is in {tournament!.Name} already. Two teams in one season can't share a name."
                        : null
                });
            }
            if (season.Teams.Count > 0) result.Seasons.Add(season);
        }
        return Success(result);
    }

    /// <summary>
    /// Makes each chosen team again in this season, linked back to the one it came from (so its record and titles carry on):
    /// its squad as it ended (players who left stay out), their cricket profiles, its default captain and its admins. A
    /// person who has a place here already is skipped and named, since a person plays for one team per tournament.
    /// </summary>
    public async Task<ServiceResult<BringBackResultDto>> BringBackAsync(int tournamentId, BringBackTeamsRequest request)
    {
        var (tournament, seasons, error) = await SeasonForTeamsAsync(tournamentId);
        if (error is { } e) return e;
        var chosen = (request.TeamIds ?? []).Distinct().ToList();
        if (chosen.Count == 0) return BadRequest("Choose the teams to bring back.");

        var ids = seasons.Select(s => s.TournamentId).ToList();
        var teams = await TeamsOfAsync(ids);
        var identities = new Identities(teams);
        var here = teams.Where(t => t.TournamentId == tournamentId).ToList();
        var hereIds = here.Select(t => identities.Of(t.TeamId)).ToHashSet();
        var hereNames = here.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var sources = new List<TeamRow>();
        foreach (var id in chosen)
        {
            var source = teams.FirstOrDefault(t => t.TeamId == id && t.TournamentId != tournamentId);
            if (source == null) return NotFound("One of those teams isn't in an earlier season of this league any more. Reload and try again.");
            if (!hereIds.Add(identities.Of(source.TeamId))) return Conflict($"{source.Name} is in {tournament!.Name} already.");
            if (!hereNames.Add(source.Name)) return Conflict($"A team called {source.Name} is in {tournament!.Name} already. Two teams in one season can't share a name.");
            sources.Add(source);
        }

        var sourceIds = sources.Select(s => s.TeamId).ToList();
        var squads = await _players.ListAsync(p => sourceIds.Contains(p.TeamId) && p.LeftOn == null, PlayerQueries.WithCricketProfile, tracking: false);
        var captains = (await _teams.QueryAsync(q => q.Where(t => sourceIds.Contains(t.Id)).Select(t => new { t.Id, t.DefaultCaptainPlayerId }), tracking: false))
            .ToDictionary(t => t.Id, t => t.DefaultCaptainPlayerId);
        var admins = await _teamAdmins.ListAsync(a => sourceIds.Contains(a.TeamId) && !a.IsDisabled, tracking: false);
        var taken = (await _players.QueryAsync(q => q.Where(p => p.TournamentId == tournamentId && p.UserId != null).Select(p => p.UserId!.Value), tracking: false)).ToHashSet();

        var made = new List<(TeamRow Source, Team Team, List<(Player From, Player To)> Places)>();
        var skipped = new List<string>();
        foreach (var source in sources)
        {
            var team = new Team
            {
                Name = source.Name,
                TournamentId = tournamentId,
                JoinCode = await NewTeamCodeAsync(made.Select(m => m.Team.JoinCode!).ToHashSet()),
                ClubId = source.ClubId,
                BroughtBackFromTeamId = source.TeamId
            };
            _teams.Add(team);
            var places = new List<(Player, Player)>();
            foreach (var from in squads.Where(p => p.TeamId == source.TeamId).OrderBy(p => p.Id))
            {
                if (from.UserId is int u && !taken.Add(u))
                {
                    skipped.Add(from.Name);
                    continue;
                }
                var place = new Player
                {
                    Name = from.Name,
                    JerseyNumber = from.JerseyNumber,
                    Position = from.Position,
                    Team = team,
                    TournamentId = tournamentId,
                    UserId = from.UserId,
                    EmailId = from.EmailId,
                    PhoneNumber = from.PhoneNumber,
                    ClubPlayerId = from.ClubPlayerId
                };
                if (from.Cricket != null)
                {
                    place.Cricket = new PlayerCricketProfile();
                    CricketProfiles.Copy(from.Cricket, place.Cricket);
                }
                team.Players.Add(place);
                places.Add((from, place));
            }
            made.Add((source, team, places));
        }

        try
        {
            await _unitOfWork.SaveChangesAsync();
        }
        catch (DuplicateEntryException)
        {
            return Conflict("Someone changed this season's teams at the same moment. Reload and try again.");
        }

        // Now the rows have ids: the default captains and the teams' admins.
        foreach (var (source, team, places) in made)
        {
            if (captains.GetValueOrDefault(source.TeamId) is int captain && places.FirstOrDefault(p => p.From.Id == captain).To is { } to)
                team.DefaultCaptainPlayerId = to.Id;
            foreach (var admin in admins.Where(a => a.TeamId == source.TeamId))
            {
                var copy = new TeamAdmin { TeamId = team.Id, TournamentId = tournamentId, UserId = admin.UserId };
                copy.Created(Now, Me);
                _teamAdmins.Add(copy);
            }
        }
        await _unitOfWork.SaveChangesAsync();

        var names = made.Select(m => m.Team.Name).ToList();
        var notice = $"{Names(names)} {(names.Count == 1 ? "is" : "are")} back, with the squad{(names.Count == 1 ? "" : "s")} they ended with.";
        if (skipped.Count > 0)
            notice += $" {Names(skipped)} already {(skipped.Count == 1 ? "has" : "have")} a place in this season, so {(skipped.Count == 1 ? "isn't" : "aren't")} in {(skipped.Count == 1 ? "a" : "those")} squad{(skipped.Count == 1 ? "" : "s")} too.";
        return Success(new BringBackResultDto { Added = made.Count, Notice = notice });
    }

    private async Task<string> NewTeamCodeAsync(HashSet<string> alsoTaken)
    {
        string code;
        do code = JoinCodes.New();
        while (alsoTaken.Contains(code) || await _teams.AnyAsync(t => t.JoinCode == code));
        return code;
    }

    private static string Names(List<string> names) => names.Count switch
    {
        0 => "",
        1 => names[0],
        _ => $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}"
    };

    // ---------------------------------------------------------------------------------------------------------------------
    // The signed-out pages
    // ---------------------------------------------------------------------------------------------------------------------

    /// <summary>The league from its link: the code itself, or the whole address (".../leagues/ABCD2345").</summary>
    private async Task<League?> ByCodeAsync(string? typed)
    {
        var text = (typed ?? "").Trim();
        var at = text.LastIndexOf("/leagues/", StringComparison.OrdinalIgnoreCase);
        if (at >= 0) text = text[(at + "/leagues/".Length)..];
        var code = JoinCodes.Normalise(text.Split('?', '/', '#')[0], PublicCodes.Length);
        return code == null ? null : await _leagues.FirstOrDefaultAsync(l => l.PublicCode == code && !l.IsDisabled, tracking: false);
    }

    public async Task<ServiceResult<JsonNode>> PublicAsync(string code)
    {
        if (await ByCodeAsync(code) is not { } league) return NotFound(NotAPublicLeague);
        var page = await PageAsync(league);
        page.Organisers = [];
        return Success(PublicShapes.Of(page)!);
    }

    public async Task<ServiceResult<JsonNode>> PublicStatsAsync(string code, int? season)
    {
        if (await ByCodeAsync(code) is not { } league) return NotFound(NotAPublicLeague);
        var stats = await StatsOfAsync(league, season);
        if (stats.Value is not { } value) return NotFound($"{league.Name} has no season {season}.");
        return Success(PublicShapes.Of(value)!);
    }

    public async Task<ServiceResult<JsonNode>> PublicTeamsAsync(string code)
    {
        if (await ByCodeAsync(code) is not { } league) return NotFound(NotAPublicLeague);
        return Success(PublicShapes.Of(await TeamsOfLeagueAsync(league))!);
    }

    // ---------------------------------------------------------------------------------------------------------------------
    // Account deletion
    // ---------------------------------------------------------------------------------------------------------------------

    public async Task ForgetUserAsync(int userId)
    {
        var seats = await _organisers.ListAsync(o => o.UserId == userId);
        foreach (var seat in seats)
        {
            _organisers.Remove(seat);
            var others = await _organisers.AnyAsync(o => o.LeagueId == seat.LeagueId && o.UserId != userId);
            if (others) continue;
            // The last organiser: the league passes to whoever made its latest season, if that's someone else.
            var heir = (await SeasonsAsync(seat.LeagueId)).Select(s => s.CreatedByUserId).LastOrDefault(id => id != userId && id != SystemUser.Id);
            if (heir != 0 && await _users.AnyAsync(u => u.UserId == heir && u.DeletedOn == null))
            {
                var next = new LeagueOrganiser { LeagueId = seat.LeagueId, UserId = heir };
                next.Created(Now, userId);
                _organisers.Add(next);
            }
        }
    }
}
