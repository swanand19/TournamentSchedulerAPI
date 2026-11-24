using Microsoft.Extensions.Options;
using TournamentScheduler.Api.Auth;
using TournamentScheduler.Api.Data;
using TournamentScheduler.Api.Data.DataServices;
using TournamentScheduler.Api.Data.Queries;
using TournamentScheduler.Api.Models;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Models.Clubs;
using TournamentScheduler.Api.Models.Messaging;
using TournamentScheduler.Api.Models.Public;
using TournamentScheduler.Api.Pictures;
using TournamentScheduler.Api.Services.Access;
using TournamentScheduler.Api.Services.Notifications;

namespace TournamentScheduler.Api.Services.Clubs;

public interface IClubEntryService
{
    // The organiser's side (Edit on the tournament).
    Task<ServiceResult<TournamentEntriesDto>> ListAsync(int tournamentId);
    Task<ServiceResult<TournamentEntriesDto>> SetCodeAsync(int tournamentId, EntryCodeRequest request);
    Task<ServiceResult<TournamentEntriesDto>> AcceptAsync(int tournamentId, int entryId);
    Task<ServiceResult<TournamentEntriesDto>> DeclineAsync(int tournamentId, int entryId);

    // A lasting team's admins (not tournament-scoped; the service checks they run the club).
    Task<ServiceResult<EntryPreviewDto>> PreviewAsync(EntryPreviewRequest request);
    Task<ServiceResult<EntryPreviewDto>> RequestAsync(EntryRequest request);
    Task<ServiceResult<EntryPreviewDto>> UpdateAsync(int entryId, EntryUpdateRequest request);
    Task<ServiceResult<bool>> WithdrawAsync(int entryId);

    /// <summary>
    /// Entries close (C-10): the schedule was approved, a knockout set up, or the tournament started. The code stops
    /// working and waiting entries are cancelled, their admins told. Stages; the caller saves with its own change.
    /// </summary>
    Task CloseAsync(int tournamentId);
}

/// <summary>
/// Entering a tournament with its code (docs/reusable-teams-plan.md section 6, step 3). The organiser switches the code
/// on and sends it to the teams they want; the code is the only way in (C-11). A lasting team's admin types it, picks the
/// squad (everyone ticked; anyone with a place in the tournament already greyed) and sends it; the organiser accepts or
/// declines (C-7). Accepting makes an ordinary team with <see cref="Team.ClubId"/>, which the club's admins then manage.
/// </summary>
public class ClubEntryService(
    IDataService<Tournament> tournaments,
    IDataService<Team> teams,
    IDataService<Club> clubs,
    IDataService<ClubAdmin> clubAdmins,
    IDataService<ClubPlayer> clubPlayers,
    IDataService<TournamentEntry> entries,
    IDataService<TournamentEntryPlayer> entryPlayers,
    IDataService<User> users,
    ClubSquads squads,
    INotifier notifier,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    JoinCodeAttempts attempts,
    PictureLinks pictureLinks,
    IOptions<PublicOptions> publicOptions,
    TimeProvider clock) : ServiceBase, IClubEntryService
{
    private int Me => currentUser.UserId;
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private string ActorName => string.IsNullOrWhiteSpace(currentUser.Name) ? "Someone" : currentUser.Name;

    // ---------------------------------------------------------------------------------------------------------------------
    // The organiser's side
    // ---------------------------------------------------------------------------------------------------------------------

    public async Task<ServiceResult<TournamentEntriesDto>> ListAsync(int tournamentId)
    {
        var tournament = await tournaments.FirstOrDefaultAsync(t => t.Id == tournamentId, tracking: false);
        if (tournament == null) return NotFound("Tournament not found.");
        return Success(await AnswerAsync(tournament, null));
    }

    public async Task<ServiceResult<TournamentEntriesDto>> SetCodeAsync(int tournamentId, EntryCodeRequest request)
    {
        var tournament = await tournaments.FirstOrDefaultAsync(t => t.Id == tournamentId);
        if (tournament == null) return NotFound("Tournament not found.");
        if (request.Open && WhyClosed(tournament) is { } closed) return Conflict(closed);

        if (request.Open && tournament.EntryCode == null) tournament.EntryCode = await NewEntryCodeAsync();
        tournament.EntriesOpen = request.Open;
        await unitOfWork.SaveChangesAsync();
        return Success(await AnswerAsync(tournament, null));
    }

    /// <summary>
    /// Makes the team: an ordinary <see cref="Team"/> linked to the club, a squad place per ticked player who can still
    /// play here (anyone who took a place meanwhile is skipped and named), the club's captain as its default captain.
    /// </summary>
    public async Task<ServiceResult<TournamentEntriesDto>> AcceptAsync(int tournamentId, int entryId)
    {
        var tournament = await tournaments.FirstOrDefaultAsync(t => t.Id == tournamentId, tracking: false);
        if (tournament == null) return NotFound("Tournament not found.");
        var entry = await entries.FirstOrDefaultAsync(e => e.TournamentEntryId == entryId && e.TournamentId == tournamentId);
        if (entry == null) return NotFound("That entry isn't here any more.");
        if (entry.Status != EntryStatus.Waiting) return Conflict(Answered(entry.Status));
        var club = await clubs.FirstOrDefaultAsync(c => c.ClubId == entry.ClubId, tracking: false);
        if (club == null) return NotFound("That team doesn't exist any more.");
        if (await teams.AnyAsync(t => t.TournamentId == tournamentId && t.Name == entry.TeamName))
            return Conflict($"A team called {entry.TeamName} is in the tournament now. Ask {club.Name}'s admins to change their team's name, or decline the entry.");

        var ticked = await TickedAsync(entry.TournamentEntryId);
        var reasons = await squads.ReasonsAsync(tournamentId, ticked, null);
        var team = new Team { Name = entry.TeamName, TournamentId = tournamentId, JoinCode = await NewTeamCodeAsync(), ClubId = club.ClubId };
        teams.Add(team);
        var places = ticked.Where(p => !reasons.ContainsKey(p.ClubPlayerId))
            .Select(p => (Club: p, Place: NewPlace(p, team, tournament.Sport)))
            .ToList();

        entry.Status = EntryStatus.Accepted;
        entry.DecidedByUserId = Me;
        entry.DecidedAt = Now;
        try
        {
            await unitOfWork.SaveChangesAsync();
        }
        catch (DuplicateEntryException)
        {
            return Conflict("Someone changed this tournament's teams at the same moment. Reload and try again.");
        }

        // Now the rows have ids: the entry's team, the default captain, and the news.
        entry.TeamId = team.Id;
        if (places.FirstOrDefault(p => p.Club.IsCaptain).Place is { } captain) team.DefaultCaptainPlayerId = captain.Id;
        foreach (var admin in await AdminsOfAsync(club.ClubId))
            await notifier.NotifyAsync(admin, NotificationTemplateCode.EntryAccepted,
                Notifier.With(("TeamName", team.Name), ("TournamentName", tournament.Name)), NotificationLink.ToClub(club.ClubId));
        await unitOfWork.SaveChangesAsync();

        var skipped = ticked.Where(p => reasons.ContainsKey(p.ClubPlayerId)).Select(p => p.Name).ToList();
        var notice = skipped.Count == 0
            ? $"{team.Name} is in, with {Plural(places.Count, "player")}."
            : $"{team.Name} is in. {Names(skipped)} already {(skipped.Count == 1 ? "has" : "have")} a place in this tournament, so {(skipped.Count == 1 ? "isn't" : "aren't")} in its squad.";
        return Success(await AnswerAsync(tournament, notice));
    }

    /// <summary>A squad place for an accepted entry: the team isn't saved yet, so it's joined through the navigation.</summary>
    private Player NewPlace(ClubPlayer from, Team team, Sport sport)
    {
        var place = squads.NewPlace(from, 0, team.TournamentId, sport);
        place.Team = team;
        return place;
    }

    public async Task<ServiceResult<TournamentEntriesDto>> DeclineAsync(int tournamentId, int entryId)
    {
        var tournament = await tournaments.FirstOrDefaultAsync(t => t.Id == tournamentId, tracking: false);
        if (tournament == null) return NotFound("Tournament not found.");
        var entry = await entries.FirstOrDefaultAsync(e => e.TournamentEntryId == entryId && e.TournamentId == tournamentId);
        if (entry == null) return NotFound("That entry isn't here any more.");
        if (entry.Status != EntryStatus.Waiting) return Conflict(Answered(entry.Status));

        entry.Status = EntryStatus.Declined;
        entry.DecidedByUserId = Me;
        entry.DecidedAt = Now;
        foreach (var admin in await AdminsOfAsync(entry.ClubId))
            await notifier.NotifyAsync(admin, NotificationTemplateCode.EntryDeclined,
                Notifier.With(("TeamName", entry.TeamName), ("TournamentName", tournament.Name)), NotificationLink.ToClub(entry.ClubId));
        await unitOfWork.SaveChangesAsync();
        return Success(await AnswerAsync(tournament, $"You declined {entry.TeamName}. Its admins have been told."));
    }

    private static string Answered(EntryStatus status) => status switch
    {
        EntryStatus.Accepted => "That team is in already.",
        EntryStatus.Withdrawn => "Its admins withdrew that entry.",
        EntryStatus.Cancelled => "Entries closed before that one was answered.",
        _ => "That entry has been answered already."
    };

    /// <summary>Why "Teams can enter with a code" can't be switched on; null while it can.</summary>
    private static string? WhyClosed(Tournament tournament) =>
        tournament.IsClosed ? "This tournament is over, so teams can't enter it."
        : tournament.IsStarted ? "The tournament has started, so teams can't enter with the code any more. Add a team by hand instead."
        : null;

    private async Task<TournamentEntriesDto> AnswerAsync(Tournament tournament, string? notice)
    {
        var dto = new TournamentEntriesDto
        {
            Open = tournament.EntriesOpen && WhyClosed(tournament) == null,
            Code = tournament.EntryCode,
            Closed = WhyClosed(tournament),
            Notice = notice
        };
        if (dto.Open && dto.Code is { } code)
        {
            var site = publicOptions.Value.WebAppUrl.Trim().TrimEnd('/');
            dto.Url = site.Length == 0 ? null : $"{site}/enter/{code}";
            dto.ShareText = $"Enter your team in {tournament.Name} on KickCricket: open your team in the app, tap Enter a tournament and type {code}."
                            + (dto.Url == null ? "" : $" Or open {dto.Url}");
        }

        var waiting = await entries.ListAsync(e => e.TournamentId == tournament.Id && e.Status == EntryStatus.Waiting,
            q => q.OrderBy(e => e.RequestedAt), tracking: false);
        if (waiting.Count == 0) return dto;

        var clubIds = waiting.Select(e => e.ClubId).Distinct().ToList();
        var clubRows = (await clubs.ListAsync(c => clubIds.Contains(c.ClubId), tracking: false)).ToDictionary(c => c.ClubId);
        var played = (await teams.QueryAsync(q => q.Where(t => t.ClubId != null && clubIds.Contains(t.ClubId.Value)).Select(t => t.ClubId!.Value), tracking: false))
            .GroupBy(id => id).ToDictionary(g => g.Key, g => g.Count());
        var askerIds = waiting.Select(e => e.RequestedByUserId).Distinct().ToList();
        var askers = (await users.QueryAsync(q => q.Where(u => askerIds.Contains(u.UserId)).Select(u => new { u.UserId, u.Name }), tracking: false))
            .ToDictionary(u => u.UserId, u => u.Name);
        var names = (await teams.QueryAsync(q => q.Where(t => t.TournamentId == tournament.Id).Select(t => t.Name), tracking: false))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in waiting)
        {
            if (!clubRows.TryGetValue(entry.ClubId, out var club)) continue;
            var ticked = await TickedAsync(entry.TournamentEntryId);
            var reasons = await squads.ReasonsAsync(tournament.Id, ticked, null);
            var count = played.GetValueOrDefault(club.ClubId);
            dto.Waiting.Add(new EntryRowDto
            {
                EntryId = entry.TournamentEntryId,
                ClubId = club.ClubId,
                ClubName = club.Name,
                Area = club.Area,
                LogoUrl = pictureLinks.For(club.LogoId),
                TeamName = entry.TeamName,
                RequestedBy = askers.GetValueOrDefault(entry.RequestedByUserId) ?? "Someone",
                RequestedAt = entry.RequestedAt,
                Record = count == 0 ? "First tournament on KickCricket" : Plural(count, "tournament") + " on KickCricket",
                Players = ticked.Select(p => Row(p, club.Sport, true, reasons.GetValueOrDefault(p.ClubPlayerId))).ToList(),
                Problem = names.Contains(entry.TeamName) ? $"A team called {entry.TeamName} is in the tournament now." : null
            });
        }
        return dto;
    }

    // ---------------------------------------------------------------------------------------------------------------------
    // A lasting team's admins
    // ---------------------------------------------------------------------------------------------------------------------

    public async Task<ServiceResult<EntryPreviewDto>> PreviewAsync(EntryPreviewRequest request)
    {
        var (club, clubError) = await ManagedAsync(request.ClubId);
        if (clubError is { } ce) return ce;

        Tournament? tournament;
        string? code = null;
        if (request.EntryId is { } entryId)
        {
            var entry = await entries.FirstOrDefaultAsync(e => e.TournamentEntryId == entryId && e.ClubId == club!.ClubId, tracking: false);
            if (entry == null) return NotFound("That entry isn't there any more.");
            tournament = await tournaments.FirstOrDefaultAsync(t => t.Id == entry.TournamentId, tracking: false);
            if (tournament == null) return NotFound("That tournament doesn't exist any more.");
        }
        else
        {
            var (found, error) = await FindByCodeAsync(request.Code, club!);
            if (error is { } e) return e;
            tournament = found;
            code = tournament!.EntryCode;
        }
        return Success(await PreviewOfAsync(tournament!, club!, code));
    }

    public async Task<ServiceResult<EntryPreviewDto>> RequestAsync(EntryRequest request)
    {
        var (club, clubError) = await ManagedAsync(request.ClubId);
        if (clubError is { } ce) return ce;
        var (tournament, error) = await FindByCodeAsync(request.Code, club!);
        if (error is { } e) return e;

        var open = await entries.FirstOrDefaultAsync(x => x.TournamentId == tournament!.Id && x.ClubId == club!.ClubId
                                                          && (x.Status == EntryStatus.Waiting || x.Status == EntryStatus.Accepted), tracking: false);
        if (open != null || await teams.AnyAsync(t => t.TournamentId == tournament!.Id && t.ClubId == club!.ClubId))
            return Conflict(open?.Status == EntryStatus.Waiting
                ? $"{club!.Name}'s entry is waiting for the organiser already. You can change its squad until they answer."
                : $"{club!.Name} is in {tournament!.Name} already.");

        var (name, ticks, problem) = await CheckAsync(tournament!, club!, request.TeamName, request.ClubPlayerIds, null);
        if (problem is { } p) return p;

        var entry = new TournamentEntry
        {
            TournamentId = tournament!.Id,
            ClubId = club!.ClubId,
            TeamName = name!,
            Status = EntryStatus.Waiting,
            RequestedByUserId = Me,
            RequestedAt = Now
        };
        foreach (var id in ticks!) entry.Players.Add(new TournamentEntryPlayer { ClubPlayerId = id });
        entries.Add(entry);
        foreach (var owner in await notifier.OwnersOfAsync(tournament.Id))
            await notifier.NotifyAsync(owner, NotificationTemplateCode.EntryRequested,
                Notifier.With(("ActorName", ActorName), ("TeamName", name!), ("TournamentName", tournament.Name)),
                new NotificationLink(tournament.Id));
        try
        {
            await unitOfWork.SaveChangesAsync();
        }
        catch (DuplicateEntryException)
        {
            return Conflict($"{club.Name}'s entry was sent a moment ago. Reload the team.");
        }
        return Success(await PreviewOfAsync(tournament, club, null));
    }

    public async Task<ServiceResult<EntryPreviewDto>> UpdateAsync(int entryId, EntryUpdateRequest request)
    {
        var entry = await entries.FirstOrDefaultAsync(e => e.TournamentEntryId == entryId, EntryQueries.WithPlayers);
        if (entry == null) return NotFound("That entry isn't there any more.");
        var (club, clubError) = await ManagedAsync(entry.ClubId);
        if (clubError is { } ce) return ce;
        if (entry.Status != EntryStatus.Waiting) return Conflict(entry.Status == EntryStatus.Accepted
            ? "The organiser has accepted it, so change the squad on the tournament's team page."
            : "That entry has been answered already.");
        var tournament = (await tournaments.FirstOrDefaultAsync(t => t.Id == entry.TournamentId, tracking: false))!;

        var (name, ticks, problem) = await CheckAsync(tournament, club!, request.TeamName, request.ClubPlayerIds, entry.TournamentEntryId);
        if (problem is { } p) return p;

        entry.TeamName = name!;
        foreach (var tick in entry.Players.Where(t => !ticks!.Contains(t.ClubPlayerId)).ToList()) entryPlayers.Remove(tick);
        foreach (var id in ticks!.Where(id => entry.Players.All(t => t.ClubPlayerId != id)))
            entryPlayers.Add(new TournamentEntryPlayer { TournamentEntryId = entry.TournamentEntryId, ClubPlayerId = id });
        await unitOfWork.SaveChangesAsync();
        return Success(await PreviewOfAsync(tournament, club!, null));
    }

    public async Task<ServiceResult<bool>> WithdrawAsync(int entryId)
    {
        var entry = await entries.FirstOrDefaultAsync(e => e.TournamentEntryId == entryId);
        if (entry == null) return NotFound("That entry isn't there any more.");
        var (_, clubError) = await ManagedAsync(entry.ClubId);
        if (clubError is { } ce) return ce;
        if (entry.Status != EntryStatus.Waiting) return Conflict("That entry has been answered already.");

        entry.Status = EntryStatus.Withdrawn;
        entry.DecidedByUserId = Me;
        entry.DecidedAt = Now;
        await unitOfWork.SaveChangesAsync();
        return Success(true);
    }

    /// <summary>The team's name here and the ticked players, checked: a name free in the tournament, everyone able to play here.</summary>
    private async Task<(string? Name, List<int>? Ticks, ServiceError? Problem)> CheckAsync(Tournament tournament, Club club, string? teamName, List<int>? ticked, int? entryId)
    {
        var name = Squash(teamName);
        if (name.Length == 0) name = club.Name;
        if (name.Length > TournamentEntry.TeamNameMax) return (null, null, BadRequest($"A team's name is at most {TournamentEntry.TeamNameMax} characters."));
        if (await teams.AnyAsync(t => t.TournamentId == tournament.Id && t.Name == name)
            || await entries.AnyAsync(e => e.TournamentId == tournament.Id && e.Status == EntryStatus.Waiting && e.TeamName == name && e.TournamentEntryId != entryId))
            return (null, null, Conflict($"A team called {name} is in {tournament.Name} already. Give yours another name for this tournament."));

        var ids = (ticked ?? []).Distinct().ToList();
        if (ids.Count == 0) return (null, null, BadRequest("Pick at least one player for the squad."));
        var people = await clubPlayers.ListAsync(p => p.ClubId == club.ClubId && ids.Contains(p.ClubPlayerId), tracking: false);
        if (people.Count != ids.Count) return (null, null, Conflict("A player you picked has left the team. Reload and pick again."));
        var reasons = await squads.ReasonsAsync(tournament.Id, people, null);
        if (people.FirstOrDefault(p => reasons.ContainsKey(p.ClubPlayerId)) is { } taken)
            return (null, null, Conflict($"{taken.Name} can't play for you here: {reasons[taken.ClubPlayerId].ToLowerInvariant()}. Untick them."));
        return (name, ids, null);
    }

    private async Task<EntryPreviewDto> PreviewOfAsync(Tournament tournament, Club club, string? code)
    {
        var entry = await entries.FirstOrDefaultAsync(e => e.TournamentId == tournament.Id && e.ClubId == club.ClubId
                                                           && (e.Status == EntryStatus.Waiting || e.Status == EntryStatus.Accepted),
            EntryQueries.WithPlayers, tracking: false);
        var people = await clubPlayers.ListAsync(p => p.ClubId == club.ClubId, tracking: false);
        var reasons = await squads.ReasonsAsync(tournament.Id, people, entry?.TeamId);
        var teamNames = await teams.QueryAsync(q => q.Where(t => t.TournamentId == tournament.Id).OrderBy(t => t.Name).Select(t => t.Name), tracking: false);
        var organiser = await users.QueryFirstOrDefaultAsync(q => q.Where(u => u.UserId == tournament.CreatedByUserId).Select(u => u.Name), tracking: false);
        var name = entry?.TeamName ?? club.Name;

        return new EntryPreviewDto
        {
            TournamentId = tournament.Id,
            TournamentName = tournament.Name,
            Sport = tournament.Sport,
            Status = tournament.Status,
            StartDate = tournament.StartDate,
            EndDate = tournament.EndDate,
            LogoUrl = pictureLinks.For(tournament.LogoId),
            Organiser = string.IsNullOrWhiteSpace(organiser) ? "The organiser" : organiser,
            Teams = teamNames,
            ClubId = club.ClubId,
            ClubName = club.Name,
            TeamName = name,
            TeamNameProblem = entry?.Status != EntryStatus.Accepted && teamNames.Contains(name, StringComparer.OrdinalIgnoreCase)
                ? $"A team called {name} is in {tournament.Name} already. Give yours another name for this tournament."
                : null,
            Players = people
                .OrderByDescending(p => p.IsCaptain).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(p =>
                {
                    var reason = reasons.GetValueOrDefault(p.ClubPlayerId);
                    var ticked = entry == null ? reason == null : entry.Players.Any(t => t.ClubPlayerId == p.ClubPlayerId);
                    return Row(p, club.Sport, ticked, entry?.Status == EntryStatus.Accepted ? null : reason);
                }).ToList(),
            EntryId = entry?.TournamentEntryId,
            EntryStatus = entry?.Status,
            Code = code
        };
    }

    private static EntryPlayerDto Row(ClubPlayer p, Sport sport, bool ticked, string? reason) => new()
    {
        ClubPlayerId = p.ClubPlayerId,
        Name = p.Name,
        Detail = Detail(p, sport),
        IsCaptain = p.IsCaptain,
        Ticked = ticked && reason == null,
        Reason = reason
    };

    /// <summary>The tournament the code opens, for this club: entries open, the same sport. Wrong codes count, as join codes do.</summary>
    private async Task<(Tournament? Tournament, ServiceError? Error)> FindByCodeAsync(string? typed, Club club)
    {
        if (attempts.WaitFor(Me) is { } wait)
            return (null, TooManyRequests($"Too many wrong codes. Try again in {Math.Max(1, (int)Math.Ceiling(wait.TotalMinutes))} minutes."));

        // The whole shared link works too: its last part is the code.
        var last = (typed ?? "").Trim().TrimEnd('/').Split('/').LastOrDefault();
        var code = JoinCodes.Normalise(last, JoinCodes.EntryLength);
        if (code == null) return (null, BadRequest($"A tournament's entry code is {JoinCodes.EntryLength} letters and numbers. Ask the organiser for it."));

        var tournament = await tournaments.FirstOrDefaultAsync(t => t.EntryCode == code, tracking: false);
        if (tournament == null)
        {
            attempts.Missed(Me);
            return (null, NotFound("No tournament has that entry code. Check it with the organiser."));
        }
        if (!tournament.EntriesOpen || WhyClosed(tournament) != null)
            return (null, Conflict($"Entries to {tournament.Name} are closed. Ask the organiser if there's still room."));
        if (tournament.Sport != club.Sport)
            return (null, Conflict($"{tournament.Name} is a {SportWord(tournament.Sport)} tournament, and {club.Name} plays {SportWord(club.Sport)}."));
        return (tournament, null);
    }

    private async Task<(Club? Club, ServiceError? Error)> ManagedAsync(int clubId)
    {
        var club = await clubs.FirstOrDefaultAsync(c => c.ClubId == clubId && !c.IsDisabled, tracking: false);
        if (club == null) return (null, NotFound("That team doesn't exist any more."));
        if (!await clubAdmins.AnyAsync(a => a.ClubId == clubId && a.UserId == Me && !a.IsDisabled))
            return (null, Forbidden("Only this team's admins can enter it into tournaments."));
        return (club, null);
    }

    // ---------------------------------------------------------------------------------------------------------------------
    // Entries closing
    // ---------------------------------------------------------------------------------------------------------------------

    public async Task CloseAsync(int tournamentId)
    {
        var tournament = await tournaments.FirstOrDefaultAsync(t => t.Id == tournamentId);
        if (tournament == null) return;
        tournament.EntriesOpen = false;

        foreach (var entry in await entries.ListAsync(e => e.TournamentId == tournamentId && e.Status == EntryStatus.Waiting))
        {
            entry.Status = EntryStatus.Cancelled;
            entry.DecidedAt = Now;
            foreach (var admin in await AdminsOfAsync(entry.ClubId))
                await notifier.NotifyAsync(admin, NotificationTemplateCode.EntryCancelled,
                    Notifier.With(("TeamName", entry.TeamName), ("TournamentName", tournament.Name)), NotificationLink.ToClub(entry.ClubId));
        }
    }

    // ---------------------------------------------------------------------------------------------------------------------

    private async Task<List<ClubPlayer>> TickedAsync(int entryId)
    {
        var ids = await entryPlayers.QueryAsync(q => q.Where(t => t.TournamentEntryId == entryId).Select(t => t.ClubPlayerId), tracking: false);
        return (await clubPlayers.ListAsync(p => ids.Contains(p.ClubPlayerId), tracking: false))
            .OrderByDescending(p => p.IsCaptain).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private Task<List<int>> AdminsOfAsync(int clubId) =>
        clubAdmins.QueryAsync(q => q.Where(a => a.ClubId == clubId && !a.IsDisabled && !a.User!.IsDisabled && a.User.DeletedOn == null).Select(a => a.UserId), tracking: false);

    private async Task<string> NewEntryCodeAsync()
    {
        string code;
        do code = JoinCodes.New(JoinCodes.EntryLength);
        while (await tournaments.AnyAsync(t => t.EntryCode == code));
        return code;
    }

    private async Task<string> NewTeamCodeAsync()
    {
        string code;
        do code = JoinCodes.New();
        while (await teams.AnyAsync(t => t.JoinCode == code));
        return code;
    }

    /// <summary>"#7 · Batter": what tells two players of the same name apart.</summary>
    internal static string? Detail(ClubPlayer place, Sport sport)
    {
        var parts = new List<string>();
        if (place.JerseyNumber is { } number) parts.Add($"#{number}");
        var what = sport == Sport.Cricket ? place.Cricket?.RoleLabel : place.Position;
        if (!string.IsNullOrWhiteSpace(what)) parts.Add(what.Trim());
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    private static string SportWord(Sport sport) => sport == Sport.Cricket ? "cricket" : "football";
    private static string Plural(int n, string word) => $"{n} {word}{(n == 1 ? "" : "s")}";
    private static string Squash(string? text) => string.Join(' ', (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string Names(List<string> names) => names.Count switch
    {
        1 => names[0],
        2 => $"{names[0]} and {names[1]}",
        _ => string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1]
    };
}
