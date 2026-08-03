using System.Collections.Concurrent;

namespace Inferior.Core.DataBus;

/// <summary>
/// Thread-safe topic-based pub/sub channel. Publishers may enqueue from any thread;
/// topic configuration, subscription, replay, and draining belong to the consumer thread.
/// </summary>
public sealed class Bus<T>
{
    private readonly ConcurrentQueue<(string Topic, T Value)> _queue = new();
    private readonly Dictionary<string, List<Action<T>>> _handlers = new();
    private readonly Dictionary<string, TopicPolicy> _policies = new();
    private readonly Dictionary<string, T> _latest = new();
    private readonly Dictionary<string, Queue<T>> _history = new();
    private readonly TopicPolicy _defaultPolicy;
    private readonly int _pendingCapacity;
    private int _pendingCount;
    private long _droppedMessageCount;

    public Bus(TopicPolicy? defaultPolicy = null, int pendingCapacity = 65_536)
    {
        if (pendingCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(pendingCapacity));

        _defaultPolicy = defaultPolicy ?? TopicPolicy.OrderedTransient;
        _defaultPolicy.Validate();
        _pendingCapacity = pendingCapacity;
    }

    /// <summary>Number of oldest pending publications discarded to enforce the queue bound.</summary>
    public long DroppedMessageCount => Interlocked.Read(ref _droppedMessageCount);

    /// <summary>Enqueue a publication without blocking the publisher.</summary>
    public void Publish(string topic, T value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        _queue.Enqueue((topic, value));
        int count = Interlocked.Increment(ref _pendingCount);

        while (count > _pendingCapacity && _queue.TryDequeue(out _))
        {
            count = Interlocked.Decrement(ref _pendingCount);
            Interlocked.Increment(ref _droppedMessageCount);
        }
    }

    /// <summary>
    /// Define the stable dispatch/retention contract for a topic. Existing retained data
    /// is discarded if it is incompatible with the new policy.
    /// </summary>
    public void ConfigureTopic(string topic, TopicPolicy policy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        policy.Validate();
        _policies[topic] = policy;

        if (policy.Retention == RetentionMode.None)
        {
            _latest.Remove(topic);
            _history.Remove(topic);
        }
        else if (policy.Retention == RetentionMode.Latest)
        {
            _history.Remove(topic);
        }
        else if (_history.TryGetValue(topic, out var history))
        {
            while (history.Count > policy.HistoryCapacity)
                history.Dequeue();
        }
    }

    public TopicPolicy GetTopicPolicy(string topic)
        => _policies.TryGetValue(topic, out var policy) ? policy : _defaultPolicy;

    /// <summary>
    /// Dispatch all pending publications. Latest-per-drain topics deliver only their
    /// final queued value; ordered topics deliver every queued value.
    /// </summary>
    public void Drain()
    {
        if (_queue.IsEmpty)
            return;

        var pending = new List<(string Topic, T Value)>();
        while (_queue.TryDequeue(out var message))
        {
            Interlocked.Decrement(ref _pendingCount);
            pending.Add(message);
        }

        var lastCoalescedIndex = new Dictionary<string, int>();
        for (int i = 0; i < pending.Count; i++)
        {
            string topic = pending[i].Topic;
            if (GetTopicPolicy(topic).Dispatch == DispatchMode.LatestPerDrain)
                lastCoalescedIndex[topic] = i;
        }

        for (int i = 0; i < pending.Count; i++)
        {
            var message = pending[i];
            TopicPolicy policy = GetTopicPolicy(message.Topic);
            if (policy.Dispatch == DispatchMode.LatestPerDrain &&
                lastCoalescedIndex[message.Topic] != i)
            {
                continue;
            }

            Retain(message.Topic, message.Value, policy);
            Dispatch(message.Topic, message.Value);
        }
    }

    /// <summary>
    /// Subscribe and optionally replay retained data to this handler only. Replay is not
    /// a publication and never invokes existing subscribers.
    /// </summary>
    public IDisposable Subscribe(string topic, Action<T> handler, ReplayMode replay = ReplayMode.None)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentNullException.ThrowIfNull(handler);

        if (!_handlers.TryGetValue(topic, out var handlers))
            _handlers[topic] = handlers = [];
        handlers.Add(handler);

        try
        {
            Replay(topic, handler, replay);
        }
        catch
        {
            Unsubscribe(topic, handler);
            throw;
        }
        return new Subscription(this, topic, handler);
    }

    public void Unsubscribe(string topic, Action<T> handler)
    {
        if (!_handlers.TryGetValue(topic, out var handlers))
            return;

        handlers.Remove(handler);
        if (handlers.Count == 0)
            _handlers.Remove(topic);
    }

    /// <summary>Forget retained values for one topic without affecting subscribers.</summary>
    public void RemoveRetained(string topic)
    {
        _latest.Remove(topic);
        _history.Remove(topic);
    }

    /// <summary>Forget all retained values without affecting topic policies or subscribers.</summary>
    public void ClearRetained()
    {
        _latest.Clear();
        _history.Clear();
    }

    private void Retain(string topic, T value, TopicPolicy policy)
    {
        switch (policy.Retention)
        {
            case RetentionMode.None:
                return;

            case RetentionMode.Latest:
                _latest[topic] = value;
                return;

            case RetentionMode.History:
                _latest[topic] = value;
                if (!_history.TryGetValue(topic, out var history))
                    _history[topic] = history = new Queue<T>(policy.HistoryCapacity);
                history.Enqueue(value);
                while (history.Count > policy.HistoryCapacity)
                    history.Dequeue();
                return;

            default:
                throw new ArgumentOutOfRangeException(nameof(policy));
        }
    }

    private void Dispatch(string topic, T value)
    {
        if (!_handlers.TryGetValue(topic, out var handlers))
            return;

        // A handler may dispose a subscription while handling a message.
        foreach (Action<T> handler in handlers.ToArray())
            handler(value);
    }

    private void Replay(string topic, Action<T> handler, ReplayMode replay)
    {
        switch (replay)
        {
            case ReplayMode.None:
                return;

            case ReplayMode.Latest:
                if (_latest.TryGetValue(topic, out var latest))
                    handler(latest);
                return;

            case ReplayMode.History:
                if (_history.TryGetValue(topic, out var history))
                {
                    foreach (T value in history)
                        handler(value);
                }
                else if (_latest.TryGetValue(topic, out var onlyLatest))
                {
                    handler(onlyLatest);
                }
                return;

            default:
                throw new ArgumentOutOfRangeException(nameof(replay));
        }
    }

    private sealed class Subscription(Bus<T> bus, string topic, Action<T> handler) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            bus.Unsubscribe(topic, handler);
        }
    }
}
