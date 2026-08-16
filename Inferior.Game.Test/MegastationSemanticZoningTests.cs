using Inferior.Game.StationGen;
using Inferior.Game.StationGen.Megastations;
using Microsoft.Xna.Framework;
using Xunit;

namespace Inferior.Game.Test;

public sealed class MegastationSemanticZoningTests
{
    private const string NovaAnchorageId = "Oranae:Oranae I:Nova Anchorage";

    [Fact]
    public void SharedBoundaryTopologyProducesIdenticalMeshArrays()
    {
        MegastationPrototypeCpuResult result = MegastationPrototypeGenerator.GenerateCpu(NovaAnchorageId);
        var rebuilt = new StationModuleMesh();

        MegastationMeshStats stats = MegastationPrototypeMeshBuilder.Build(
            result.RegularisedOccupancy,
            result.BoundaryTopology,
            rebuilt);
        var (expectedVertices, expectedIndices) = result.Mesh.ToIntArrays();
        var (actualVertices, actualIndices) = rebuilt.ToIntArrays();

        Assert.Equal(result.Diagnostics.BoundaryTopologySignature, stats.TopologySignature.Semantic);
        Assert.Equal(expectedVertices, actualVertices);
        Assert.Equal(expectedIndices, actualIndices);
    }

    [Fact]
    public void ZoningIsDeterministicForSameFinalTopology()
    {
        MegastationPrototypeCpuResult result = MegastationPrototypeGenerator.GenerateCpu(NovaAnchorageId);
        MegastationSemanticZoningResult a = result.SemanticZoning;
        MegastationSemanticZoningResult b = MegastationSemanticZoningBuilder.Build(
            result.Diagnostics.RootSeed,
            result.RegularisedOccupancy,
            result.BoundaryTopology,
            result.Faces);

        Assert.Equal(a.Anchors, b.Anchors);
        Assert.Equal(
            a.Surfaces.Select(SurfaceSignature),
            b.Surfaces.Select(SurfaceSignature));
        Assert.Equal(
            a.Zones.Select(ZoneSignature),
            b.Zones.Select(ZoneSignature));
        Assert.Equal(
            a.DebugIndexGroups.Select(group => (group.Role, Indices: string.Join(',', group.Indices))),
            b.DebugIndexGroups.Select(group => (group.Role, Indices: string.Join(',', group.Indices))));
        Assert.Equal(
            a.Diagnostics.AreaByRole.OrderBy(pair => pair.Key),
            b.Diagnostics.AreaByRole.OrderBy(pair => pair.Key));
        Assert.Equal(a.Diagnostics.ZoneCount, b.Diagnostics.ZoneCount);
        Assert.Equal(a.Diagnostics.SurfaceFaceCount, b.Diagnostics.SurfaceFaceCount);
        Console.WriteLine($"{ZoningSummary(a.Diagnostics)}; debugGroups={a.DebugIndexGroups.Count(group => group.Indices.Count > 0)}");
    }

    [Fact]
    public void FaceGrownTowerSideWallRetainsSourceDistrictDistinctFromSurfaceNormal()
    {
        var grid = Grid([10f, 10f, 10f], [10f, 10f, 10f], [10f, 10f, 10f], 1..2, 1..2, 1..2);
        var occupancy = new StructuralOccupancy(grid);
        occupancy.FillCore();
        occupancy.MarkUrban(1, 2, 1, MegacellOwner.FaceInterior, RegionIdentity.Face(GridDirection.PositiveY));
        var growth = FaceGrowth(GridDirection.PositiveY, GridAxis.X, GridAxis.Z,
            new UrbanDistrict(3, 1, 1, 1, 1, 1, 1));
        BoundaryTopology topology = BoundaryTopologyBuilder.Build(occupancy, MegastationPrototypeSettings.Default);

        MegastationSemanticZoningResult zoning = MegastationSemanticZoningBuilder.Build(123, occupancy, topology, [growth]);
        BoundaryFaceKey sideWall = new(1, 2, 1, GridDirection.PositiveX);
        MegastationSemanticSurface surface = zoning.SurfaceByFace[sideWall];

        Assert.Equal(GridDirection.PositiveX, surface.Face.Direction);
        Assert.Equal(GridDirection.PositiveY, surface.SourceFace);
        Assert.Equal("face.+y/district:03", surface.StructuralAnchorIdentity);
        Assert.Equal(MegastationStructuralAnchorKind.FaceDistrict, zoning.ZoneByFace[sideWall].Anchor.Kind);
    }

    [Fact]
    public void EdgeCornerAndCoreOwnershipProduceStableAnchorKinds()
    {
        var grid = Grid([1f, 1f, 1f], [1f, 1f, 1f], [1f, 1f, 1f], 1..2, 1..2, 1..2);
        var occupancy = new StructuralOccupancy(grid);
        occupancy.FillCore();
        occupancy.MarkUrban(2, 2, 1, MegacellOwner.EdgeRegion, RegionIdentity.Edge(GridDirection.PositiveX, GridDirection.PositiveY));
        occupancy.MarkUrban(2, 2, 2, MegacellOwner.CornerRegion, RegionIdentity.Corner(
            GridDirection.PositiveX, GridDirection.PositiveY, GridDirection.PositiveZ));
        BoundaryTopology topology = BoundaryTopologyBuilder.Build(occupancy, MegastationPrototypeSettings.Default);

        MegastationSemanticZoningResult zoning = MegastationSemanticZoningBuilder.Build(456, occupancy, topology, []);

        Assert.Contains(zoning.Anchors, anchor => anchor.Kind == MegastationStructuralAnchorKind.CoreComponent);
        Assert.Contains(zoning.Anchors, anchor => anchor.Kind == MegastationStructuralAnchorKind.EdgeRegion
            && anchor.Identity == "edge.+x.+y");
        Assert.Contains(zoning.Anchors, anchor => anchor.Kind == MegastationStructuralAnchorKind.CornerRegion
            && anchor.Identity == "corner.+x.+y.+z");
    }

    [Fact]
    public void OneStructuralAnchorHasExactlyOneZoneAndRole()
    {
        MegastationPrototypeCpuResult result = MegastationPrototypeGenerator.GenerateCpu("semantic-coherence-fixture");
        MegastationSemanticZoningResult zoning = result.SemanticZoning;

        Assert.Equal(zoning.Anchors.Count, zoning.Zones.Count);
        Assert.All(
            zoning.Surfaces.GroupBy(surface => surface.StructuralAnchorIdentity),
            group => Assert.Single(zoning.Zones, zone => zone.Anchor.Identity == group.Key));
        Assert.All(zoning.Zones, zone =>
            Assert.All(zone.Faces, face => Assert.Same(zone, zoning.ZoneByFace[face])));
    }

    [Fact]
    public void PhysicalAreaUsesNonUniformGridDimensionsRatherThanFaceCount()
    {
        var grid = Grid([1f], [2f], [10f], 0..1, 0..1, 0..1);
        var occupancy = new StructuralOccupancy(grid);
        occupancy.FillCore();
        BoundaryTopology topology = BoundaryTopologyBuilder.Build(occupancy, MegastationPrototypeSettings.Default);

        MegastationSemanticZoningResult zoning = MegastationSemanticZoningBuilder.Build(789, occupancy, topology, []);

        Assert.Equal(20f, zoning.SurfaceByFace[new(0, 0, 0, GridDirection.PositiveX)].Metrics.PhysicalArea);
        Assert.Equal(10f, zoning.SurfaceByFace[new(0, 0, 0, GridDirection.PositiveY)].Metrics.PhysicalArea);
        Assert.Equal(2f, zoning.SurfaceByFace[new(0, 0, 0, GridDirection.PositiveZ)].Metrics.PhysicalArea);
        Assert.Equal(64f, zoning.Diagnostics.TotalSurfaceArea);
        Assert.Equal(64f, zoning.Diagnostics.AreaByRole.Values.Sum());
    }

    [Fact]
    public void FragmentSelectionUsesPhysicalLengthThenOrdinalTieBreak()
    {
        Assert.Equal("wide", MegastationSemanticZoningBuilder.SelectFragmentAnchor(
            new Dictionary<string, float> { ["narrow"] = 4f, ["wide"] = 12f }));
        Assert.Equal("alpha", MegastationSemanticZoningBuilder.SelectFragmentAnchor(
            new Dictionary<string, float> { ["zeta"] = 8f, ["alpha"] = 8f }));
    }

    [Fact]
    public void TopologyBiasesMoveWeightsInExpectedDirections()
    {
        MegastationSurfaceMetrics baseline = Metrics();
        MegastationSurfaceMetrics prominent = Metrics(prominence: 0.95f, localHeight: 0.4f, exposure: 0.5f);
        MegastationSurfaceMetrics recessed = Metrics(prominence: 0.05f, concavity: 0.75f);
        MegastationSurfaceMetrics broad = Metrics(area: 20f, planarArea: 400f);
        MegastationSurfaceMetrics extreme = Metrics(prominence: 0.8f, extremity: 1f, exposure: 0.66f);

        var baseWeights = MegastationSemanticZoningBuilder.RoleWeights(MegastationStructuralAnchorKind.FaceDistrict, baseline);
        var highWeights = MegastationSemanticZoningBuilder.RoleWeights(MegastationStructuralAnchorKind.FaceDistrict, prominent);
        var recessWeights = MegastationSemanticZoningBuilder.RoleWeights(MegastationStructuralAnchorKind.FaceDistrict, recessed);
        var broadWeights = MegastationSemanticZoningBuilder.RoleWeights(MegastationStructuralAnchorKind.FaceDistrict, broad);
        var extremeWeights = MegastationSemanticZoningBuilder.RoleWeights(MegastationStructuralAnchorKind.FaceDistrict, extreme);

        Assert.True(highWeights[MegastationZoneRole.Habitation] > baseWeights[MegastationZoneRole.Habitation]);
        Assert.True(recessWeights[MegastationZoneRole.Utilities] > baseWeights[MegastationZoneRole.Utilities]);
        Assert.True(broadWeights[MegastationZoneRole.Industrial] > baseWeights[MegastationZoneRole.Industrial]);
        Assert.True(broadWeights[MegastationZoneRole.Logistics] > baseWeights[MegastationZoneRole.Logistics]);
        Assert.True(extremeWeights[MegastationZoneRole.Strategic] > baseWeights[MegastationZoneRole.Strategic]);
    }

    [Fact]
    public void CapabilitiesContainNoUnsupportedClearanceDockingOrDefenceClaims()
    {
        string[] capabilityNames = Enum.GetNames<MegastationZoneCapabilities>();

        Assert.DoesNotContain("AttachmentSuitable", capabilityNames);
        Assert.DoesNotContain("ClearanceVerified", capabilityNames);
        Assert.DoesNotContain("DockingCandidate", capabilityNames);
        Assert.DoesNotContain("DefenceCapable", capabilityNames);
    }

    [Fact]
    public void DebugIndexGroupsCoverEverySemanticBoundaryFaceExactlyOnce()
    {
        MegastationPrototypeCpuResult result = MegastationPrototypeGenerator.GenerateCpu("semantic-debug-range-fixture");
        MegastationSemanticZoningResult zoning = result.SemanticZoning;
        var (_, productionIndices) = result.Mesh.ToIntArrays();
        var expectedByRole = Enum.GetValues<MegastationZoneRole>()
            .ToDictionary(role => role, _ => new List<int>());
        for (int face = 0; face < result.BoundaryTopology.Faces.Count; face++)
        {
            MegastationZoneRole role = zoning.ZoneByFace[result.BoundaryTopology.Faces[face].Key].Role;
            expectedByRole[role].AddRange(productionIndices.Skip(face * 6).Take(6));
        }

        int[] groupedIndices = zoning.DebugIndexGroups.SelectMany(group => group.Indices).ToArray();
        Assert.Equal(result.BoundaryTopology.Faces.Count * 6, groupedIndices.Length);
        Assert.All(groupedIndices, index =>
            Assert.InRange(index, 0, result.BoundaryTopology.Faces.Count * 4 - 1));
        Assert.Equal(
            result.BoundaryTopology.Faces.Count * 2,
            zoning.DebugIndexGroups.Sum(group => group.Indices.Count / 3));
        Assert.All(zoning.DebugIndexGroups, group =>
            Assert.Equal(expectedByRole[group.Role], group.Indices));
    }

    private static UrbanGrowthResult FaceGrowth(
        GridDirection direction,
        GridAxis uAxis,
        GridAxis vAxis,
        params UrbanDistrict[] districts)
        => new()
        {
            Patch = new SurfacePatch
            {
                Id = $"test-{direction}",
                Direction = direction,
                PlaneIndex = 1,
                Cells = [new SurfaceCell(1, 1, 1)],
                UAxis = uAxis,
                VAxis = vAxis,
                MinU = 1,
                MaxU = 1,
                MinV = 1,
                MaxV = 1,
            },
            Depths = new int[1, 1],
            Districts = districts,
        };

    private static SliceGrid Grid(
        float[] x,
        float[] y,
        float[] z,
        Range coreX,
        Range coreY,
        Range coreZ)
        => new(x, y, z, coreX, coreY, coreZ);

    private static MegastationSurfaceMetrics Metrics(
        float area = 10f,
        float prominence = 0.4f,
        float extremity = 0.4f,
        float exposure = 0.2f,
        float localHeight = 0f,
        float planarArea = 40f,
        float concavity = 0f)
        => new(
            area,
            Vector3.Zero,
            prominence,
            extremity,
            exposure,
            1f - prominence,
            localHeight,
            planarArea,
            concavity);

    private static object SurfaceSignature(MegastationSemanticSurface surface) => new
    {
        surface.Face,
        surface.StructuralAnchorIdentity,
        surface.SourceFace,
        surface.Metrics,
        Adjacent = string.Join('|', surface.AdjacentFaces),
        Coplanar = string.Join('|', surface.CoplanarNeighbours),
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

    private static string ZoningSummary(MegastationSemanticZoningDiagnostics diagnostics)
        => $"faces={diagnostics.SurfaceFaceCount}; zones={diagnostics.ZoneCount}; "
         + $"area={diagnostics.TotalSurfaceArea:F1}; zoningMs={diagnostics.ZoningMilliseconds}; "
         + string.Join("; ", diagnostics.AreaByRole.OrderBy(pair => pair.Key).Select(pair =>
             $"{pair.Key}={pair.Value / diagnostics.TotalSurfaceArea * 100f:F1}%"))
         + $"; anchors={diagnostics.CoreAnchorCount}/{diagnostics.FaceDistrictAnchorCount}/"
         + $"{diagnostics.EdgeAnchorCount}/{diagnostics.CornerAnchorCount}; "
         + $"repairs={diagnostics.RepairFragmentCount}/{diagnostics.FragmentsMerged}";
}
