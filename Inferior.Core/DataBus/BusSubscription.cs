namespace Inferior.Core.DataBus;

/// <summary>
/// Couples a <see cref="Bus{T}"/> subscription to its teardown. Subscribes immediately
/// in the constructor; Dispose unsubscribes. Disposing more than once is a no-op.
/// </summary>
public sealed class BusSubscription<T> : IDisposable
{
    private readonly IDisposable _subscription;

    public BusSubscription(
        Bus<T> bus,
        string topic,
        Action<T> handler,
        ReplayMode replay = ReplayMode.None)
        => _subscription = bus.Subscribe(topic, handler, replay);

    public BusSubscription(
        TelemetryChannel<T> channel,
        string topic,
        Action<T> handler,
        ReplayMode replay = ReplayMode.Latest)
        => _subscription = channel.Subscribe(topic, handler, replay);

    public void Dispose() => _subscription.Dispose();
}
