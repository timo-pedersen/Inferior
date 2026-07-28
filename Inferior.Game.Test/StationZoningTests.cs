using System.Linq;
using Inferior.Game.StationGen;
using Microsoft.Xna.Framework;
using Xunit;

namespace Inferior.Game.Test;

// Brief Z1 — Face Zoning Infrastructure. StationDecorator.ComputeZones/AssignZoneTypes are
// internal, GraphicsDevice-free pure helpers (same testable-helper pattern as
// StationTextureRegistry.OffsetPaletteForVariant / ComputeWindowGrid), so the mechanism can
// be verified directly instead of only through a full Decorate() run.
public sealed class StationZoningTests
{
    private static StationDecorator.FaceInfo MakeFace(float width, float height)
        => new(Vector3.UnitZ, Vector3.Zero, Vector3.UnitX, Vector3.UnitY, width, height, isExposed: true);

    // The exact five faces verified as "the ordinary module catalogue" in the window-sizing
    // brief (hab/core/science/industrial/cargo) must stay unzoned — this is the "small
    // modules bit-identical" gate's structural precondition. A real Decorate()-level A/B
    // hash comparison (20 seeded stations, before/after Z1) confirmed these five module
    // types' mesh/glass byte output is unchanged; this test guards the underlying threshold
    // decision so a future constant tweak can't silently break it without a red test.
    [Theory]
    [InlineData(18f, 14f)] // hab, side face
    [InlineData(20f, 20f)] // core, any face (cube)
    [InlineData(14f, 14f)] // science, any face (cube)
    [InlineData(22f, 18f)] // industrial, side face
    [InlineData(24f, 12f)] // cargo, largest face
    public void ComputeZones_OrdinaryModuleCatalogue_StaysUnzoned(float width, float height)
    {
        var rng = new System.Random(12345);
        var (zones, isUnzoned) = StationDecorator.ComputeZones(MakeFace(width, height), rng);

        Assert.True(isUnzoned, $"Expected a {width}x{height} face to stay below the zoning threshold");
        Assert.Single(zones);
        Assert.Equal(width,  zones[0].Width);
        Assert.Equal(height, zones[0].Height);
    }

    // The "_Large" module tier (36-48m) and mega faces (100m+) are exactly what Z1 exists
    // to organise — they must actually zone, not silently stay in the bit-identical path.
    [Theory]
    [InlineData(36f, 28f)]  // hab-block-large
    [InlineData(48f, 24f)]  // cargo-bay-large
    [InlineData(44f, 36f)]  // industrial-block-large
    [InlineData(239.47f, 33.47f)]  // a real mega docking-bay wall (station seed 11, Port)
    [InlineData(239.47f, 239.47f)] // the same bay's top/bottom faces
    public void ComputeZones_LargeAndMegaFaces_DoZone(float width, float height)
    {
        var rng = new System.Random(54321);
        var (_, isUnzoned) = StationDecorator.ComputeZones(MakeFace(width, height), rng);

        Assert.False(isUnzoned, $"Expected a {width}x{height} face to be subdivided, not bypassed");
    }

    // A face producing a single whole-face zone must be geometrically identical to the
    // original face (not just "close") — the critical no-op property the brief calls out
    // explicitly: reconstructing via offsetU=offsetV=0 must reproduce the exact same
    // FaceInfo, since a downstream pass reads Width/Height/LocalCenter directly.
    [Fact]
    public void ComputeZones_Unzoned_ReturnsFaceUnchanged()
    {
        var face = MakeFace(18f, 14f);
        var rng = new System.Random(1);
        var (zones, isUnzoned) = StationDecorator.ComputeZones(face, rng);

        Assert.True(isUnzoned);
        Assert.Equal(face.LocalCenter, zones[0].LocalCenter);
        Assert.Equal(face.LocalNormal, zones[0].LocalNormal);
        Assert.Equal(face.LocalRight,  zones[0].LocalRight);
        Assert.Equal(face.LocalUp,     zones[0].LocalUp);
        Assert.Equal(face.Width,  zones[0].Width);
        Assert.Equal(face.Height, zones[0].Height);
    }

    // The grid/merge algorithm must fully cover a zoned face with non-overlapping
    // rectangles: total zone area must equal face area (no gaps, no double-claimed cells).
    [Theory]
    [InlineData(36f, 28f)]
    [InlineData(48f, 24f)]
    [InlineData(239.47f, 239.47f)]
    public void ComputeZones_ZonedFace_FullyTilesWithNoOverlap(float width, float height)
    {
        var rng = new System.Random(777);
        var (zones, isUnzoned) = StationDecorator.ComputeZones(MakeFace(width, height), rng);
        Assert.False(isUnzoned);

        float totalArea = zones.Sum(z => z.Width * z.Height);
        float faceArea  = width * height;
        Assert.Equal(faceArea, totalArea, 1);
    }

    [Fact]
    public void ComputeZones_IsDeterministic()
    {
        var face = MakeFace(239.47f, 239.47f);
        var (zonesA, _) = StationDecorator.ComputeZones(face, new System.Random(42));
        var (zonesB, _) = StationDecorator.ComputeZones(face, new System.Random(42));

        Assert.Equal(zonesA.Length, zonesB.Length);
        for (int i = 0; i < zonesA.Length; i++)
        {
            Assert.Equal(zonesA[i].LocalCenter, zonesB[i].LocalCenter);
            Assert.Equal(zonesA[i].Width,  zonesB[i].Width);
            Assert.Equal(zonesA[i].Height, zonesB[i].Height);
        }
    }

    // Windows are count-driven (0-3 per face, capped 5 per module), not weight-driven — the
    // brief's specific fix for the "24 window zones on a 40-zone bay wall" failure mode.
    [Fact]
    public void AssignZoneTypes_WindowZones_NeverExceedPerFaceOrPerModuleCap()
    {
        var rng = new System.Random(2026);
        var face = MakeFace(239.47f, 239.47f);
        var (zones, isUnzoned) = StationDecorator.ComputeZones(face, rng);
        Assert.False(isUnzoned);
        Assert.True(zones.Length > 5, "Need a large enough zone count for this test to mean anything");

        var budget = new StationDecorator.ModuleZoneBudget();
        int totalWindowZones = 0;
        // Simulate several faces of one module sharing the same budget object.
        for (int f = 0; f < 4; f++)
        {
            var types = StationDecorator.AssignZoneTypes("docking-bay", zones, rng, budget);
            int thisFace = types.Count(t => t == StationDecorator.ZoneType.Windows);
            Assert.True(thisFace <= 3, $"Expected <=3 window zones per face, got {thisFace}");
            totalWindowZones += thisFace;
        }

        Assert.True(totalWindowZones <= 5, $"Expected <=5 window zones per module, got {totalWindowZones}");
        Assert.True(budget.WindowZonesRemaining >= 0);
    }

    // Brief Z2 superseded Z1's "CommsArray/PipeCorridor never assigned" invariant — they're
    // now real guaranteed-set types. What's still true: ZoneTypeWeights itself has no entry
    // for either, so the WEIGHT pass alone (guaranteed set pre-satisfied/exhausted) must
    // never produce them.
    [Theory]
    [InlineData("industrial")]
    [InlineData("docking-bay")]
    [InlineData("cargo")]
    [InlineData("hab")]
    [InlineData("science")]
    [InlineData("core")]
    [InlineData("connector")]
    [InlineData("some-unlisted-category")]
    public void AssignZoneTypes_WeightPassAlone_NeverProducesCommsArrayOrPipeCorridor(string category)
    {
        var rng = new System.Random(31337);
        var face = MakeFace(239.47f, 239.47f);
        var (zones, _) = StationDecorator.ComputeZones(face, rng);

        var budget = new StationDecorator.ModuleZoneBudget
        {
            TankFarmRemaining = 0,
            NeedsPipeCorridor = false,
            NeedsCommsArray   = false,
            NeedsSignage      = false,
        };

        var types = StationDecorator.AssignZoneTypes(category, zones, rng, budget);

        Assert.DoesNotContain(StationDecorator.ZoneType.CommsArray,   types);
        Assert.DoesNotContain(StationDecorator.ZoneType.PipeCorridor, types);
    }

    // Per Timo: tanks and pipes should dominate industrial/bay categories. Statistical
    // check over many zones/seeds rather than a single roll, since PickWeightedZoneType is
    // randomized per zone. Under Z2, guaranteed TankFarm zones add to this share too — the
    // bound stays a loose statistical floor, not an exact accounting.
    [Fact]
    public void AssignZoneTypes_IndustrialCategory_MachineryAndTankFarmDominate()
    {
        var face = MakeFace(239.47f, 239.47f);
        var counts = new Dictionary<StationDecorator.ZoneType, int>();

        for (int seed = 0; seed < 50; seed++)
        {
            var rng = new System.Random(seed);
            var (zones, _) = StationDecorator.ComputeZones(face, rng);
            var budget = new StationDecorator.ModuleZoneBudget();
            var types = StationDecorator.AssignZoneTypes("industrial", zones, rng, budget);
            foreach (var t in types)
                counts[t] = counts.GetValueOrDefault(t) + 1;
        }

        int machineryAndTankFarm = counts.GetValueOrDefault(StationDecorator.ZoneType.Machinery)
                                 + counts.GetValueOrDefault(StationDecorator.ZoneType.TankFarm);
        int total = counts.Values.Sum();

        Assert.True(machineryAndTankFarm > total / 3,
            $"Expected Machinery+TankFarm to be a dominant share of industrial zones, got {machineryAndTankFarm}/{total}");
    }

    // Structural zones must be common enough to visibly break up large faces (brief
    // verification item 4) — check they appear at all across a realistic sample, for every
    // category's weight table. Under Z2 this is now a per-face GUARANTEE (see the dedicated
    // test below), so this loose statistical check necessarily still holds — kept as-is
    // since it's still a true, valid statement, just a weaker one than the new guarantee.
    [Theory]
    [InlineData("industrial")]
    [InlineData("docking-bay")]
    [InlineData("cargo")]
    [InlineData("hab")]
    [InlineData("science")]
    [InlineData("core")]
    [InlineData("connector")]
    public void AssignZoneTypes_StructuralZones_AppearAcrossSample(string category)
    {
        var face = MakeFace(239.47f, 239.47f);
        int structuralCount = 0;

        for (int seed = 0; seed < 30; seed++)
        {
            var rng = new System.Random(seed);
            var (zones, _) = StationDecorator.ComputeZones(face, rng);
            var budget = new StationDecorator.ModuleZoneBudget();
            var types = StationDecorator.AssignZoneTypes(category, zones, rng, budget);
            structuralCount += types.Count(t => t == StationDecorator.ZoneType.Structural);
        }

        Assert.True(structuralCount > 0, $"Expected at least some Structural zones for category '{category}'");
    }

    // ── Brief Z2: guaranteed set (Part 1) ──────────────────────────────────────

    // The core Z2 guarantee: across a module's several multi-zone faces sharing one
    // ModuleZoneBudget (exactly how Decorate() constructs and threads it), the floor is
    // met — 1-4 tank farms, >=1 pipe corridor, >=1 comms array, exactly 1 signage — and
    // Structural appears on EVERY individual face (a per-face floor, not module-wide).
    [Fact]
    public void AssignZoneTypes_GuaranteedSet_MetAcrossModule()
    {
        var rng = new System.Random(9001);
        var face = MakeFace(239.47f, 239.47f);
        var budget = new StationDecorator.ModuleZoneBudget();

        int tankFarmTotal = 0;
        for (int f = 0; f < 5; f++)
        {
            var (zones, isUnzoned) = StationDecorator.ComputeZones(face, rng);
            Assert.False(isUnzoned);
            var types = StationDecorator.AssignZoneTypes("docking-bay", zones, rng, budget);

            Assert.Contains(StationDecorator.ZoneType.Structural, types);
            tankFarmTotal += types.Count(t => t == StationDecorator.ZoneType.TankFarm);
        }

        // The guaranteed floor itself is satisfied (budget fully consumed, not still
        // pending) — the weight pass may have added even more TankFarm zones on top of
        // the 1-4 target, so tankFarmTotal itself has no fixed upper bound here.
        Assert.Equal(0, budget.TankFarmRemaining);
        Assert.True(tankFarmTotal >= 1, "Expected at least one TankFarm zone across the module");
        Assert.False(budget.NeedsPipeCorridor, "Expected the module-wide PipeCorridor guarantee to be met");
        Assert.False(budget.NeedsCommsArray,   "Expected the module-wide CommsArray guarantee to be met");
        Assert.False(budget.NeedsSignage,      "Expected the module-wide Signage guarantee to be met");
    }

    // Exactly one Signage zone per module, never more, even across many faces.
    [Fact]
    public void AssignZoneTypes_Signage_ExactlyOnePerModule()
    {
        var rng = new System.Random(1357);
        var face = MakeFace(239.47f, 239.47f);
        var budget = new StationDecorator.ModuleZoneBudget();

        int signageTotal = 0;
        for (int f = 0; f < 6; f++)
        {
            var (zones, _) = StationDecorator.ComputeZones(face, rng);
            var types = StationDecorator.AssignZoneTypes("docking-bay", zones, rng, budget);
            signageTotal += types.Count(t => t == StationDecorator.ZoneType.Signage);
        }

        Assert.Equal(1, signageTotal);
    }

    // Graceful degradation (Brief Z2 Part 1): a face with too few zones for the full
    // guaranteed set claims in strict priority order (tank farm, then structural, then
    // pipe corridor, then comms array, then signage) rather than an arbitrary subset, and
    // never throws. A hand-built 2-zone array (bypassing ComputeZones' own randomness)
    // makes the exact zone count deterministic. TankFarm's own target is forced to exactly
    // 1 (rather than left to its normal 1-4 roll) so this test isolates priority order
    // itself, not TankFarm's random target size: with a target of 1 and 2 zones available,
    // TankFarm (priority 1) takes exactly one slot, Structural (priority 2) takes the
    // other, and PipeCorridor/CommsArray/Signage get nothing this face — simply pending on
    // the budget for a later face, not an error.
    [Fact]
    public void AssignZoneTypes_TooFewZonesForFullGuaranteedSet_ClaimsInPriorityOrder()
    {
        var zones = new[] { MakeFace(10f, 10f), MakeFace(10f, 10f) };
        var rng = new System.Random(4242);
        var budget = new StationDecorator.ModuleZoneBudget { TankFarmRemaining = 1 };

        var types = StationDecorator.AssignZoneTypes("docking-bay", zones, rng, budget);

        Assert.Equal(2, types.Length);
        Assert.Contains(StationDecorator.ZoneType.TankFarm,   types);
        Assert.Contains(StationDecorator.ZoneType.Structural, types);
        Assert.DoesNotContain(StationDecorator.ZoneType.PipeCorridor, types);
        Assert.DoesNotContain(StationDecorator.ZoneType.CommsArray,   types);
        Assert.DoesNotContain(StationDecorator.ZoneType.Signage,      types);
        Assert.Equal(0, budget.TankFarmRemaining);
        Assert.True(budget.NeedsPipeCorridor);
        Assert.True(budget.NeedsCommsArray);
        Assert.True(budget.NeedsSignage);
    }

    [Fact]
    public void AssignZoneTypes_IsDeterministic()
    {
        var face = MakeFace(239.47f, 239.47f);

        var rngA = new System.Random(2468);
        var (zonesA, _) = StationDecorator.ComputeZones(face, rngA);
        var typesA = StationDecorator.AssignZoneTypes("docking-bay", zonesA, rngA, new StationDecorator.ModuleZoneBudget());

        var rngB = new System.Random(2468);
        var (zonesB, _) = StationDecorator.ComputeZones(face, rngB);
        var typesB = StationDecorator.AssignZoneTypes("docking-bay", zonesB, rngB, new StationDecorator.ModuleZoneBudget());

        Assert.Equal(typesA, typesB);
    }

    // Brief Z2 Part 3's adjacency bias is best-effort, not a hard constraint — this checks
    // it actually fires at a real, nonzero rate across many seeds (the number itself is
    // reported, not asserted against a specific threshold beyond ">0").
    [Fact]
    public void AssignZoneTypes_PipeCorridor_OftenLandsAdjacentToTankFarm()
    {
        var face = MakeFace(239.47f, 239.47f);
        int withPipeCorridor = 0, adjacentToTankFarm = 0;

        for (int seed = 0; seed < 200; seed++)
        {
            var rng = new System.Random(seed);
            var (zones, _) = StationDecorator.ComputeZones(face, rng);
            var budget = new StationDecorator.ModuleZoneBudget();
            var types = StationDecorator.AssignZoneTypes("docking-bay", zones, rng, budget);

            int pipeIdx = Array.IndexOf(types, StationDecorator.ZoneType.PipeCorridor);
            if (pipeIdx < 0) continue;
            withPipeCorridor++;

            for (int i = 0; i < types.Length; i++)
            {
                if (types[i] != StationDecorator.ZoneType.TankFarm) continue;
                if (StationDecorator.ZonesAreAdjacent(zones[pipeIdx], zones[i]))
                {
                    adjacentToTankFarm++;
                    break;
                }
            }
        }

        Assert.True(withPipeCorridor > 0, "Expected at least some seeds to produce a PipeCorridor zone");
        Assert.True(adjacentToTankFarm > 0,
            $"Expected the adjacency bias to succeed at least sometimes, got {adjacentToTankFarm}/{withPipeCorridor}");
    }
}
