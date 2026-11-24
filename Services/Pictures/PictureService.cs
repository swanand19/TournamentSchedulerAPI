using Microsoft.Extensions.Options;
using TournamentScheduler.Api.Auth;
using TournamentScheduler.Api.Data;
using TournamentScheduler.Api.Data.DataServices;
using TournamentScheduler.Api.Models;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Pictures;

namespace TournamentScheduler.Api.Services.Pictures;

/// <summary>A picture as the apps send it: base64 (a "data:image/…;base64," prefix is fine), already shrunk on the device.</summary>
public class SetPictureRequest
{
    public string Image { get; set; } = string.Empty;
}

/// <summary>The picture's new link (signed, expiring), or null once it's removed.</summary>
public class PictureDto
{
    public string? Url { get; set; }
}

public interface IPictureService
{
    Task<ServiceResult<PictureDto>> SetMyPhotoAsync(SetPictureRequest request);
    Task<ServiceResult<PictureDto>> RemoveMyPhotoAsync();
    Task<ServiceResult<PictureDto>> SetTournamentPictureAsync(int tournamentId, string kind, SetPictureRequest request);
    Task<ServiceResult<PictureDto>> RemoveTournamentPictureAsync(int tournamentId, string kind);

    /// <summary>A lasting team's logo (2026-11-20): its admins only.</summary>
    Task<ServiceResult<PictureDto>> SetClubLogoAsync(int clubId, SetPictureRequest request);
    Task<ServiceResult<PictureDto>> RemoveClubLogoAsync(int clubId);

    /// <summary>A league's logo (2026-11-24): its organisers only.</summary>
    Task<ServiceResult<PictureDto>> SetLeagueLogoAsync(int leagueId, SetPictureRequest request);
    Task<ServiceResult<PictureDto>> RemoveLeagueLogoAsync(int leagueId);
}

/// <summary>
/// Profile photos (each person their own) and a tournament's logo and banner (its owners), HLD P-26. Uploads
/// come through the gateway like everything else; the file is checked from its own header (JPEG or PNG, sane
/// size), kept under a new random name, and the old one deleted once the change is saved.
/// </summary>
public class PictureService : ServiceBase, IPictureService
{
    private readonly IDataService<User> _users;
    private readonly IDataService<Tournament> _tournaments;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly IPictureStore _store;
    private readonly PictureLinks _links;
    private readonly PictureOptions _options;

    private readonly IDataService<Models.Clubs.Club> _clubs;
    private readonly IDataService<Models.Clubs.ClubAdmin> _clubAdmins;
    private readonly IDataService<Models.Leagues.League> _leagues;
    private readonly IDataService<Models.Leagues.LeagueOrganiser> _leagueOrganisers;

    public PictureService(IDataService<User> users, IDataService<Tournament> tournaments, IUnitOfWork unitOfWork,
        ICurrentUser currentUser, IPictureStore store, PictureLinks links, IOptions<PictureOptions> options,
        IDataService<Models.Clubs.Club> clubs, IDataService<Models.Clubs.ClubAdmin> clubAdmins,
        IDataService<Models.Leagues.League> leagues, IDataService<Models.Leagues.LeagueOrganiser> leagueOrganisers)
    {
        _leagues = leagues;
        _leagueOrganisers = leagueOrganisers;
        _clubs = clubs;
        _clubAdmins = clubAdmins;
        _users = users;
        _tournaments = tournaments;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _store = store;
        _links = links;
        _options = options.Value;
    }

    public async Task<ServiceResult<PictureDto>> SetMyPhotoAsync(SetPictureRequest request)
    {
        if (await _users.GetByIdAsync(_currentUser.UserId) is not { } user) return NotFound("Account not found.");
        return await Replace(request, () => user.PhotoId, id => user.PhotoId = id);
    }

    public async Task<ServiceResult<PictureDto>> RemoveMyPhotoAsync()
    {
        if (await _users.GetByIdAsync(_currentUser.UserId) is not { } user) return NotFound("Account not found.");
        return await Remove(() => user.PhotoId, id => user.PhotoId = id);
    }

    public async Task<ServiceResult<PictureDto>> SetTournamentPictureAsync(int tournamentId, string kind, SetPictureRequest request)
    {
        if (await _tournaments.GetByIdAsync(tournamentId) is not { } tournament) return NotFound("Tournament not found.");
        return Slot(tournament, kind) is { } slot ? await Replace(request, slot.Get, slot.Set) : BadRequest(UnknownKind);
    }

    public async Task<ServiceResult<PictureDto>> RemoveTournamentPictureAsync(int tournamentId, string kind)
    {
        if (await _tournaments.GetByIdAsync(tournamentId) is not { } tournament) return NotFound("Tournament not found.");
        return Slot(tournament, kind) is { } slot ? await Remove(slot.Get, slot.Set) : BadRequest(UnknownKind);
    }

    public async Task<ServiceResult<PictureDto>> SetClubLogoAsync(int clubId, SetPictureRequest request)
    {
        var (club, error) = await ClubIRunAsync(clubId);
        return error ?? await Replace(request, () => club!.LogoId, id => club!.LogoId = id);
    }

    public async Task<ServiceResult<PictureDto>> RemoveClubLogoAsync(int clubId)
    {
        var (club, error) = await ClubIRunAsync(clubId);
        return error ?? await Remove(() => club!.LogoId, id => club!.LogoId = id);
    }

    private async Task<(Models.Clubs.Club? Club, ServiceResult<PictureDto>? Error)> ClubIRunAsync(int clubId)
    {
        var club = await _clubs.FirstOrDefaultAsync(c => c.ClubId == clubId && !c.IsDisabled);
        if (club == null) return (null, NotFound("That team doesn't exist any more."));
        var me = _currentUser.UserId;
        if (!await _clubAdmins.AnyAsync(a => a.ClubId == clubId && a.UserId == me && !a.IsDisabled))
            return (null, Forbidden("Only this team's admins can do that."));
        return (club, null);
    }

    public async Task<ServiceResult<PictureDto>> SetLeagueLogoAsync(int leagueId, SetPictureRequest request)
    {
        var (league, error) = await LeagueIRunAsync(leagueId);
        return error ?? await Replace(request, () => league!.LogoId, id => league!.LogoId = id);
    }

    public async Task<ServiceResult<PictureDto>> RemoveLeagueLogoAsync(int leagueId)
    {
        var (league, error) = await LeagueIRunAsync(leagueId);
        return error ?? await Remove(() => league!.LogoId, id => league!.LogoId = id);
    }

    private async Task<(Models.Leagues.League? League, ServiceResult<PictureDto>? Error)> LeagueIRunAsync(int leagueId)
    {
        var league = await _leagues.FirstOrDefaultAsync(l => l.LeagueId == leagueId && !l.IsDisabled);
        if (league == null) return (null, NotFound("That league doesn't exist any more."));
        var me = _currentUser.UserId;
        if (!await _leagueOrganisers.AnyAsync(o => o.LeagueId == leagueId && o.UserId == me && !o.IsDisabled))
            return (null, Forbidden("Only this league's organisers can do that."));
        return (league, null);
    }

    private const string UnknownKind = "A tournament has a logo and a banner; choose one of them.";

    private static (Func<string?> Get, Action<string?> Set)? Slot(Tournament tournament, string kind) => kind.ToLowerInvariant() switch
    {
        "logo" => (() => tournament.LogoId, id => tournament.LogoId = id),
        "banner" => (() => tournament.BannerId, id => tournament.BannerId = id),
        _ => null
    };

    private async Task<ServiceResult<PictureDto>> Replace(SetPictureRequest request, Func<string?> current, Action<string?> set)
    {
        if (Decode(request.Image) is not { } bytes) return BadRequest("That file isn't a picture we can use. Choose a JPEG or PNG photo.");
        if (bytes.Length >= _options.MaxBytes)
            return BadRequest($"That picture is too big. Choose a smaller one (under {_options.MaxBytes / 1000} KB).");
        if (PictureFile.Read(bytes) is not { } info) return BadRequest("That file isn't a picture we can use. Choose a JPEG or PNG photo.");
        if (Math.Min(info.Width, info.Height) < PictureFile.MinSide)
            return BadRequest($"That picture is too small. Choose one at least {PictureFile.MinSide} pixels across.");
        if (Math.Max(info.Width, info.Height) > PictureFile.MaxSide)
            return BadRequest($"That picture is too large. Choose one under {PictureFile.MaxSide} pixels across.");

        var old = current();
        var id = await _store.SaveAsync(bytes, info.Extension);
        set(id);
        try
        {
            await _unitOfWork.SaveChangesAsync();
        }
        catch
        {
            _store.Delete(id);
            throw;
        }
        _store.Delete(old);
        return Success(new PictureDto { Url = _links.For(id) });
    }

    private async Task<ServiceResult<PictureDto>> Remove(Func<string?> current, Action<string?> set)
    {
        var old = current();
        if (old == null) return Success(new PictureDto());
        set(null);
        await _unitOfWork.SaveChangesAsync();
        _store.Delete(old);
        return Success(new PictureDto());
    }

    private static byte[]? Decode(string? image)
    {
        if (string.IsNullOrWhiteSpace(image)) return null;
        var text = image.Trim();
        if (text.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && text.IndexOf(',') is > 0 and var comma) text = text[(comma + 1)..];
        var buffer = new byte[text.Length * 3 / 4 + 3];
        return Convert.TryFromBase64String(text, buffer, out var written) ? buffer[..written] : null;
    }
}
