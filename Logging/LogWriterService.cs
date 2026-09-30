using Microsoft.Extensions.Options;

namespace TournamentScheduler.Api.Logging;

/// <summary>
/// Empties the <see cref="LogQueue"/> into the logs database in batches — whichever comes first of
/// <c>BatchSize</c> rows or <c>FlushIntervalMs</c> — and purges rows past their retention every
/// <c>PurgeIntervalMinutes</c>.
///
/// When the database refuses a batch (down, full, not created yet), the batch goes to the fallback
/// file and the database is left alone for <c>RetryAfterSeconds</c>, so an outage costs one failed
/// attempt every half-minute rather than one per batch. On shutdown what is still queued is written
/// before the process exits.
///
/// Its own problems are logged as warnings, never errors: an error would be queued as an ErrorLogs
/// row and could feed itself.
/// </summary>
public sealed class LogWriterService(
    LogQueue queue,
    ILogStore store,
    LogFallbackFile fallback,
    IOptions<LogStoreOptions> options,
    TimeProvider clock,
    ILogger<LogWriterService> logger) : BackgroundService
{
    private readonly LogStoreOptions _options = options.Value;
    private DateTimeOffset _databaseRestsUntil = DateTimeOffset.MinValue;
    private DateTimeOffset _nextPurge = DateTimeOffset.MinValue;
    private long _droppedReported;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!queue.Enabled) return;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // Purge first, so a quiet server still purges at start-up and every interval after.
                await PurgeIfDue(stoppingToken);
                var batch = await NextBatch(stoppingToken);
                if (batch.Count > 0) await Write(batch);
                ReportDrops();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down: StopAsync drains the rest.
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        if (!queue.Enabled) return;

        // Whatever arrived before shutdown still gets written, within the host's shutdown time.
        var rest = new List<object>();
        while (queue.Reader.TryRead(out var entry)) rest.Add(entry);
        for (var i = 0; i < rest.Count; i += _options.BatchSize)
            await Write(rest.GetRange(i, Math.Min(_options.BatchSize, rest.Count - i)));
    }

    /// <summary>Waits for a first row, then takes whatever else arrives within the flush interval.</summary>
    private async Task<List<object>> NextBatch(CancellationToken stoppingToken)
    {
        var batch = new List<object>(_options.BatchSize);

        using var purgeWake = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        purgeWake.CancelAfter(TimeSpan.FromMinutes(Math.Max(1, _options.PurgeIntervalMinutes)));
        try
        {
            if (!await queue.Reader.WaitToReadAsync(purgeWake.Token)) return batch;
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            return batch; // quiet for a whole purge interval: go and purge
        }

        var deadline = clock.GetUtcNow().AddMilliseconds(_options.FlushIntervalMs);
        while (batch.Count < _options.BatchSize)
        {
            while (batch.Count < _options.BatchSize && queue.Reader.TryRead(out var entry)) batch.Add(entry);
            var left = deadline - clock.GetUtcNow();
            if (batch.Count >= _options.BatchSize || left <= TimeSpan.Zero) break;

            using var wait = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            wait.CancelAfter(left);
            try
            {
                if (!await queue.Reader.WaitToReadAsync(wait.Token)) break;
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        return batch;
    }

    private async Task Write(List<object> batch)
    {
        var now = clock.GetUtcNow();
        if (now >= _databaseRestsUntil)
        {
            try
            {
                // Its own timeout, not the stopping token: shutdown is exactly when a batch must finish.
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await store.WriteAsync(batch, timeout.Token);
                return;
            }
            catch (Exception e)
            {
                _databaseRestsUntil = now.AddSeconds(_options.RetryAfterSeconds);
                logger.LogWarning(e, "The logs database refused {Count} rows; writing them to {Directory} and retrying the database in {Seconds}s.",
                    batch.Count, fallback.Directory, _options.RetryAfterSeconds);
            }
        }

        if (!fallback.TryWrite(batch, now.UtcDateTime))
            logger.LogWarning("{Count} log rows were lost: neither the logs database nor {Directory} could take them.", batch.Count, fallback.Directory);
    }

    private async Task PurgeIfDue(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        if (now < _nextPurge) return;
        _nextPurge = now.AddMinutes(Math.Max(1, _options.PurgeIntervalMinutes));

        var cutoff = now.UtcDateTime.AddHours(-_options.RetentionHours);
        fallback.Purge(cutoff);
        if (now < _databaseRestsUntil) return;

        try
        {
            var deleted = await store.PurgeAsync(cutoff, cancellationToken);
            if (deleted > 0) logger.LogInformation("Purged {Count} log rows older than {Cutoff:u}.", deleted, cutoff);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "Purging the logs database failed; trying again in {Minutes} minutes.", _options.PurgeIntervalMinutes);
        }
    }

    private void ReportDrops()
    {
        var dropped = queue.Dropped;
        if (dropped == _droppedReported) return;
        logger.LogWarning("{Count} log rows were dropped because the queue was full ({Total} since start-up).",
            dropped - _droppedReported, dropped);
        _droppedReported = dropped;
    }
}
