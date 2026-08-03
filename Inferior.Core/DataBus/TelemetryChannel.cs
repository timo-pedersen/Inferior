using Inferior.Core.Simulation;

namespace Inferior.Core.DataBus;

/// <summary>
/// Typed telemetry channel layered over <see cref="Bus{T}"/>. It adds a common sample
/// envelope while allowing ordinary value consumers to remain unaware of timing metadata.
/// </summary>
public sealed class TelemetryChannel<T>
{
    private readonly Bus<TelemetrySample<T>> _bus;
    private readonly Dictionary<(string Topic, Action<T> Handler), List<IDisposable>> _valueSubscriptions = new();
    private long _sequence;

    public TelemetryChannel(TopicPolicy? defaultPolicy = null, int pendingCapacity = 65_536)
        => _bus = new Bus<TelemetrySample<T>>(
            defaultPolicy ?? TopicPolicy.LatestState,
            pendingCapacity);

    public long DroppedMessageCount => _bus.DroppedMessageCount;

    public void ConfigureTopic(string topic, TopicPolicy policy)
        => _bus.ConfigureTopic(topic, policy);

    public TopicPolicy GetTopicPolicy(string topic)
        => _bus.GetTopicPolicy(topic);

    /// <summary>Publish using the current session-local simulation time.</summary>
    public void Publish(string topic, T value)
        => Publish(topic, value, GameClock.SimTime);

    public void Publish(string topic, T value, double simulationTime)
    {
        ulong sequence = unchecked((ulong)Interlocked.Increment(ref _sequence));
        _bus.Publish(topic, new TelemetrySample<T>(value, simulationTime, sequence));
    }

    /// <summary>Subscribe to values while ignoring the transport envelope.</summary>
    public IDisposable Subscribe(string topic, Action<T> handler, ReplayMode replay = ReplayMode.Latest)
    {
        ArgumentNullException.ThrowIfNull(handler);
        Action<TelemetrySample<T>> sampleHandler = sample => handler(sample.Value);
        IDisposable subscription = _bus.Subscribe(topic, sampleHandler, replay);

        var key = (topic, handler);
        if (!_valueSubscriptions.TryGetValue(key, out var subscriptions))
            _valueSubscriptions[key] = subscriptions = [];
        subscriptions.Add(subscription);

        return new ValueSubscription(this, key, subscription);
    }

    /// <summary>Subscribe to the complete sample, including time and sequence.</summary>
    public IDisposable SubscribeSamples(
        string topic,
        Action<TelemetrySample<T>> handler,
        ReplayMode replay = ReplayMode.Latest)
        => _bus.Subscribe(topic, handler, replay);

    /// <summary>
    /// Compatibility teardown for existing value subscriptions. New code should retain and
    /// dispose the object returned by <see cref="Subscribe(string, Action{T}, ReplayMode)"/>.
    /// </summary>
    public void Unsubscribe(string topic, Action<T> handler)
    {
        var key = (topic, handler);
        if (!_valueSubscriptions.TryGetValue(key, out var subscriptions) || subscriptions.Count == 0)
            return;

        IDisposable subscription = subscriptions[^1];
        subscription.Dispose();
        RemoveValueSubscription(key, subscription);
    }

    public void Drain() => _bus.Drain();

    public void RemoveRetained(string topic) => _bus.RemoveRetained(topic);

    public void ClearRetained() => _bus.ClearRetained();

    private void RemoveValueSubscription(
        (string Topic, Action<T> Handler) key,
        IDisposable subscription)
    {
        if (!_valueSubscriptions.TryGetValue(key, out var subscriptions))
            return;

        subscriptions.Remove(subscription);
        if (subscriptions.Count == 0)
            _valueSubscriptions.Remove(key);
    }

    private sealed class ValueSubscription(
        TelemetryChannel<T> owner,
        (string Topic, Action<T> Handler) key,
        IDisposable inner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            inner.Dispose();
            owner.RemoveValueSubscription(key, inner);
        }
    }
}
