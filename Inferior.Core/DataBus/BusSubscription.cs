namespace Inferior.Core.DataBus;

/// <summary>
/// Couples a <see cref="Bus{T}"/> subscription to its teardown. Subscribes immediately
/// in the constructor; Dispose unsubscribes. Disposing more than once is a no-op.
/// </summary>
public sealed class BusSubscription<T> : IDisposable
{
    private readonly Bus<T>    _bus;
    private readonly string    _topic;
    private readonly Action<T> _handler;
    private bool _disposed;

    public BusSubscription(Bus<T> bus, string topic, Action<T> handler)
    {
        _bus     = bus;
        _topic   = topic;
        _handler = handler;
        _bus.Subscribe(_topic, _handler);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _bus.Unsubscribe(_topic, _handler);
    }
}
