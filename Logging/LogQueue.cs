using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace TournamentScheduler.Api.Logging;

/// <summary>
/// Where log rows wait to be written. Handing a row over never blocks and never throws, so a slow
/// or unreachable logs database can't slow a scorer's tap down. The background writer
/// (<see cref="LogWriterService"/>) empties it in batches.
///
/// Bounded: if the writer falls far behind, new rows are dropped (and counted) rather than letting
/// memory grow without limit.
/// </summary>
public sealed class LogQueue
{
    private readonly Channel<object> _channel;
    private long _dropped;

    public LogQueue(IOptions<LogStoreOptions> options)
    {
        Enabled = options.Value.Enabled;
        _channel = Channel.CreateBounded<object>(new BoundedChannelOptions(Math.Max(1, options.Value.QueueCapacity))
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true
        }, _ => Interlocked.Increment(ref _dropped));
    }

    public bool Enabled { get; }

    /// <summary>Rows dropped because the queue was full, since start-up.</summary>
    public long Dropped => Interlocked.Read(ref _dropped);

    public ChannelReader<object> Reader => _channel.Reader;

    public void Enqueue(MBMiddlewareLog entry) => Write(entry);
    public void Enqueue(MBActivityLog entry) => Write(entry);
    public void Enqueue(ErrorLog entry) => Write(entry);
    public void Enqueue(RemoteLog entry) => Write(entry);

    private void Write(object entry)
    {
        if (Enabled) _channel.Writer.TryWrite(entry);
    }

    /// <summary>No more rows: lets the writer drain what is left and stop.</summary>
    public void Complete() => _channel.Writer.TryComplete();
}
