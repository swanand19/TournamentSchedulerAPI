using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TournamentScheduler.Api.Models.Notifications;
using TournamentScheduler.Api.Realtime;

namespace TournamentScheduler.Api.Data;

public class UnitOfWork : IUnitOfWork
{
    private readonly TournamentDbContext _db;
    private readonly ILiveUpdates? _live;

    public UnitOfWork(TournamentDbContext db, ILiveUpdates? live = null)
    {
        _db = db;
        _live = live;
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Every inbox change — a new notification from any service or job, or one read on another
        // device — is committed here, so this is the one place that nudges the bells.
        var inboxes = _live == null ? [] : _db.ChangeTracker.Entries<Notification>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified && e.Entity.InInbox)
            .Select(e => e.Entity.UserId)
            .Distinct()
            .ToList();

        int saved;
        try
        {
            saved = await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new DuplicateEntryException(ex);
        }

        if (inboxes.Count > 0) await _live!.InboxChangedAsync(inboxes);
        return saved;
    }

    public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_db.Database.CurrentTransaction != null)
            throw new InvalidOperationException("A transaction is already open for this request; commit or dispose it first.");

        return new Transaction(await _db.Database.BeginTransactionAsync(cancellationToken));
    }

    public Task<bool> CanConnectAsync() => _db.Database.CanConnectAsync();

    private sealed class Transaction(IDbContextTransaction inner) : IUnitOfWorkTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken = default) => inner.CommitAsync(cancellationToken);

        public Task RollbackAsync(CancellationToken cancellationToken = default) => inner.RollbackAsync(cancellationToken);

        // EF rolls back an uncommitted transaction when it is disposed.
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
