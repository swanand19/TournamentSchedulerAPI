using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace TournamentScheduler.Api.Logging;

/// <summary>Where log rows end up. The SQL Server one in production; tests swap in their own.</summary>
public interface ILogStore
{
    Task WriteAsync(IReadOnlyList<object> entries, CancellationToken cancellationToken);

    /// <summary>Deletes rows older than <paramref name="cutoffUtc"/>; returns how many.</summary>
    Task<int> PurgeAsync(DateTime cutoffUtc, CancellationToken cancellationToken);
}

/// <summary>The logs database (<c>LogConnection</c>), through <see cref="LogDbContext"/>.</summary>
public sealed class SqlLogStore(IDbContextFactory<LogDbContext> contexts) : ILogStore
{
    private const int PurgeBatch = 5_000;

    // Written out in full rather than built: SQL text never comes from a variable. {0} is the cutoff,
    // sent as a parameter. Small batches keep each delete's transaction — and the log file — small.
    private static readonly string[] PurgeStatements =
    [
        "DELETE TOP (5000) FROM [MBMiddlewareLogs] WHERE [StartDate] < {0}",
        "DELETE TOP (5000) FROM [MBActivityLogs] WHERE [StartDate] < {0}",
        "DELETE TOP (5000) FROM [ErrorLogs] WHERE [Crd] < {0}",
        "DELETE TOP (5000) FROM [RemoteLogs] WHERE [StartDate] < {0}"
    ];

    public async Task WriteAsync(IReadOnlyList<object> entries, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        foreach (var entry in entries) FitToColumns(db, entry);
        db.AddRange(entries);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Header values come from the apps and could be any length; one too long for its column would
    /// make SQL Server refuse the whole batch. Trim each to its column's size, read from the model.
    /// </summary>
    private static void FitToColumns(LogDbContext db, object entry)
    {
        var entity = db.Model.FindEntityType(entry.GetType());
        if (entity == null) return;

        foreach (var property in entity.GetProperties())
        {
            if (property.ClrType != typeof(string) || property.GetMaxLength() is not int max || property.PropertyInfo == null) continue;
            if (property.PropertyInfo.GetValue(entry) is string value && value.Length > max)
                property.PropertyInfo.SetValue(entry, value[..max]);
        }
    }

    public async Task<int> PurgeAsync(DateTime cutoffUtc, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var total = 0;

        foreach (var statement in PurgeStatements)
        {
            int deleted;
            do
            {
                deleted = await db.Database.ExecuteSqlRawAsync(statement, [cutoffUtc], cancellationToken);
                total += deleted;
            } while (deleted == PurgeBatch && !cancellationToken.IsCancellationRequested);
        }

        return total;
    }
}

/// <summary>Used when no <c>LogConnection</c> is configured: everything goes to the fallback file.</summary>
public sealed class UnconfiguredLogStore : ILogStore
{
    public Task WriteAsync(IReadOnlyList<object> entries, CancellationToken cancellationToken) =>
        throw new InvalidOperationException($"No '{LogStoreOptions.ConnectionStringName}' connection string is configured.");

    public Task<int> PurgeAsync(DateTime cutoffUtc, CancellationToken cancellationToken) => Task.FromResult(0);
}

/// <summary>
/// Where rows go when the logs database can't take them (down, full, not created yet): one
/// JSON-lines file per day, purged with the same retention. If even this fails the rows are lost —
/// logging must never be the reason a request fails.
/// </summary>
public sealed class LogFallbackFile(IOptions<LogStoreOptions> options)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string _directory = options.Value.FallbackDirectory;
    private readonly Lock _gate = new();

    public string Directory => _directory;

    public bool TryWrite(IReadOnlyList<object> entries, DateTime nowUtc)
    {
        try
        {
            var lines = entries.Select(e => JsonSerializer.Serialize(new { table = TableOf(e), entry = e }, Json));
            lock (_gate)
            {
                System.IO.Directory.CreateDirectory(_directory);
                // Shared, so someone reading or tailing the file during an outage doesn't block the writer (or vice versa).
                using var stream = new FileStream(Path.Combine(_directory, $"logs-{nowUtc:yyyyMMdd}.jsonl"),
                    FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                using var writer = new StreamWriter(stream);
                foreach (var line in lines) writer.WriteLine(line);
            }
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Purge(DateTime cutoffUtc)
    {
        try
        {
            if (!System.IO.Directory.Exists(_directory)) return;
            foreach (var file in System.IO.Directory.EnumerateFiles(_directory, "logs-*.jsonl"))
                if (File.GetLastWriteTimeUtc(file) < cutoffUtc) File.Delete(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Next hour, then.
        }
    }

    public static string TableOf(object entry) => entry switch
    {
        MBMiddlewareLog => "MBMiddlewareLogs",
        MBActivityLog => "MBActivityLogs",
        ErrorLog => "ErrorLogs",
        RemoteLog => "RemoteLogs",
        _ => entry.GetType().Name
    };
}
