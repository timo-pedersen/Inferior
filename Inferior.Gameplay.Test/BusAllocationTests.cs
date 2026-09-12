using Inferior.Core.DataBus;
using Xunit;
using Xunit.Abstractions;

namespace Inferior.Gameplay.Test;

/// <summary>
/// Regression coverage for the Bus&lt;T&gt; allocation-reduction work: Drain() reuses its
/// scratch buffers and Dispatch() caches per-topic handler snapshots instead of allocating a
/// fresh List/Dictionary/array on every drain/message respectively (see Docs/gc-optimization-question.md
/// finding #1). Measured baseline before this fix, same workload: ~2368 bytes/tick.
/// </summary>
public sealed class BusAllocationTests(ITestOutputHelper output)
{
    [Fact]
    public void SteadyStateDrainAllocatesNoManagedMemory()
    {
        // Mirrors DataBus.ScalarTelemetry's real shape: ~25 topics, a handful of subscribers
        // each, ~25 publishes/tick, drained once per frame.
        var bus = new Bus<double>(TopicPolicy.OrderedTransient);
        const int topicCount = 25;
        var topics = new string[topicCount];
        for (int i = 0; i < topicCount; i++)
            topics[i] = $"Topic{i}";

        var subscriptions = new List<IDisposable>();
        foreach (string topic in topics)
        {
            for (int s = 0; s < 3; s++)
                subscriptions.Add(bus.Subscribe(topic, _ => { }));
        }

        void OneTick()
        {
            foreach (string topic in topics)
                bus.Publish(topic, 1.0);
            bus.Drain();
        }

        // Warm up (JIT, initial List/Dictionary growth) before measuring.
        for (int i = 0; i < 50; i++)
            OneTick();

        const int measuredTicks = 1000;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < measuredTicks; i++)
            OneTick();
        long after = GC.GetAllocatedBytesForCurrentThread();

        long totalBytes = after - before;
        output.WriteLine($"{totalBytes} bytes over {measuredTicks} ticks " +
            $"({totalBytes / (double)measuredTicks:F1} bytes/tick)");

        // Zero once warmed up: Subscribe/Unsubscribe are stable across these ticks, so the
        // pending-message buffer, the coalescing-index buffer, and every topic's handler
        // snapshot are all already sized and cached. A regression here means one of those
        // reused buffers started reallocating again.
        Assert.Equal(0, totalBytes);

        foreach (var subscription in subscriptions)
            subscription.Dispose();
    }
}
