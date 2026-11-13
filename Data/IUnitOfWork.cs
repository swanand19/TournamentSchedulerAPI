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
    /// Runs <paramref name="work"/> in one transaction, for work that must commit or fail as one across
    /// several saves or stored procedure calls — both join it automatically. It commits when the work
    /// returns and rolls back when it throws. If the database drops mid-way, the whole block is run again
    /// from the start (see <see cref="DatabaseOptions"/>), so the work must be safe to repeat: read what it
    /// needs inside the block, not before.
    /// <code>
    /// await _unitOfWork.ExecuteInTransactionAsync(async ct =>
    /// {
    ///     await _procedures.ExecuteAsync(new ArchiveSeason { TournamentId = id });
    ///     await _unitOfWork.SaveChangesAsync(ct);
    /// });
    /// </code>
    /// </summary>
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens an explicit transaction by hand. Only usable when retries are off (<c>Database:RetryOnFailure</c>
    /// false, as in the SQL Server tests) — EF refuses a hand-opened transaction under a retrying connection,
    /// because it couldn't replay it. Prefer <see cref="ExecuteInTransactionAsync"/>.
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
