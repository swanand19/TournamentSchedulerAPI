using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace TournamentScheduler.Api.Data;

public class UnitOfWork : IUnitOfWork
{
    private readonly TournamentDbContext _db;

    public UnitOfWork(TournamentDbContext db)
    {
        _db = db;
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new DuplicateEntryException(ex);
        }
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
