using Inferior.Game.Ships;
using Inferior.Gameplay.Hull;
using Inferior.Gameplay.Ship;
using Xunit;
using Xunit.Abstractions;

namespace Inferior.Game.Test;

/// <summary>
/// Regression coverage for the ShipPropulsion.Resolve() allocation-reduction work (see
/// Docs/gc-optimization-question.md finding #2: heaviest single per-tick call site).
/// ResolvedEnginePropulsion is now a readonly record struct (was a sealed record — one heap
/// allocation per installed engine) and Resolve() allocates exactly one precisely-sized array
/// (no intermediate List, no ToArray() copy, no Array.AsReadOnly() wrapper, and no boxed
/// enumerator from iterating Ship.EngineMounts/ShipPropulsionCapability.Engines as
/// IReadOnlyList&lt;T&gt;). Measured baseline before this fix, same 2-engine Aries workload:
/// 520 bytes/call, spread across up to 5 separate heap objects. After: 320 bytes/call in exactly
/// one array allocation.
/// </summary>
public sealed class ShipPropulsionAllocationTests(ITestOutputHelper output)
{
    // Documented pre-fix baseline for this exact workload (List&lt;record&gt; + N records +
    // ToArray() + Array.AsReadOnly()). The assertion below is a strict "must not regress toward
    // this" gate rather than an exact pin, since the precise byte count legitimately shifts with
    // struct field layout.
    private const long PreFixBaselineBytesPerCall = 520;

    [Fact]
    public void SteadyStateResolveAllocatesLessThanTheRecordBasedBaseline()
    {
        Ship ship = ShipBuilder.NewShip(AriesHullDefinitionFactory.HullId)
            .WithDefaultStartingComponents()
            .Build();

        void OneCall() => ShipPropulsion.Resolve(ship);

        // Warm up (JIT) before measuring.
        for (int i = 0; i < 50; i++)
            OneCall();

        const int measuredCalls = 10_000;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < measuredCalls; i++)
            OneCall();
        long after = GC.GetAllocatedBytesForCurrentThread();

        long totalBytes = after - before;
        double bytesPerCall = totalBytes / (double)measuredCalls;
        output.WriteLine($"{totalBytes} bytes over {measuredCalls} calls ({bytesPerCall:F1} bytes/call, " +
            $"pre-fix baseline was {PreFixBaselineBytesPerCall})");

        Assert.True(bytesPerCall < PreFixBaselineBytesPerCall,
            $"Resolve() allocated {bytesPerCall:F1} bytes/call, at or above the " +
            $"{PreFixBaselineBytesPerCall} bytes/call pre-fix baseline — the array/struct " +
            "allocation reduction appears to have regressed.");
    }
}
