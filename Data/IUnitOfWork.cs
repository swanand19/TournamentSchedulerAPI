namespace TournamentScheduler.Api.Data;

/// <summary>
/// Commits everything the data services staged during one request, in one transaction. Every data
/// service in a request shares the same underlying context, so one save covers all of them.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Writes every staged change. Throws <see cref="DuplicateEntryException"/> when a unique index
    /// rejects the write (two requests racing to create the same team name, say).
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens an explicit transaction, for work that must commit or fail as one across several saves
    /// or stored procedure calls — both join it automatically. Nothing is kept unless
    /// <see cref="IUnitOfWorkTransaction.CommitAsync"/> is called; disposing without it rolls back.
    /// <code>
    /// await using var tx = await _unitOfWork.BeginTransactionAsync();
    /// await _procedures.ExecuteAsync(new ArchiveSeason { TournamentId = id });
    /// await _unitOfWork.SaveChangesAsync();
    /// await tx.CommitAsync();
    /// </code>
    /// </summary>
    Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>Whether the database answers at all — for the health check.</summary>
    Task<bool> CanConnectAsync();
}

public interface IUnitOfWorkTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);

    Task RollbackAsync(CancellationToken cancellationToken = default);
}

/// <summary>A write refused by a unique index. Raised by the data layer so services never see SQL errors.</summary>
public sealed class DuplicateEntryException(Exception inner)
    : Exception("A row with the same unique value already exists.", inner);
