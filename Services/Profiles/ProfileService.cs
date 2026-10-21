using System.Globalization;
using TournamentScheduler.Api.Auth;
using TournamentScheduler.Api.Data.DataServices;
using TournamentScheduler.Api.Models;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Models.Profiles;
using TournamentScheduler.Api.Services.Cricket;

namespace TournamentScheduler.Api.Services.Profiles;

public interface IProfileService
{
    /// <summary>Your profile with no ids; someone else's by their account or by a squad place linked to it.</summary>
    Task<ServiceResult<ProfileDto>> GetProfileAsync(int? userId, int? playerId);

    Task<ServiceResult<CareerDto>> GetCareerAsync(Sport sport, int? userId, int? playerId);
}

/// <summary>
/// A person's profile and career, added up from every tournament where their account is linked to a
/// squad place (one place per tournament). The figures come from the tournament stats engines
/// themselves (<see cref="IStatsService"/>, <see cref="ICricketStatsService"/>), completed matches
/// only, so a career always agrees with the boards it was built from; rates are worked out once from
/// the summed counts, never averaged.
///
/// Anyone signed in may open a player's profile, but only a player's: someone with no linked place
/// is not findable by id. Email and phone are only ever on your own.
/// </summary>
public class ProfileService : ServiceBase, IProfileService
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private readonly IDataService<User> _users;
    private readonly IDataService<Player> _players;
    private readonly IDataService<TournamentMember> _members;
    private readonly IDataService<Tournament> _tournaments;
    private readonly IStatsService _football;
    private readonly ICricketStatsService _cricket;
    private readonly ICurrentUser _currentUser;

    public ProfileService(
        IDataService<User> users,
        IDataService<Player> players,
        IDataService<TournamentMember> members,
        IDataService<Tournament> tournaments,
        IStatsService football,
        ICricketStatsService cricket,
        ICurrentUser currentUser)
    {
        _users = users;
        _players = players;
        _members = members;
        _tournaments = tournaments;
        _football = football;
        _cricket = cricket;
        _currentUser = currentUser;
    }

    /// <summary>A squad place linked to the person, with its team and tournament.</summary>
    private sealed record Place(
        int PlayerId, int TeamId, string TeamName, bool IsCaptain,
        int TournamentId, string TournamentName, Sport Sport, TournamentStatus Status,
        DateOnly? StartDate, DateOnly? EndDate, DateTime? CompletedAt);

    // -----------------------------------------------------------------
    // Profile
    // -----------------------------------------------------------------

    public async Task<ServiceResult<ProfileDto>> GetProfileAsync(int? userId, int? playerId)
    {
        var (user, error) = await ResolveAsync(userId, playerId);
        if (user == null) return error!.Value;

        var me = user.UserId == _currentUser.UserId;
        var places = await PlacesAsync(user.UserId);

        var profile = new ProfileDto
        {
            UserId = user.UserId,
            Name = user.Name,
            IsMe = me,
            MemberSince = user.Crd,
            Email = me ? user.EmailId : null,
            Phone = me ? user.PhoneNumber : null,
            TournamentsRun = (await _members.QueryAsync(q => q
                    .Where(m => m.UserId == user.UserId && !m.IsDisabled)
                    .Select(m => m.TournamentId)
                    .Distinct(), tracking: false)).Count
        };

        foreach (var sport in Enum.GetValues<Sport>())
        {
            var mine = places.Where(p => p.Sport == sport).ToList();
            var card = new SportCardDto { Sport = sport.ToString(), Tournaments = mine.Count };

            if (sport == Sport.Football)
            {
                var (total, _, _) = await FootballAsync(mine);
                card.Matches = total.Appearances;
                if (total.Appearances > 0)
                    card.Headline = new() { Stat("goals", "Goals", total.Goals), Stat("assists", "Assists", total.Assists) };
            }
            else
            {
                var (total, _, _) = await CricketAsync(mine);
                card.Matches = total.Matches;
                if (total.Matches > 0)
                    card.Headline = new() { Stat("runs", "Runs", total.Runs), Stat("wickets", "Wickets", total.Wickets) };
            }

            profile.Sports.Add(card);
        }

        return Success(profile);
    }

    // -----------------------------------------------------------------
    // Career
    // -----------------------------------------------------------------

    public async Task<ServiceResult<CareerDto>> GetCareerAsync(Sport sport, int? userId, int? playerId)
    {
        var (user, error) = await ResolveAsync(userId, playerId);
        if (user == null) return error!.Value;

        var places = (await PlacesAsync(user.UserId)).Where(p => p.Sport == sport).ToList();
        var career = new CareerDto
        {
            UserId = user.UserId,
            Name = user.Name,
            IsMe = user.UserId == _currentUser.UserId,
            Sport = sport.ToString()
        };

        if (sport == Sport.Football) await FillFootballAsync(career, places);
        else await FillCricketAsync(career, places);

        var open = await OpenableAsync(places.Select(p => p.TournamentId).Distinct().ToList(), career.IsMe);
        career.Teams.ForEach(t => t.CanOpen = open.Contains(t.TournamentId));
        career.Tournaments.ForEach(t => t.CanOpen = open.Contains(t.TournamentId));

        var tournaments = places.Count;
        career.Basis = career.Matches == 0
            ? "No completed matches yet — figures appear as matches finish."
            : $"From {Plural(career.Matches, "completed match", "completed matches")} in {Plural(tournaments, "tournament", "tournaments")}.";

        return Success(career);
    }

    private async Task FillFootballAsync(CareerDto career, List<Place> places)
    {
        var (t, perPlace, all) = await FootballAsync(places);
        career.Matches = t.Appearances;

        career.StatGroups.Add(new StatGroupDto
        {
            Key = "overview",
            Title = "Overview",
            Stats = new()
            {
                Stat("matches", "Matches", t.Appearances, t.Appearances > 0 ? $"{Plural(t.Starts, "start", "starts")}" : null),
                Stat("goals", "Goals", t.Goals, PerMatch(t.Goals, t.Appearances)),
                Stat("assists", "Assists", t.Assists, PerMatch(t.Assists, t.Appearances)),
                Stat("goalContributions", "Goals + assists", t.Goals + t.Assists),
                Stat("minutes", "Minutes", t.MinutesPlayed, t.Appearances > 0 ? $"{t.MinutesPlayed / t.Appearances} a match" : null),
                Stat("yellowCards", "Yellow cards", t.YellowCards),
                Stat("redCards", "Red cards", t.TotalReds, t.SecondYellowReds > 0 ? $"{t.SecondYellowReds} for a second yellow" : null),
                Stat("shootout", "Shootout penalties", t.ShootoutScored, t.ShootoutTaken > 0 ? $"scored of {t.ShootoutTaken} taken" : null),
            }
        });

        foreach (var place in places.OrderByDescending(p => p.StartDate ?? DateOnly.MinValue).ThenByDescending(p => p.TournamentId))
        {
            var f = perPlace[place.PlayerId];
            career.Teams.Add(Team(place, f.Appearances));
            career.Tournaments.Add(Tournament(place, f.Appearances, new()
            {
                Stat("goals", "Goals", f.Goals),
                Stat("assists", "Assists", f.Assists),
                Stat("minutes", "Minutes", f.MinutesPlayed),
            }));

            AddWinner(career, place, await _football.ChampionAsync(place.TournamentId));

            // A completed tournament's top scorer and top assister; level players share the title.
            if (place.Status != TournamentStatus.Completed) continue;
            var field = all[place.TournamentId];
            AddTitle(career, place, field, f, x => x.Goals, "TopScorer", "Top scorer", "goal", "goals");
            AddTitle(career, place, field, f, x => x.Assists, "MostAssists", "Most assists", "assist", "assists");
        }

        career.HonourStats = new()
        {
            Stat("topScorer", "Top scorer", career.Honours.Count(h => h.Kind == "TopScorer")),
            Stat("mostAssists", "Most assists", career.Honours.Count(h => h.Kind == "MostAssists")),
            Stat("tournamentsWon", "Tournaments won", career.Honours.Count(h => h.Kind == "TournamentWinner")),
        };
        career.Honours = career.Honours.OrderByDescending(h => h.Date).ToList();
    }

    /// <summary>Their team won the tournament's Final: every player in that squad shares the title.</summary>
    private static void AddWinner(CareerDto career, Place place, int? champion)
    {
        if (champion != place.TeamId) return;
        career.Honours.Add(new HonourDto
        {
            Kind = "TournamentWinner",
            Title = "Tournament winner",
            Detail = place.IsCaptain ? $"Captain of {place.TeamName}" : $"With {place.TeamName}",
            TournamentId = place.TournamentId,
            TournamentName = place.TournamentName,
            Date = place.CompletedAt
        });
    }

    private static void AddTitle(CareerDto career, Place place, Dictionary<int, FootballPlayerFigures> field,
        FootballPlayerFigures mine, Func<FootballPlayerFigures, int> value, string kind, string title, string one, string many)
    {
        var best = field.Values.Select(value).DefaultIfEmpty(0).Max();
        if (best == 0 || value(mine) != best) return;
        var shared = field.Values.Count(x => value(x) == best) > 1;

        career.Honours.Add(new HonourDto
        {
            Kind = kind,
            Title = shared ? $"Joint {title.ToLowerInvariant()}" : title,
            Detail = Plural(best, one, many),
            TournamentId = place.TournamentId,
            TournamentName = place.TournamentName,
            Date = place.CompletedAt
        });
    }

    private async Task FillCricketAsync(CareerDto career, List<Place> places)
    {
        var (t, perPlace, all) = await CricketAsync(places);
        career.Matches = t.Matches;

        career.StatGroups.Add(new StatGroupDto
        {
            Key = "batting",
            Title = "Batting",
            Stats = new()
            {
                Stat("innings", "Innings", t.Innings, t.NotOuts > 0 ? $"{t.NotOuts} not out" : null),
                Stat("runs", "Runs", t.Runs, t.BallsFaced > 0 ? $"off {Plural(t.BallsFaced, "ball", "balls")}" : null),
                Text("highest", "Highest score", t.High < 0 ? "—" : $"{t.High}{(t.HighNotOut ? "*" : "")}", t.High >= 0 && t.HighNotOut ? "not out" : null),
                Text("average", "Average", Rate(t.Average), t.Average == null && t.Innings > 0 ? "needs a dismissal" : null),
                Text("strikeRate", "Strike rate", Rate(t.StrikeRate)),
                Text("fiftiesHundreds", "50s / 100s", $"{t.Fifties} / {t.Hundreds}"),
                Text("foursSixes", "4s / 6s", $"{t.Fours} / {t.Sixes}"),
            }
        });
        career.StatGroups.Add(new StatGroupDto
        {
            Key = "bowling",
            Title = "Bowling",
            Stats = new()
            {
                Stat("wickets", "Wickets", t.Wickets, t.LegalBalls > 0 ? $"in {OversText(t.Overs)} overs" : null),
                Text("best", "Best figures", t.BestWickets < 0 ? "—" : $"{t.BestWickets}/{t.BestRuns}"),
                Text("economy", "Economy", Rate(t.Economy), t.Economy != null ? "runs an over" : null),
                Text("average", "Average", Rate(t.BowlingAverage), t.BowlingAverage != null ? "runs a wicket" : null),
                Text("hauls", "3w / 5w hauls", $"{t.Hauls3} / {t.Hauls5}"),
            }
        });
        career.StatGroups.Add(new StatGroupDto
        {
            Key = "fielding",
            Title = "Fielding",
            Stats = new()
            {
                Stat("catches", "Catches", t.Catches, "including caught and bowled"),
                Stat("stumpings", "Stumpings", t.Stumpings, "as wicket-keeper"),
                Stat("runOuts", "Run-outs", t.RunOuts, "direct hit or the throw"),
            }
        });

        foreach (var place in places.OrderByDescending(p => p.StartDate ?? DateOnly.MinValue).ThenByDescending(p => p.TournamentId))
        {
            var f = perPlace[place.PlayerId];
            var figures = all[place.TournamentId];
            career.Teams.Add(Team(place, f.Matches));
            career.Tournaments.Add(Tournament(place, f.Matches, new()
            {
                Stat("runs", "Runs", f.Runs),
                Stat("wickets", "Wickets", f.Wickets),
                Stat("catches", "Catches", f.Catches),
            }));

            foreach (var award in figures.PlayersOfMatch.Where(a => a.PlayerId == place.PlayerId))
            {
                career.Honours.Add(new HonourDto
                {
                    Kind = "PlayerOfMatch",
                    Title = "Player of the match",
                    Detail = award.Summary,
                    TournamentId = place.TournamentId,
                    TournamentName = place.TournamentName,
                    MatchId = award.MatchId,
                    Context = $"Match {award.MatchNumber} · {award.Fixture}",
                    Date = award.Date
                });
            }

            AddWinner(career, place, await _cricket.ChampionAsync(place.TournamentId));

            // A completed tournament's MVP: the most points, then fewer matches — the MVP board's order.
            if (place.Status == TournamentStatus.Completed && f.MvpPoints > 0)
            {
                var ranked = figures.Players.Values.Where(x => x.MvpPoints > 0).ToList();
                var top = ranked.Max(x => x.MvpPoints);
                var fewest = ranked.Where(x => x.MvpPoints == top).Min(x => x.Matches);
                if (f.MvpPoints == top && f.Matches == fewest)
                {
                    var shared = ranked.Count(x => x.MvpPoints == top && x.Matches == fewest) > 1;
                    career.Honours.Add(new HonourDto
                    {
                        Kind = "TournamentMvp",
                        Title = shared ? "Joint most valuable player" : "Most valuable player",
                        Detail = $"{f.MvpPoints} MVP points",
                        TournamentId = place.TournamentId,
                        TournamentName = place.TournamentName,
                        Date = place.CompletedAt
                    });
                }
            }
        }

        career.HonourStats = new()
        {
            Stat("playerOfMatch", "Player of the match", career.Honours.Count(h => h.Kind == "PlayerOfMatch")),
            Stat("mvpPoints", "MVP points", t.MvpPoints),
            Stat("tournamentMvp", "Tournament MVP", career.Honours.Count(h => h.Kind == "TournamentMvp")),
            Stat("tournamentsWon", "Tournaments won", career.Honours.Count(h => h.Kind == "TournamentWinner")),
        };
        career.Honours = career.Honours.OrderByDescending(h => h.Date).ToList();
    }

    // -----------------------------------------------------------------
    // Adding up
    // -----------------------------------------------------------------

    /// <summary>The total, each place's own figures, and every tournament's whole field (for titles).</summary>
    private async Task<(FootballPlayerFigures Total, Dictionary<int, FootballPlayerFigures> PerPlace, Dictionary<int, Dictionary<int, FootballPlayerFigures>> All)>
        FootballAsync(List<Place> places)
    {
        var total = new FootballPlayerFigures();
        var perPlace = new Dictionary<int, FootballPlayerFigures>();
        var all = new Dictionary<int, Dictionary<int, FootballPlayerFigures>>();

        foreach (var place in places)
        {
            var field = await _football.PlayerFiguresAsync(place.TournamentId);
            all[place.TournamentId] = field;
            var f = field.GetValueOrDefault(place.PlayerId) ?? new FootballPlayerFigures { PlayerId = place.PlayerId };
            perPlace[place.PlayerId] = f;

            total.Goals += f.Goals;
            total.Assists += f.Assists;
            total.Appearances += f.Appearances;
            total.Starts += f.Starts;
            total.MinutesPlayed += f.MinutesPlayed;
            total.YellowCards += f.YellowCards;
            total.StraightReds += f.StraightReds;
            total.SecondYellowReds += f.SecondYellowReds;
            total.ShootoutTaken += f.ShootoutTaken;
            total.ShootoutScored += f.ShootoutScored;
        }

        return (total, perPlace, all);
    }

    private async Task<(CricketPlayerFigures Total, Dictionary<int, CricketPlayerFigures> PerPlace, Dictionary<int, CricketTournamentFigures> All)>
        CricketAsync(List<Place> places)
    {
        var total = new CricketPlayerFigures();
        var perPlace = new Dictionary<int, CricketPlayerFigures>();
        var all = new Dictionary<int, CricketTournamentFigures>();

        foreach (var place in places)
        {
            var figures = await _cricket.PlayerFiguresAsync(place.TournamentId);
            all[place.TournamentId] = figures;
            var f = figures.Players.GetValueOrDefault(place.PlayerId) ?? new CricketPlayerFigures();
            perPlace[place.PlayerId] = f;
            total.Add(f);
        }

        return (total, perPlace, all);
    }

    // -----------------------------------------------------------------
    // Who
    // -----------------------------------------------------------------

    /// <summary>
    /// Me, the account behind a squad place, or an account by id. Deleted, blocked and system accounts
    /// have no profile, and nor does anyone else with no linked squad place.
    /// </summary>
    private async Task<(User? User, ServiceError? Error)> ResolveAsync(int? userId, int? playerId)
    {
        if (playerId is int pid)
        {
            var player = await _players.FirstOrDefaultAsync(p => p.Id == pid, tracking: false);
            if (player == null) return (null, NotFound("Player not found."));
            if (player.UserId is not int linked)
                return (null, NotFound("This player hasn't linked an account yet, so there's no profile to show."));
            userId = linked;
        }

        var id = userId ?? _currentUser.UserId;
        var user = await _users.FirstOrDefaultAsync(u => u.UserId == id, tracking: false);
        if (user == null || user.DeletedOn != null || user.IsDisabled || user.UserId == SystemUser.Id)
            return (null, NotFound("Profile not found."));

        if (user.UserId != _currentUser.UserId && !await _players.AnyAsync(p => p.UserId == user.UserId))
            return (null, NotFound("Profile not found."));

        return (user, null);
    }

    /// <summary>
    /// Which of these tournaments the viewer may open: the same people tournament access lets in
    /// (its creator, owners and scorers, and its players). Your own are always yours.
    /// </summary>
    private async Task<HashSet<int>> OpenableAsync(List<int> tournamentIds, bool isMe)
    {
        if (isMe || tournamentIds.Count == 0) return tournamentIds.ToHashSet();

        var me = _currentUser.UserId;
        var member = await _members.QueryAsync(q => q
            .Where(m => m.UserId == me && !m.IsDisabled && tournamentIds.Contains(m.TournamentId))
            .Select(m => m.TournamentId), tracking: false);
        var created = await _tournaments.QueryAsync(q => q
            .Where(t => t.CreatedByUserId == me && tournamentIds.Contains(t.Id))
            .Select(t => t.Id), tracking: false);
        var plays = await _players.QueryAsync(q => q
            .Where(p => p.UserId == me && tournamentIds.Contains(p.TournamentId))
            .Select(p => p.TournamentId), tracking: false);
        return member.Concat(created).Concat(plays).ToHashSet();
    }

    private Task<List<Place>> PlacesAsync(int userId) =>
        _players.QueryAsync(q => q
            .Where(p => p.UserId == userId)
            .Select(p => new Place(
                p.Id, p.TeamId, p.Team!.Name, p.Team.DefaultCaptainPlayerId == p.Id,
                p.Team.TournamentId, p.Team.Tournament!.Name, p.Team.Tournament.Sport, p.Team.Tournament.Status,
                p.Team.Tournament.StartDate, p.Team.Tournament.EndDate, p.Team.Tournament.CompletedAt)), tracking: false);

    // -----------------------------------------------------------------
    // Display
    // -----------------------------------------------------------------

    private static CareerTeamDto Team(Place p, int matches) => new()
    {
        TeamId = p.TeamId,
        TeamName = p.TeamName,
        TournamentId = p.TournamentId,
        TournamentName = p.TournamentName,
        TournamentStatus = p.Status.ToString(),
        Matches = matches,
        IsCaptain = p.IsCaptain
    };

    private static CareerTournamentDto Tournament(Place p, int matches, List<StatDto> highlights) => new()
    {
        TournamentId = p.TournamentId,
        Name = p.TournamentName,
        Status = p.Status.ToString(),
        StartDate = p.StartDate,
        EndDate = p.EndDate,
        TeamId = p.TeamId,
        TeamName = p.TeamName,
        Matches = matches,
        Highlights = highlights
    };

    private static StatDto Stat(string key, string label, int value, string? sub = null) =>
        new() { Key = key, Label = label, Value = value.ToString(Inv), Sub = sub };

    private static StatDto Text(string key, string label, string value, string? sub = null) =>
        new() { Key = key, Label = label, Value = value, Sub = sub };

    private static string Rate(double? value) => value is double v ? v.ToString("0.00", Inv) : "—";

    private static string? PerMatch(int value, int matches) =>
        matches > 0 && value > 0 ? $"{(value / (double)matches).ToString("0.00", Inv)} a match" : null;

    private static string OversText(double overs)
    {
        var balls = (int)Math.Round(overs * 6);
        return $"{balls / 6}.{balls % 6}";
    }

    private static string Plural(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";
}
