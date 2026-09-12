using Inferior.Gameplay.Sensors;
using Xunit;
using Xunit.Abstractions;

namespace Inferior.Gameplay.Test;

/// <summary>
/// Regression coverage for the per-tick sensor topic-string caching work (see
/// Docs/gc-optimization-question.md finding #3). PassiveSensor.Publish() now caches
/// "{TopicPrefix}.{ValueName}" once (lazily — TopicPrefix/ValueName are init-only, fixed by the
/// time any instance method runs, but not computable in a constructor since init setters run
/// after construction) instead of interpolating it fresh every call; MagneticFieldSensor and
/// SolarSpectrumSensor now do the same for their direction/data topics (previously computed once
/// at construction for TelemetryInfo/DeviceInfo publication, but then redundantly re-interpolated
/// on every Tick() instead of reusing that already-computed string).
///
/// Note on scope: several sites the GC doc flagged as "the same pattern" (LandingSupportSystem's
/// 8 sites, ShieldComponent) turned out — verified, not assumed — to already be zero-allocation:
/// their topic interpolations have only `const string` parts, which the C# compiler folds into a
/// single interned literal at compile time. No fix was needed or applied there.
/// </summary>
public sealed class SensorTopicAllocationTests(ITestOutputHelper output)
{
    // Documented pre-fix baselines for these exact workloads (fresh interpolation every call).
    private const double GravitySensorPreFixBaselineBytesPerTick = 137.6;
    private const double MagneticFieldSensorPreFixBaselineBytesPerTick = 137.2;

    [Fact]
    public void GravitySensorTickAllocatesLessThanThePreFixBaseline()
    {
        var sensor = new GravitySensor();
        MeasureAndAssert("GravitySensor.Tick()", sensor.Tick, GravitySensorPreFixBaselineBytesPerTick);
    }

    [Fact]
    public void MagneticFieldSensorTickAllocatesLessThanThePreFixBaseline()
    {
        var sensor = new MagneticFieldSensor("TestMagSensor");
        MeasureAndAssert("MagneticFieldSensor.Tick()", sensor.Tick, MagneticFieldSensorPreFixBaselineBytesPerTick);
    }

    private void MeasureAndAssert(string label, Action tick, double preFixBaselineBytesPerTick)
    {
        for (int i = 0; i < 50; i++)
            tick();

        const int measuredTicks = 10_000;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < measuredTicks; i++)
            tick();
        long after = GC.GetAllocatedBytesForCurrentThread();

        double bytesPerTick = (after - before) / (double)measuredTicks;
        output.WriteLine($"{label}: {bytesPerTick:F1} bytes/tick (pre-fix baseline was {preFixBaselineBytesPerTick})");

        // Not zero: Bus<T>.Publish()'s underlying ConcurrentQueue<T> allocates a new segment
        // every ~32 enqueues — a pre-existing, structural cost unrelated to topic strings and
        // out of scope here. This asserts the topic-string allocation specifically is gone,
        // as a "must not regress toward the record baseline" gate.
        Assert.True(bytesPerTick < preFixBaselineBytesPerTick,
            $"{label} allocated {bytesPerTick:F1} bytes/tick, at or above the " +
            $"{preFixBaselineBytesPerTick} bytes/tick pre-fix baseline — topic-string caching " +
            "appears to have regressed.");
    }
}
