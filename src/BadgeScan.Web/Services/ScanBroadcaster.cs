using System.Collections.Concurrent;
using System.Threading.Channels;
using BadgeScan.Web.Models;

namespace BadgeScan.Web.Services;

/// <summary>
/// Hands every saved scan to everyone watching the live list. Each watcher (a kiosk tab
/// on /api/scans/stream) gets its own small queue; a watcher that stops reading loses its
/// oldest items rather than holding everyone else up.
/// </summary>
public sealed class ScanBroadcaster
{
    private readonly ConcurrentDictionary<Guid, Channel<ScanResult>> _watchers = new();

    public int WatcherCount => _watchers.Count;

    public Subscription Subscribe()
    {
        var channel = Channel.CreateBounded<ScanResult>(new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropOldest });
        var id = Guid.NewGuid();
        _watchers[id] = channel;
        return new Subscription(channel.Reader, () =>
        {
            _watchers.TryRemove(id, out _);
            channel.Writer.TryComplete();
        });
    }

    public void Publish(ScanResult scan)
    {
        foreach (var channel in _watchers.Values) channel.Writer.TryWrite(scan);
    }

    public sealed class Subscription(ChannelReader<ScanResult> reader, Action unsubscribe) : IDisposable
    {
        public ChannelReader<ScanResult> Reader => reader;
        public void Dispose() => unsubscribe();
    }
}
