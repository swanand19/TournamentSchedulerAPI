using TournamentScheduler.Api.Data;
using TournamentScheduler.Api.Data.DataServices;
using TournamentScheduler.Api.Gateway;
using TournamentScheduler.Api.Models.Accounts;
using ServiceDescriptor = TournamentScheduler.Api.Gateway.ServiceDescriptor;

namespace TournamentScheduler.Api.Services.Accounts;

/// <summary>What one sync changed — logged at start-up, and shown by the portal's "Update permissions" later.</summary>
public sealed record PermissionSyncResult(int Added, int Refreshed, int Retired, int Returned)
{
    public override string ToString() => $"{Added} added, {Refreshed} refreshed, {Retired} retired, {Returned} back";
}

/// <summary>One service as the request check needs it: the admin's switch, the sync's, and who is ticked.</summary>
public sealed record PermissionEntry(bool IsDisabled, bool IsObsolete, IReadOnlySet<int> RoleIds);

/// <summary>Every service's entry, by service id. Loaded whole and cached; the table is small.</summary>
public sealed record PermissionSnapshot(IReadOnlyDictionary<string, PermissionEntry> Services);

public interface IPermissionService
{
    /// <summary>Brings the Permissions table in step with the code. See <see cref="PermissionService"/>.</summary>
    Task<PermissionSyncResult> SyncAsync(IReadOnlyCollection<ServiceDescriptor> services);

    Task<PermissionSnapshot> LoadSnapshotAsync();
}

/// <summary>
/// The Permissions table follows the code; the ticks (RolePermissions) belong to the admins.
///
/// The sync, for each service in the code:
/// <list type="bullet">
/// <item><b>new</b> — adds its row, and ticks Admin plus the code's <c>[DefaultRoles]</c>. The only
/// time code ever sets a tick: a later deploy can't undo an admin's decision.</item>
/// <item><b>still there</b> — refreshes what the portal shows (controller, action, URL, flags);
/// leaves the ticks and the admin's <c>IsDisabled</c> alone.</item>
/// <item><b>gone</b> — marks it obsolete, keeping its ticks; <b>back again</b> — clears that, and the
/// old ticks apply again.</item>
/// </list>
/// </summary>
public class PermissionService : ServiceBase, IPermissionService
{
    private readonly IDataService<Permission> _permissions;
    private readonly IDataService<RolePermission> _rolePermissions;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;

    public PermissionService(IDataService<Permission> permissions, IDataService<RolePermission> rolePermissions, IUnitOfWork unitOfWork, TimeProvider clock)
    {
        _permissions = permissions;
        _rolePermissions = rolePermissions;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<PermissionSyncResult> SyncAsync(IReadOnlyCollection<ServiceDescriptor> services)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var existing = (await _permissions.ListAsync()).ToDictionary(p => p.ServiceRequestId, StringComparer.Ordinal);
        var added = new List<(Permission Row, ServiceDescriptor Service)>();
        int refreshed = 0, retired = 0, returned = 0;

        foreach (var service in services)
        {
            if (!existing.TryGetValue(service.Id, out var row))
            {
                row = new Permission { ServiceRequestId = service.Id };
                Describe(row, service);
                row.Created(now, SystemUser.Id);
                _permissions.Add(row);
                added.Add((row, service));
                continue;
            }

            var changed = Describe(row, service);
            if (row.IsObsolete)
            {
                row.IsObsolete = false;
                returned++;
                changed = true;
            }
            else if (changed)
            {
                refreshed++;
            }
            if (changed) row.Modified(now, SystemUser.Id);
        }

        var inCode = services.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var row in existing.Values.Where(p => !inCode.Contains(p.ServiceRequestId) && !p.IsObsolete))
        {
            row.IsObsolete = true;
            row.Modified(now, SystemUser.Id);
            retired++;
        }

        await _unitOfWork.SaveChangesAsync(); // gives the new rows their ids

        foreach (var (row, service) in added)
        {
            foreach (var roleId in FirstTicks(service))
            {
                var tick = new RolePermission { RoleId = roleId, PermissionId = row.PermissionId };
                tick.Created(now, SystemUser.Id);
                _rolePermissions.Add(tick);
            }
        }
        if (added.Count > 0) await _unitOfWork.SaveChangesAsync();

        return new PermissionSyncResult(added.Count, refreshed, retired, returned);
    }

    /// <summary>Admin always; the code's defaults otherwise — never EndUser for an admin-only service.</summary>
    private static IEnumerable<int> FirstTicks(ServiceDescriptor service) =>
        service.DefaultRoleIds
            .Append(RoleIds.Admin)
            .Where(roleId => !(service.IsAdminOnly && roleId == RoleIds.EndUser))
            .Distinct();

    /// <summary>Copies what the code says onto the row; true when anything changed.</summary>
    private static bool Describe(Permission row, ServiceDescriptor service)
    {
        var url = service.RouteTemplate.Length > 300 ? service.RouteTemplate[..300] : service.RouteTemplate;
        var changed = row.ControllerName != service.ControllerName
                      || row.ActionName != service.MethodName
                      || row.HttpMethod != service.HttpMethod
                      || row.URL != url
                      || row.IsAnonymous != service.IsAnonymous
                      || row.IsAdminOnly != service.IsAdminOnly
                      || row.IsProtected != service.IsProtected;

        row.ControllerName = service.ControllerName;
        row.ActionName = service.MethodName;
        row.HttpMethod = service.HttpMethod;
        row.URL = url;
        row.IsAnonymous = service.IsAnonymous;
        row.IsAdminOnly = service.IsAdminOnly;
        row.IsProtected = service.IsProtected;
        return changed;
    }

    public async Task<PermissionSnapshot> LoadSnapshotAsync()
    {
        var permissions = await _permissions.QueryAsync(q => q.Select(p => new { p.PermissionId, p.ServiceRequestId, p.IsDisabled, p.IsObsolete }), tracking: false);
        var ticks = await _rolePermissions.QueryAsync(q => q.Where(rp => !rp.IsDisabled).Select(rp => new { rp.PermissionId, rp.RoleId }), tracking: false);
        var rolesByPermission = ticks.GroupBy(t => t.PermissionId).ToDictionary(g => g.Key, g => (IReadOnlySet<int>)g.Select(t => t.RoleId).ToHashSet());

        var entries = permissions.ToDictionary(
            p => p.ServiceRequestId,
            p => new PermissionEntry(p.IsDisabled, p.IsObsolete, rolesByPermission.GetValueOrDefault(p.PermissionId) ?? new HashSet<int>()),
            StringComparer.Ordinal);
        return new PermissionSnapshot(entries);
    }
}
