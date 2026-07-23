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

        int moduleBudget = 5;
        int totalWindowZones = 0;
        // Simulate several faces of one module sharing the same ref budget.
        for (int f = 0; f < 4; f++)
        {
            var types = StationDecorator.AssignZoneTypes("docking-bay", zones, rng, ref moduleBudget);
            int thisFace = types.Count(t => t == StationDecorator.ZoneType.Windows);
            Assert.True(thisFace <= 3, $"Expected <=3 window zones per face, got {thisFace}");
            totalWindowZones += thisFace;
        }

        Assert.True(totalWindowZones <= 5, $"Expected <=5 window zones per module, got {totalWindowZones}");
        Assert.True(moduleBudget >= 0);
    }

    // CommsArray/PipeCorridor are reserved for Z2 — Z1 must never assign them from the
    // weight table (they intentionally have no entry in ZoneTypeWeights).
    [Theory]
    [InlineData("industrial")]
    [InlineData("docking-bay")]
    [InlineData("cargo")]
    [InlineData("hab")]
    [InlineData("science")]
    [InlineData("core")]
    [InlineData("connector")]
    [InlineData("some-unlisted-category")]
    public void AssignZoneTypes_NeverAssignsZ2ReservedTypes(string category)
    {
        var rng = new System.Random(31337);
        var face = MakeFace(239.47f, 239.47f);
        var (zones, _) = StationDecorator.ComputeZones(face, rng);
        int budget = 5;

        var types = StationDecorator.AssignZoneTypes(category, zones, rng, ref budget);

        Assert.DoesNotContain(StationDecorator.ZoneType.CommsArray,   types);
        Assert.DoesNotContain(StationDecorator.ZoneType.PipeCorridor, types);
    }

    // Per Timo: tanks and pipes should dominate industrial/bay categories. Statistical
    // check over many zones/seeds rather than a single roll, since PickWeightedZoneType is
    // randomized per zone.
    [Fact]
    public void AssignZoneTypes_IndustrialCategory_MachineryAndTankFarmDominate()
    {
        var face = MakeFace(239.47f, 239.47f);
        var counts = new Dictionary<StationDecorator.ZoneType, int>();

        for (int seed = 0; seed < 50; seed++)
        {
            var rng = new System.Random(seed);
            var (zones, _) = StationDecorator.ComputeZones(face, rng);
            int budget = 5;
            var types = StationDecorator.AssignZoneTypes("industrial", zones, rng, ref budget);
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
    // category's weight table.
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
            int budget = 5;
            var types = StationDecorator.AssignZoneTypes(category, zones, rng, ref budget);
            structuralCount += types.Count(t => t == StationDecorator.ZoneType.Structural);
        }

        Assert.True(structuralCount > 0, $"Expected at least some Structural zones for category '{category}'");
    }
}
