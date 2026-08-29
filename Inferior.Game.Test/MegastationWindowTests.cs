using Inferior.Game.StationGen;
using Inferior.Game.StationGen.Megastations;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Xunit;

namespace Inferior.Game.Test;

public sealed class MegastationWindowTests
{
    private const string NovaAnchorageId = "Oranae:Oranae I:Nova Anchorage";

    [Fact]
    public void RegionExtractionUsesZoneDirectionPlaneAndCanonicalEdgeConnectivity()
    {
        var grid = Grid([10f, 12f, 14f, 16f], [8f, 9f], [11f, 13f]);
        var occupancy = new StructuralOccupancy(grid);
        occupancy.MarkUrban(0, 0, 0, regionId: "hab");
        occupancy.MarkUrban(1, 0, 0, regionId: "hab");
        occupancy.MarkUrban(3, 0, 0, regionId: "hab");
        occupancy.MarkUrban(2, 1, 1, regionId: "hab");
        BoundaryTopology topology = BoundaryTopologyBuilder.Build(occupancy, MegastationPrototypeSettings.Default);
        BoundaryFaceKey a = new(0, 0, 0, GridDirection.PositiveZ);
        BoundaryFaceKey b = new(1, 0, 0, GridDirection.PositiveZ);
        BoundaryFaceKey disconnected = new(3, 0, 0, GridDirection.PositiveZ);
        BoundaryFaceKey otherPlane = new(2, 1, 1, GridDirection.PositiveZ);
        BoundaryFaceKey otherDirection = new(0, 0, 0, GridDirection.NegativeX);
        MegastationSemanticZoningResult zoning = Zoning(
            ("hab-a", MegastationZoneRole.Habitation, GridDirection.PositiveY,
                new[] { a, b, disconnected, otherPlane, otherDirection }));

        MegastationPlanarSurfaceRegion[] regions = MegastationWindowPlanner.ExtractRegions(grid, topology, zoning);

        Assert.Equal(4, regions.Length);
        Assert.Contains(regions, region => region.Faces.SequenceEqual(new[] { a, b }));
        Assert.Contains(regions, region => region.Faces.SequenceEqual(new[] { disconnected }));
        Assert.Contains(regions, region => region.Faces.SequenceEqual(new[] { otherPlane }));
        Assert.Contains(regions, region => region.Faces.SequenceEqual(new[] { otherDirection }));

        MegastationSemanticZoningResult splitZoning = Zoning(
            ("hab-a", MegastationZoneRole.Habitation, GridDirection.PositiveY, new[] { a }),
            ("hab-b", MegastationZoneRole.Habitation, GridDirection.PositiveY, new[] { b }));
        Assert.Equal(2, MegastationWindowPlanner.ExtractRegions(grid, topology, splitZoning).Length);
    }

    [Fact]
    public void ExactMaskRejectsOutsideHoleStepAndMarginViolations()
    {
        MegastationPlanarSurfaceRegion region = MaskRegion(
            (new(0, 0, 0, GridDirection.PositiveZ), 0f, 10f, 0f, 10f),
            (new(1, 0, 0, GridDirection.PositiveZ), 10f, 20f, 0f, 4f),
            (new(1, 1, 0, GridDirection.PositiveZ), 10f, 20f, 6f, 10f));

        Assert.True(MegastationWindowPlanner.ContainsFootprint(region, 5f, 5f, 2f, 2f, 0.25f));
        Assert.False(MegastationWindowPlanner.ContainsFootprint(region, 19.5f, 9f, 2f, 1f, 0.25f));
        Assert.False(MegastationWindowPlanner.ContainsFootprint(region, 12f, 5f, 2f, 2f, 0.25f));
        Assert.False(MegastationWindowPlanner.ContainsFootprint(region, 10f, 5f, 4f, 4f, 0.25f));
        Assert.False(MegastationWindowPlanner.ContainsFootprint(region, 1f, 1f, 2f, 2f, 0.25f));
    }

    [Fact]
    public void SourceFaceEligibilityWorksForAllSixDirectionsWithoutWorldUp()
    {
        foreach (GridDirection source in Enum.GetValues<GridDirection>())
        {
            GridAxis sourceAxis = Direction.PrimaryAxis(source);
            foreach (GridDirection surface in Enum.GetValues<GridDirection>())
            {
                bool expected = Direction.PrimaryAxis(surface) != sourceAxis;
                Assert.Equal(expected, MegastationWindowPlanner.IsEligibleWall(source, surface));
            }
        }
    }

    [Fact]
    public void NovaPlanIsDeterministicAndTraversalIndependent()
    {
        MegastationPrototypeCpuResult result = MegastationPrototypeGenerator.GenerateCpu(NovaAnchorageId);
        MegastationWindowPlan a = result.WindowPlan;
        MegastationSemanticZoningResult reversed = Reverse(result.SemanticZoning);
        MegastationWindowPlan b = MegastationWindowPlanner.Plan(result.Grid, result.BoundaryTopology, reversed);
        MegastationWindowMeshBuildResult rebuilt = MegastationWindowMeshBuilder.Build(b);

        Assert.Equal(a.Regions.Select(RegionSignature), b.Regions.Select(RegionSignature));
        Assert.Equal(a.Blocks, b.Blocks);
        Assert.Equal(a.Windows, b.Windows);
        Assert.Equal(result.WindowGlassMesh.ToIntArrays().verts, rebuilt.Mesh.ToIntArrays().verts);
        Assert.Equal(result.WindowGlassMesh.ToIntArrays().indices, rebuilt.Mesh.ToIntArrays().indices);
    }

    [Fact]
    public void WindowScaleRemainsHumanSizedOnNonUniformGrid()
    {
        MegastationPrototypeCpuResult result = MegastationPrototypeGenerator.GenerateCpu(NovaAnchorageId);

        Assert.NotEmpty(result.WindowPlan.Windows);
        Assert.All(result.WindowPlan.Windows, window =>
        {
            Assert.InRange(window.Width, 1.14f, 2.4751f);
            Assert.InRange(window.Height, 1.14f, 2.4751f);
        });
    }

    [Fact]
    public void DarkRegionsAndNonHabitationExclusionAreExplicit()
    {
        var grid = Grid([80f], [80f], [10f]);
        var occupancy = new StructuralOccupancy(grid);
        occupancy.MarkUrban(0, 0, 0, regionId: "fixture");
        BoundaryTopology topology = BoundaryTopologyBuilder.Build(occupancy, MegastationPrototypeSettings.Default);
        BoundaryFaceKey wall = new(0, 0, 0, GridDirection.PositiveZ);
        MegastationSemanticZoningResult habitation = Zoning(
            ("hab", MegastationZoneRole.Habitation, GridDirection.PositiveY, new[] { wall }));
        MegastationWindowTuning allDark = MegastationWindowTuning.Default with { ActiveRegionProbability = 0f };

        MegastationWindowPlan dark = MegastationWindowPlanner.Plan(grid, topology, habitation, allDark);
        Assert.Single(dark.Regions);
        Assert.Equal(1, dark.Diagnostics.DarkRegionCount);
        Assert.Empty(dark.Windows);

        MegastationSemanticZoningResult structural = Zoning(
            ("structural", MegastationZoneRole.Structural, GridDirection.PositiveY, new[] { wall }));
        MegastationWindowPlan excluded = MegastationWindowPlanner.Plan(grid, topology, structural);
        Assert.Empty(excluded.Regions);
        Assert.Empty(excluded.Windows);
    }

    [Fact]
    public void ActiveLayoutContainsBlocksSeparatorsAndAbsentCandidates()
    {
        var grid = Grid([120f], [120f], [10f]);
        var occupancy = new StructuralOccupancy(grid);
        occupancy.MarkUrban(0, 0, 0, regionId: "fixture");
        BoundaryTopology topology = BoundaryTopologyBuilder.Build(occupancy, MegastationPrototypeSettings.Default);
        BoundaryFaceKey wall = new(0, 0, 0, GridDirection.PositiveZ);
        MegastationSemanticZoningResult zoning = Zoning(
            ("hab", MegastationZoneRole.Habitation, GridDirection.PositiveY, new[] { wall }));
        MegastationWindowTuning tuning = MegastationWindowTuning.Default with
        {
            ActiveRegionProbability = 1f,
            MissingBlockProbability = 0f,
            AbsentProbability = 0.5f,
        };

        MegastationWindowPlan plan = MegastationWindowPlanner.Plan(grid, topology, zoning, tuning);

        Assert.NotEmpty(plan.Blocks);
        Assert.NotEmpty(plan.Windows);
        Assert.True(plan.Diagnostics.AbsentCandidateCount > 0);
        Assert.True(plan.Diagnostics.ActiveWindowArea < plan.Diagnostics.EligibleHabitationWallArea);
        Assert.Contains(plan.Blocks, a => plan.Blocks.Any(b =>
            a.RegionIdentity == b.RegionIdentity && a != b &&
            (b.MinU > a.MaxU || b.MinV > a.MaxV)));
    }

    [Fact]
    public void MeshBuilderEmitsExactlyOneQuadPerPresentWindowWithoutGraphicsDevice()
    {
        MegastationWindowPlan plan = PlanWithWindows(7);

        MegastationWindowMeshBuildResult result = MegastationWindowMeshBuilder.Build(plan);

        Assert.Equal(28, result.Mesh.VertexCount);
        Assert.Equal(42, result.Mesh.IndexCount);
        Assert.Equal(14, result.Diagnostics.MeshTriangleCount);
        Assert.DoesNotContain(
            typeof(MegastationWindowPlanner).GetMethods().Concat(typeof(MegastationWindowMeshBuilder).GetMethods()),
            method => method.GetParameters().Any(parameter => parameter.ParameterType == typeof(GraphicsDevice)));
    }

    [Fact]
    public void Z2aLeavesStructuralAndZoningOutputsUnchanged()
    {
        MegastationPrototypeCpuResult result = MegastationPrototypeGenerator.GenerateCpu(NovaAnchorageId);
        string completeBefore = MegastationMassingSignatureBuilder.Compute(result).Complete;
        string structuralBefore = MegastationMassingSignatureBuilder.ComputeStructuralSolid(result).Body;
        var rebuilt = new StationModuleMesh();
        MegastationMeshStats rebuiltStats = MegastationPrototypeMeshBuilder.Build(
            result.RegularisedOccupancy,
            result.BoundaryTopology,
            rebuilt,
            interiorPlan: result.InteriorPlan);
        MegastationSemanticZoningResult rebuiltZoning = MegastationSemanticZoningBuilder.Build(
            result.Diagnostics.RootSeed, result.RegularisedOccupancy, result.BoundaryTopology, result.Faces);

        Assert.Equal(completeBefore, MegastationMassingSignatureBuilder.Compute(result).Complete);
        Assert.Equal(structuralBefore, MegastationMassingSignatureBuilder.ComputeStructuralSolid(result).Body);
        Assert.Equal(result.Diagnostics.BoundaryTopologySignature, rebuiltStats.TopologySignature.Semantic);
        Assert.Equal(result.Mesh.ToIntArrays().verts, rebuilt.ToIntArrays().verts);
        Assert.Equal(result.Mesh.ToIntArrays().indices, rebuilt.ToIntArrays().indices);
        Assert.Equal(
            result.SemanticZoning.Zones.Select(ZoneSignature),
            rebuiltZoning.Zones.Select(ZoneSignature));
        Assert.Equal(result.WindowPlan.Windows.Count * 4, result.WindowGlassMesh.VertexCount);
        Assert.Equal(result.WindowPlan.Windows.Count * 6, result.WindowGlassMesh.IndexCount);
        Console.WriteLine(WindowSummary(result.WindowPlan.Diagnostics));
    }

    private static SliceGrid Grid(float[] x, float[] y, float[] z)
        => new(x, y, z, 0..0, 0..0, 0..0);

    private static MegastationSemanticZoningResult Zoning(
        params (string identity, MegastationZoneRole role, GridDirection source, BoundaryFaceKey[] faces)[] specifications)
    {
        MegastationSemanticZone[] zones = specifications.Select((spec, index) =>
        {
            var anchor = new MegastationStructuralAnchor(spec.identity, MegastationStructuralAnchorKind.FaceDistrict, spec.source);
            return new MegastationSemanticZone(
                spec.identity, anchor, spec.role, MegastationZoneCapabilities.None, spec.faces,
                spec.faces.Length, Vector3.Zero, Vector3.One, Metrics(), 1000 + index);
        }).ToArray();
        var surfaces = zones.SelectMany(zone => zone.Faces.Select(face => new MegastationSemanticSurface(
            face, zone.Anchor.Identity, zone.Anchor.SourceFace, Metrics(), [], []))).ToArray();
        return new MegastationSemanticZoningResult
        {
            Anchors = zones.Select(zone => zone.Anchor).ToArray(),
            Surfaces = surfaces,
            SurfaceByFace = surfaces.ToDictionary(surface => surface.Face),
            Zones = zones,
            ZoneByFace = zones.SelectMany(zone => zone.Faces.Select(face => (face, zone))).ToDictionary(pair => pair.face, pair => pair.zone),
            DebugIndexGroups = [],
            Diagnostics = new(
                surfaces.Length, zones.Length, surfaces.Length, 0, zones.Length, 0, 0, 0, 0, 0,
                Enum.GetValues<MegastationZoneRole>().ToDictionary(role => role, role =>
                    (float)zones.Where(zone => zone.Role == role).Sum(zone => zone.Faces.Count))),
        };
    }

    private static MegastationSurfaceMetrics Metrics() => new(
        1f, Vector3.Zero, 0.5f, 0.5f, 0.5f, 0.5f, 0f, 1f, 0f);

    private static MegastationPlanarSurfaceRegion MaskRegion(
        params (BoundaryFaceKey face, float minU, float maxU, float minV, float maxV)[] rectangles)
    {
        MegastationWindowMaskRect[] mask = rectangles
            .Select(rect => new MegastationWindowMaskRect(rect.face, rect.minU, rect.maxU, rect.minV, rect.maxV))
            .ToArray();
        return new(
            "region", "zone", 1, GridDirection.PositiveZ, GridDirection.PositiveY, 1, 0f,
            Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY,
            rectangles.Select(rect => rect.face).ToArray(), mask,
            mask.Min(rect => rect.MinU), mask.Max(rect => rect.MaxU),
            mask.Min(rect => rect.MinV), mask.Max(rect => rect.MaxV),
            mask.Sum(rect => (rect.MaxU - rect.MinU) * (rect.MaxV - rect.MinV)));
    }

    private static MegastationSemanticZoningResult Reverse(MegastationSemanticZoningResult source)
    {
        MegastationSemanticZone[] zones = source.Zones.Reverse()
            .Select(zone => zone with { Faces = zone.Faces.Reverse().ToArray() })
            .ToArray();
        return new MegastationSemanticZoningResult
        {
            Anchors = source.Anchors.Reverse().ToArray(),
            Surfaces = source.Surfaces.Reverse().ToArray(),
            SurfaceByFace = source.SurfaceByFace,
            Zones = zones,
            ZoneByFace = zones.SelectMany(zone => zone.Faces.Select(face => (face, zone))).ToDictionary(pair => pair.face, pair => pair.zone),
            DebugIndexGroups = source.DebugIndexGroups,
            Diagnostics = source.Diagnostics,
        };
    }

    private static object RegionSignature(MegastationPlanarSurfaceRegion region) => new
    {
        region.Identity,
        Faces = string.Join('|', region.Faces),
        Mask = string.Join('|', region.Mask),
    };

    private static object ZoneSignature(MegastationSemanticZone zone) => new
    {
        zone.Identity,
        zone.Anchor,
        zone.Role,
        zone.Capabilities,
        Faces = string.Join('|', zone.Faces),
        zone.TotalPhysicalArea,
        zone.BoundsMin,
        zone.BoundsMax,
        zone.Metrics,
        zone.Seed,
    };

    private static MegastationWindowPlan PlanWithWindows(int count)
    {
        MegastationWindowInstance[] windows = Enumerable.Range(0, count).Select(index => new MegastationWindowInstance(
            $"window:{index}", "region", "block", new Vector3(index * 3f, 0f, 0.05f),
            Vector3.UnitZ, Vector3.UnitY, 1.5f, 1.8f, MegastationWindowState.Lit,
            new Color(255, 250, 220))).ToArray();
        return new([], [], windows, new(0, 0, 0, 0, 0, count, count, 0, 0, 0, 0f, 0f, 0));
    }

    private static string WindowSummary(MegastationWindowDiagnostics diagnostics)
        => $"[MegastationWindows] habitationZones={diagnostics.HabitationZoneCount}; " +
           $"eligibleRegions={diagnostics.EligibleRegionCount}; activeRegions={diagnostics.ActiveRegionCount}; " +
           $"darkRegions={diagnostics.DarkRegionCount}; blocks={diagnostics.BlockCount}; " +
           $"windows={diagnostics.WindowCount}; lit={diagnostics.LitWindowCount}; " +
           $"dim={diagnostics.DimWindowCount}; dark={diagnostics.DarkWindowCount}; " +
           $"absentCandidates={diagnostics.AbsentCandidateCount}; vertices={diagnostics.MeshVertexCount}; " +
           $"triangles={diagnostics.MeshTriangleCount}; bytes={diagnostics.MeshBytes}; " +
           $"planningMs={diagnostics.PlanningMilliseconds}; meshBuildMs={diagnostics.MeshBuildMilliseconds}";
}
