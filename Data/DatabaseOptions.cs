namespace TournamentScheduler.Api.Data;

/// <summary>
/// The <c>Database</c> section: how the API rides out a database that blips. Azure SQL drops connections
/// for a few seconds now and then (fail-overs, patching), so a failed command is retried with a growing
/// delay instead of reaching the person as an error. Retrying is why explicit transactions must go
/// through <see cref="IUnitOfWork.ExecuteInTransactionAsync"/>: the whole block is what gets retried.
/// </summary>
public sealed class DatabaseOptions
{
    public const string Section = "Database";

    /// <summary>Retry transient failures (SQL Server's own list of them: timeouts, fail-overs, throttling).</summary>
    public bool RetryOnFailure { get; set; } = true;

    public int MaxRetryCount { get; set; } = 5;
    public int MaxRetryDelaySeconds { get; set; } = 10;

    /// <summary>
    /// How long one command may run. The slowest real ones (stats pages) take well under a second; this only
    /// stops a stuck command from holding a request — and a pooled connection — for ever.
    /// </summary>
    public int CommandTimeoutSeconds { get; set; } = 30;
}
