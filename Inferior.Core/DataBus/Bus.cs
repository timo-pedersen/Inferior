using System.Collections.Concurrent;

namespace Inferior.Core.DataBus;

/// <summary>
/// Thread-safe message bus. Publish from any thread; Drain and Subscribe on main thread only.
/// </summary>
public sealed class Bus<T>
{
    private readonly ConcurrentQueue<(string topic, T value)> _queue    = new();
    private readonly Dictionary<string, List<Action<T>>>      _handlers = new();

    // Safe from any thread — enqueues only, never blocks
    public void Publish(string topic, T value)
        => _queue.Enqueue((topic, value));

    // Main thread only — dispatches all pending messages synchronously
    public void Drain()
    {
        while (_queue.TryDequeue(out var msg))
            if (_handlers.TryGetValue(msg.topic, out var handlers))
                foreach (var h in handlers)
                    h(msg.value);
    }

    // Main thread only
    public void Subscribe(string topic, Action<T> handler)
    {
        if (!_handlers.TryGetValue(topic, out var list))
            _handlers[topic] = list = [];
        list.Add(handler);
    }

    public void Unsubscribe(string topic, Action<T> handler)
    {
        if (_handlers.TryGetValue(topic, out var list))
            list.Remove(handler);
    }
}
