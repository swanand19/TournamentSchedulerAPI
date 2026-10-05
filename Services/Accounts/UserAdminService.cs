using TournamentScheduler.Api.Data;
using TournamentScheduler.Api.Data.DataServices;
using TournamentScheduler.Api.Models.Accounts;

namespace TournamentScheduler.Api.Services.Accounts;

public interface IUserAdminService
{
    /// <summary>Makes the account with this email an Admin, creating it first if there isn't one.</summary>
    Task<ServiceResult<string>> MakeAdminAsync(string email, string? name);
}

/// <summary>
/// What the admin portal will do for users, available before it exists. Today only the first admin,
/// from the command line (<c>dotnet run -- users create-admin</c>): nobody can sign up as one.
/// </summary>
public class UserAdminService : ServiceBase, IUserAdminService
{
    private readonly IDataService<User> _users;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;

    public UserAdminService(IDataService<User> users, IUnitOfWork unitOfWork, TimeProvider clock)
    {
        _users = users;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<ServiceResult<string>> MakeAdminAsync(string email, string? name)
    {
        var normalised = AccountService.NormaliseEmail(email);
        if (normalised == null) return BadRequest($"'{email}' isn't a valid email address.");

        var now = _clock.GetUtcNow().UtcDateTime;
        var user = await _users.FirstOrDefaultAsync(u => u.EmailId == normalised);

        if (user == null)
        {
            var trimmed = name?.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.Length < AccountService.NameMinLength || trimmed.Length > AccountService.NameMaxLength)
                return BadRequest($"There is no account for {normalised} yet, so give a name ({AccountService.NameMinLength}–{AccountService.NameMaxLength} characters) to create one.");

            user = new User { Name = trimmed, EmailId = normalised, RoleId = RoleIds.Admin };
            user.Created(now, SystemUser.Id);
            _users.Add(user);
            await _unitOfWork.SaveChangesAsync();
            return Success($"Created admin account {user.UserId} for {normalised}. It signs in with a code sent to that email.");
        }

        if (user.RoleId == RoleIds.Admin) return Success($"{normalised} (account {user.UserId}) is already an admin.");

        user.RoleId = RoleIds.Admin;
        user.Modified(now, SystemUser.Id);
        await _unitOfWork.SaveChangesAsync();
        return Success($"{normalised} (account {user.UserId}) is now an admin.");
    }
}
