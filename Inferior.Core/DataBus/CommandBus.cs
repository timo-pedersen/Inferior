using System.Collections.Concurrent;

namespace Inferior.Core.DataBus;

/// <summary>
/// One-way command channel: main thread → sim thread.
/// Main thread calls Send(); sim thread calls Drain() once per tick to dispatch.
///
/// Handlers are registered by components during initialization and matched by topic prefix,
/// so "Reactor" matches "Reactor.Throttle.Set", "Reactor.Output.Query", etc.
/// </summary>
public static class CommandBus
{
    private static readonly ConcurrentQueue<ComponentCommand>                      _queue    = new();
    private static readonly List<(string Prefix, Action<ComponentCommand> Handler)> _handlers = new();
    private static readonly object                                                  _lock     = new();

    /// <summary>Send a command. Safe to call from any thread (typically the main thread).</summary>
    public static void Send(string topic, double value = 0.0)
        => _queue.Enqueue(new ComponentCommand(topic, value));

    /// <summary>
    /// Register a handler for all commands whose topic starts with <paramref name="prefix"/>.
    /// Call from the sim thread during component initialization.
    /// </summary>
    public static IDisposable Subscribe(string prefix, Action<ComponentCommand> handler)
    {
        var subscription = (prefix, handler);
        lock (_lock) _handlers.Add(subscription);
        return new Subscription(subscription);
    }

    /// <summary>
    /// Drain the queue and dispatch to matching handlers. Call once per sim tick.
    /// </summary>
    public static void Drain()
    {
        (string, Action<ComponentCommand>)[] snapshot;
        lock (_lock) snapshot = [.. _handlers];

        while (_queue.TryDequeue(out var cmd))
            foreach (var (prefix, handler) in snapshot)
                if (cmd.Topic.StartsWith(prefix, StringComparison.Ordinal))
                    handler(cmd);
    }

    private sealed class Subscription(
        (string Prefix, Action<ComponentCommand> Handler) subscription) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed)
                    return;
                _disposed = true;
                _handlers.Remove(subscription);
            }
        }
    }
}
