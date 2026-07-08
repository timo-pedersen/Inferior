using Inferior.Game.StationGen;
using Xunit;
using Xunit.Abstractions;

namespace Inferior.Game.Test;

public class DockingBayLayoutTests(ITestOutputHelper output)
{
    [Fact]
    public void DoorSize_OnlyEverTwoVariants_WidthAlwaysSameHeightVariesWithPadMix()
    {
        bool sawMediumOnlyDoor = false, sawLargeDoor = false;

        for (int seed = 0; seed < 500; seed++)
        {
            var layout = DockingBayLayout.Compute(seed, StationScale.Port);

            Assert.Equal(40f, layout.DoorWidth, 3);
            Assert.True(layout.DoorHeight is 16f or 24f,
                $"Unexpected door height {layout.DoorHeight} for seed {seed} (largeCount={layout.LargeCount})");

            bool hasLarge = layout.LargeCount > 0;
            Assert.Equal(hasLarge ? 24f : 16f, layout.DoorHeight, 3);

            if (hasLarge) sawLargeDoor = true; else sawMediumOnlyDoor = true;
        }

        Assert.True(sawMediumOnlyDoor, "Expected at least one medium-only bay (smaller door) across 500 seeds");
        Assert.True(sawLargeDoor, "Expected at least one bay with large pads (bigger door) across 500 seeds");
    }

    [Fact]
    public void ChamferDepth_StaysInRequestedAbsoluteRange()
    {
        for (int seed = 0; seed < 500; seed++)
        {
            var layout = DockingBayLayout.Compute(seed, StationScale.Port);
            float depth = layout.ChamferFraction * layout.DoorHeight;

            Assert.InRange(layout.ChamferFraction, 0.05f, 0.25f);
            Assert.InRange(depth, 0.5f, 6.0f);
        }
    }

    [Fact]
    public void CavityFootprint_GrowsWithPadCount_AndNeverClipsThePads()
    {
        float maxCavityArea = 0;
        int   maxSlots       = 0;

        for (int seed = 0; seed < 200; seed++)
        {
            var layout = DockingBayLayout.Compute(seed, StationScale.Port);
            int slots  = layout.MediumCount + layout.LargeCount * 2;

            // The grid itself (columns/rows of 36m slots + spacing) must fit inside the
            // reported cavity with room to spare for perimeter clearance — never clipped.
            const float slotSize = 36f, spacing = 18f, clearance = 20f;
            float gridWidth = layout.Columns * slotSize + (layout.Columns - 1) * spacing;
            float gridDepth = layout.Rows    * slotSize + (layout.Rows    - 1) * spacing;

            Assert.True(layout.CavityWidth >= gridWidth + 2 * clearance - 0.01f,
                $"Cavity width {layout.CavityWidth} clips the {layout.Columns}-column grid ({gridWidth}) at seed {seed}");
            Assert.True(layout.CavityDepth >= gridDepth + 2 * clearance - 0.01f,
                $"Cavity depth {layout.CavityDepth} clips the {layout.Rows}-row grid ({gridDepth}) at seed {seed}");

            float area = layout.CavityWidth * layout.CavityDepth;
            if (slots > maxSlots) { maxSlots = slots; maxCavityArea = area; }
        }

        output.WriteLine($"Largest pad mix seen: {maxSlots} slot-equivalents, cavity area {maxCavityArea:F0} m^2");
        Assert.True(maxSlots > 4, "Expected to see at least one bay bigger than the minimum capacity across 200 seeds");
    }

    [Fact]
    public void ScaleGate_MegastationAllowsBiggerBaysThanPort()
    {
        int maxPortSlots = 0, maxMegaSlots = 0;
        for (int seed = 0; seed < 200; seed++)
        {
            maxPortSlots = System.Math.Max(maxPortSlots, TotalSlots(DockingBayLayout.Compute(seed, StationScale.Port)));
            maxMegaSlots = System.Math.Max(maxMegaSlots, TotalSlots(DockingBayLayout.Compute(seed, StationScale.Megastation)));
        }

        output.WriteLine($"Max Port slots: {maxPortSlots}, max Megastation slots: {maxMegaSlots}");
        Assert.True(maxMegaSlots > maxPortSlots);
    }

    private static int TotalSlots(DockingBayLayout layout) => layout.MediumCount + layout.LargeCount * 2;
}
