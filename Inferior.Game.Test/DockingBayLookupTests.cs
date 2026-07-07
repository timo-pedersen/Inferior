using System.Diagnostics;
using Inferior.Galaxy;
using Inferior.Game.StationGen;
using Xunit;

namespace Inferior.Game.Test;

public class DockingBayLookupTests(Xunit.Abstractions.ITestOutputHelper output)
{
    // StationGenerator.FindDockingBay runs only the growth loop (port/AABB math, no
    // GraphicsDevice, no mesh building) — this confirms that's actually cheap enough to call
    // once per station on system-map load, rather than assuming it from the architecture alone.
    [Fact]
    public void FindDockingBay_IsCheapAcrossManyLargeStations()
    {
        const int trials = 500;
        var sw = Stopwatch.StartNew();
        int found = 0;

        for (int i = 0; i < trials; i++)
        {
            var station = new Station { Name = $"Timing Test Station {i}", Size = StationSize.Large };
            if (StationGenerator.FindDockingBay(station) != null) found++;
        }

        sw.Stop();
        double msPerCall = sw.Elapsed.TotalMilliseconds / trials;
        output.WriteLine($"FindDockingBay: {msPerCall:F4} ms/call average over {trials} trials " +
                          $"({found}/{trials} produced a docking bay), total {sw.Elapsed.TotalMilliseconds:F1} ms");

        // Generous bound — actual cost should be microseconds (pure SeededRandom-driven
        // placement math up to ~60 modules at Port scale, no allocation-heavy work).
        Assert.True(msPerCall < 2.0, $"FindDockingBay too slow for per-station caching: {msPerCall:F4} ms/call");

        // Sanity: CoreHubLarge's guaranteed-compatible ports mean the pre-growth attach should
        // succeed for essentially every seed, but this only asserts it happens at all.
        Assert.True(found > 0, "Expected at least one Large station to generate a docking bay");
    }

    [Fact]
    public void FindDockingBay_NeverAppearsBelowPortScale()
    {
        for (int i = 0; i < 100; i++)
        {
            var small  = new Station { Name = $"Small Test {i}",  Size = StationSize.Small };
            var medium = new Station { Name = $"Medium Test {i}", Size = StationSize.Medium };

            Assert.Null(StationGenerator.FindDockingBay(small));
            Assert.Null(StationGenerator.FindDockingBay(medium));
        }
    }

    [Fact]
    public void FindDockingBay_ReturnsRealDefinitionValues()
    {
        for (int i = 0; i < 50; i++)
        {
            var station = new Station { Name = $"Definition Test {i}", Size = StationSize.Large };
            var bay = StationGenerator.FindDockingBay(station);
            if (bay == null) continue;

            Assert.Equal("docking-bay", bay.Category);
            Assert.True(bay.BoundingBox.X > 0 && bay.BoundingBox.Y > 0 && bay.BoundingBox.Z > 0);
            Assert.True(bay.DoorOpening.X > 0 && bay.DoorOpening.Y > 0);
            return;
        }

        Assert.Fail("No docking bay found across 50 Large-station seeds — expected at least one");
    }
}
