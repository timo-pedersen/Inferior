using Inferior.Game.StationGen;
using Inferior.Game.StationGen.Megastations;
using Inferior.Game.States;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Xunit;

namespace Inferior.Game.Test;

public sealed class MegastationLightingTests
{
    private const string NovaAnchorageId = "Oranae:Oranae I:Nova Anchorage";

    [Theory]
    [InlineData("Gaanis:Gaanis II:Omega Beacon")]
    [InlineData("Enloax:Enloax Vd:Deep Haven")]
    public void RepresentativeMegastationsRetainSmallMostlySteadyClusters(string stationIdentity)
    {
        MegastationLightPlan plan = MegastationPrototypeGenerator.GenerateCpu(stationIdentity).LightPlan;
        float averageLampsPerCluster = plan.Lights.Count / (float)plan.Clusters.Count;

        Assert.True(plan.Lights.Count > plan.Clusters.Count);
        Assert.InRange(averageLampsPerCluster, 1f, 4f);
        Assert.True(plan.Diagnostics.AnimatedLightCount < plan.Lights.Count / 10f);
        Console.WriteLine($"station={stationIdentity}; {LightingSummary(plan.Diagnostics)}; " +
            $"averageLampsPerCluster={averageLampsPerCluster:F3}");
    }

    [Fact]
    public void RoleGatingLeavesStructuralAndHabitationDark()
    {
        (SliceGrid grid, BoundaryTopology topology, BoundaryFaceKey[] faces) = SixFaceFixture();
        MegastationSemanticZoningResult zoning = Zoning(
            ("structural", MegastationZoneRole.Structural, 100_000f, new[] { faces[0] }),
            ("habitation", MegastationZoneRole.Habitation, 100_000f, new[] { faces[1] }),
            ("industrial", MegastationZoneRole.Industrial, 100_000f, new[] { faces[2] }),
            ("logistics", MegastationZoneRole.Logistics, 100_000f, new[] { faces[3] }),
            ("utilities", MegastationZoneRole.Utilities, 100_000f, new[] { faces[4] }),
            ("strategic", MegastationZoneRole.Strategic, 100_000f, new[] { faces[5] }));

        MegastationLightPlan plan = MegastationLightingPlanner.Plan(grid, topology, zoning);

        Assert.DoesNotContain(plan.Lights, light => light.Role is MegastationZoneRole.Structural or MegastationZoneRole.Habitation);
        Assert.Contains(plan.Lights, light => light.Role == MegastationZoneRole.Industrial);
        Assert.Contains(plan.Lights, light => light.Role == MegastationZoneRole.Logistics);
        Assert.Contains(plan.Lights, light => light.Role == MegastationZoneRole.Utilities);
        Assert.Contains(plan.Lights, light => light.Role == MegastationZoneRole.Strategic);
    }

    [Fact]
    public void NovaLightingIsDeterministicAndTraversalIndependent()
    {
        MegastationPrototypeCpuResult result = MegastationPrototypeGenerator.GenerateCpu(NovaAnchorageId);
        MegastationLightPlan a = result.LightPlan;
        MegastationLightPlan b = MegastationLightingPlanner.Plan(
            result.Grid,
            result.BoundaryTopology,
            Reverse(result.SemanticZoning));
        b = MegastationAttachmentPlanner.SuppressLights(
            b,
            result.AttachmentPlan.Reservations,
            out _);

        Assert.Equal(a.Regions.Select(RegionSignature), b.Regions.Select(RegionSignature));
        Assert.Equal(a.Clusters, b.Clusters);
        Assert.Equal(a.Lights, b.Lights);
    }

    [Fact]
    public void LightsLieOnSelectedExteriorFacesWithNormalOnlyGlowOffset()
    {
        (SliceGrid grid, BoundaryTopology topology, BoundaryFaceKey[] faces) = SixFaceFixture();
        MegastationSemanticZoningResult zoning = Zoning(
            ("industrial", MegastationZoneRole.Industrial, 100_000f, new[] { faces[0] }),
            ("logistics", MegastationZoneRole.Logistics, 100_000f, new[] { faces[1] }),
            ("utilities", MegastationZoneRole.Utilities, 100_000f, new[] { faces[2] }),
            ("strategic-a", MegastationZoneRole.Strategic, 100_000f, new[] { faces[3] }),
            ("strategic-b", MegastationZoneRole.Strategic, 100_000f, new[] { faces[4] }),
            ("strategic-c", MegastationZoneRole.Strategic, 100_000f, new[] { faces[5] }));

        MegastationLightPlan plan = MegastationLightingPlanner.Plan(grid, topology, zoning);

        Assert.Equal(6, plan.Regions.Select(region => region.Direction).Distinct().Count());
        Assert.All(plan.Lights, light =>
        {
            BoundaryFace face = topology.FaceByKey[light.SurfaceFace];
            Vector3 expectedNormal = BoundaryTopologyBuilder.Normal(face.Direction);
            Vector3[] vertices = face.Vertices.Select(vertex => BoundaryTopologyBuilder.Position(grid, vertex)).ToArray();
            float plane = Vector3.Dot(vertices[0], expectedNormal);
            Assert.Equal(expectedNormal, light.Normal);
            Assert.InRange(MathF.Abs(Vector3.Dot(light.SurfacePosition, expectedNormal) - plane), 0f, 0.001f);
            AssertVectorNear(light.SurfacePosition + expectedNormal * 0.06f, light.GlowPosition, 0.0001f);

            foreach (GridAxis axis in Enum.GetValues<GridAxis>())
            {
                if (axis == Direction.PrimaryAxis(face.Direction)) continue;
                float coordinate = Component(light.SurfacePosition, axis);
                float minimum = vertices.Min(vertex => Component(vertex, axis));
                float maximum = vertices.Max(vertex => Component(vertex, axis));
                Assert.InRange(coordinate, minimum + 0.749f, maximum - 0.749f);
            }
        });
    }

    [Fact]
    public void DensityScalesByPhysicalAreaAndUtilitiesRemainBelowIndustrial()
    {
        (SliceGrid grid, BoundaryTopology topology, BoundaryFaceKey[] faces) = LargeSixFaceFixture();
        BoundaryFaceKey industrialFace = faces[0];
        BoundaryFaceKey utilitiesFace = faces[1];
        MegastationSemanticZoningResult small = Zoning(
            ("industrial", MegastationZoneRole.Industrial, 100_000f, new[] { industrialFace }),
            ("utilities", MegastationZoneRole.Utilities, 100_000f, new[] { utilitiesFace }));
        MegastationSemanticZoningResult large = Zoning(
            ("industrial", MegastationZoneRole.Industrial, 1_100_000f, new[] { industrialFace }),
            ("utilities", MegastationZoneRole.Utilities, 100_000f, new[] { utilitiesFace }));

        MegastationLightPlan smallPlan = MegastationLightingPlanner.Plan(grid, topology, small);
        MegastationLightPlan largePlan = MegastationLightingPlanner.Plan(grid, topology, large);

        Assert.True(smallPlan.Diagnostics.UtilitiesLightCount < smallPlan.Diagnostics.IndustrialLightCount);
        Assert.True(largePlan.Diagnostics.IndustrialLightCount > smallPlan.Diagnostics.IndustrialLightCount);
        Assert.True(largePlan.Diagnostics.IndustrialClusterCount > smallPlan.Diagnostics.IndustrialClusterCount);
        Assert.InRange(largePlan.Lights.Count, 1, 200);
    }

    [Fact]
    public void EqualPhysicalAreaProducesEqualClusterCountAcrossFacePartitionsAndNonUniformSlices()
    {
        MegastationLightPlan singleFace = PlanIndustrialSurface([1_000f], 1_000_000f);
        MegastationLightPlan uniformSplit = PlanIndustrialSurface([500f, 500f], 1_000_000f);
        MegastationLightPlan nonUniformSplit = PlanIndustrialSurface([250f, 750f], 1_000_000f);

        Assert.Equal(singleFace.Diagnostics.IndustrialClusterCount,
            uniformSplit.Diagnostics.IndustrialClusterCount);
        Assert.Equal(singleFace.Diagnostics.IndustrialClusterCount,
            nonUniformSplit.Diagnostics.IndustrialClusterCount);
    }

    [Fact]
    public void EqualAreaRoleHierarchyFavoursIndustrialAndLogisticsAndBoundsStrategic()
    {
        (SliceGrid grid, BoundaryTopology topology, BoundaryFaceKey[] faces) = LargeSixFaceFixture();
        MegastationSemanticZoningResult zoning = Zoning(
            ("industrial", MegastationZoneRole.Industrial, 1_000_000f, new[] { faces[0] }),
            ("logistics", MegastationZoneRole.Logistics, 1_000_000f, new[] { faces[1] }),
            ("utilities", MegastationZoneRole.Utilities, 1_000_000f, new[] { faces[2] }),
            ("strategic", MegastationZoneRole.Strategic, 1_000_000f, new[] { faces[3] }));

        MegastationLightPlan plan = MegastationLightingPlanner.Plan(grid, topology, zoning);
        MegastationLightPlan cappedStrategic = MegastationLightingPlanner.Plan(
            grid,
            topology,
            Zoning(("strategic-large", MegastationZoneRole.Strategic, 10_000_000f,
                new[] { faces[4] })));

        Assert.True(plan.Diagnostics.IndustrialClusterCount > plan.Diagnostics.UtilitiesClusterCount);
        Assert.True(plan.Diagnostics.LogisticsClusterCount > plan.Diagnostics.UtilitiesClusterCount);
        Assert.True(plan.Diagnostics.IndustrialClusterCount > plan.Diagnostics.StrategicClusterCount);
        Assert.True(plan.Diagnostics.LogisticsClusterCount > plan.Diagnostics.StrategicClusterCount);
        Assert.InRange(cappedStrategic.Diagnostics.StrategicClusterCount, 1,
            MegastationLightingTuning.Default.StrategicMaximumClustersPerZone);
    }

    [Fact]
    public void DenseTuningAddsClustersWithoutInflatingClusterLampCounts()
    {
        (SliceGrid grid, BoundaryTopology topology, BoundaryFaceKey[] faces) = LargeSixFaceFixture();
        MegastationSemanticZoningResult zoning = Zoning(
            ("industrial", MegastationZoneRole.Industrial, 1_000_000f, new[] { faces[0] }));
        MegastationLightingTuning sparse = MegastationLightingTuning.Default with
        {
            IndustrialAreaPerCluster = 320_000f,
        };

        MegastationLightPlan sparsePlan = MegastationLightingPlanner.Plan(grid, topology, zoning, sparse);
        MegastationLightPlan densePlan = MegastationLightingPlanner.Plan(
            grid, topology, zoning, MegastationLightingTuning.Default);
        float sparseAverage = sparsePlan.Lights.Count / (float)sparsePlan.Clusters.Count;
        float denseAverage = densePlan.Lights.Count / (float)densePlan.Clusters.Count;

        Assert.True(densePlan.Clusters.Count > sparsePlan.Clusters.Count * 5);
        Assert.InRange(sparseAverage, 2f, 4f);
        Assert.InRange(denseAverage, 2f, 4f);
        Assert.InRange(MathF.Abs(denseAverage - sparseAverage), 0f, 1f);
    }

    [Fact]
    public void AcceptedClustersRespectRoleSeparationInPhysicalMetres()
    {
        (SliceGrid grid, BoundaryTopology topology, BoundaryFaceKey[] faces) = LargeSixFaceFixture();
        MegastationSemanticZoningResult zoning = Zoning(
            ("industrial", MegastationZoneRole.Industrial, 1_000_000f, new[] { faces[0] }),
            ("logistics", MegastationZoneRole.Logistics, 1_000_000f, new[] { faces[1] }),
            ("utilities", MegastationZoneRole.Utilities, 1_000_000f, new[] { faces[2] }),
            ("strategic", MegastationZoneRole.Strategic, 1_000_000f, new[] { faces[3] }));
        MegastationLightPlan plan = MegastationLightingPlanner.Plan(grid, topology, zoning);

        foreach (IGrouping<string, MegastationLightCluster> zoneClusters in
                 plan.Clusters.GroupBy(cluster => cluster.ZoneIdentity))
        {
            MegastationLightCluster[] clusters = zoneClusters.ToArray();
            float minimum = MinimumSeparation(clusters[0].Role);
            Vector3[] centres = clusters.Select(cluster => ClusterCentre(plan, cluster.Identity)).ToArray();
            for (int i = 0; i < centres.Length; i++)
            for (int j = i + 1; j < centres.Length; j++)
                Assert.True(Vector3.Distance(centres[i], centres[j]) >= minimum - 0.001f);
        }
    }

    [Fact]
    public void StrategicAnimationIsSparseAndOtherRolesAreMostlySteady()
    {
        MegastationPrototypeCpuResult result = MegastationPrototypeGenerator.GenerateCpu(NovaAnchorageId);
        MegastationLightPlan plan = result.LightPlan;

        Assert.InRange(plan.Lights.Count, 500, 800);
        Assert.True(plan.Diagnostics.SteadyLightCount > plan.Diagnostics.AnimatedLightCount);
        Assert.InRange(plan.Diagnostics.AnimatedLightCount, 0, 20);
        Assert.All(
            plan.Lights.Where(light => light.Role is MegastationZoneRole.Industrial or MegastationZoneRole.Logistics),
            light => Assert.Equal(LightPattern.Continuous, light.Pattern));
        Assert.All(
            plan.Lights.Where(light => light.Pattern != LightPattern.Continuous),
            light => Assert.Contains(light.Role, new[] { MegastationZoneRole.Utilities, MegastationZoneRole.Strategic }));
        Assert.All(
            plan.Lights.Where(light => light.Role == MegastationZoneRole.Strategic),
            light => Assert.Equal(GlowType.AviationWarning, light.GlowType));
    }

    [Fact]
    public void Z2bChangesOnlyPureLightMetadata()
    {
        MegastationPrototypeCpuResult result = MegastationPrototypeGenerator.GenerateCpu(NovaAnchorageId);
        MegastationWindowPlan rebuiltWindows = MegastationWindowPlanner.Plan(
            result.Grid, result.BoundaryTopology, result.SemanticZoning);
        MegastationWindowMeshBuildResult rebuiltWindowMesh = MegastationWindowMeshBuilder.Build(rebuiltWindows);
        var rebuiltStructure = new StationModuleMesh();
        MegastationMeshStats rebuiltStructureStats = MegastationPrototypeMeshBuilder.Build(
            result.RegularisedOccupancy, result.BoundaryTopology, rebuiltStructure);
        PlacedModule module = MegastationPrototypeGenerator.CreatePlacedModule(result);

        Assert.Equal(result.WindowPlan.Regions.Select(WindowRegionSignature), rebuiltWindows.Regions.Select(WindowRegionSignature));
        Assert.Equal(result.WindowPlan.Blocks, rebuiltWindows.Blocks);
        Assert.Equal(result.WindowPlan.Windows, rebuiltWindows.Windows);
        Assert.Equal(result.WindowGlassMesh.ToIntArrays().verts, rebuiltWindowMesh.Mesh.ToIntArrays().verts);
        Assert.Equal(result.WindowGlassMesh.ToIntArrays().indices, rebuiltWindowMesh.Mesh.ToIntArrays().indices);
        Assert.Equal(result.Diagnostics.BoundaryTopologySignature, rebuiltStructureStats.TopologySignature.Semantic);
        Assert.Equal(result.Mesh.ToIntArrays().verts, rebuiltStructure.ToIntArrays().verts);
        Assert.Equal(result.Mesh.ToIntArrays().indices, rebuiltStructure.ToIntArrays().indices);
        Assert.Equal(
            result.LightPlan.Lights.Select(light => light.ToStationLightInfo()),
            module.GlowLights);
        Assert.Same(result.InfrastructureMesh, module.Mesh);
        Assert.True(module.HasNativeMegastationInfrastructure);
        Assert.Equal(result.WindowGlassMesh, module.GlassMesh);
        Assert.DoesNotContain(
            typeof(MegastationLightingPlanner).GetMethods(),
            method => method.GetParameters().Any(parameter => parameter.ParameterType == typeof(GraphicsDevice)));
        Console.WriteLine(LightingSummary(result.LightPlan.Diagnostics));
    }

    [Fact]
    public void SurfaceNormalsSurviveHandoffForDepthPresentation()
    {
        MegastationPrototypeCpuResult result = MegastationPrototypeGenerator.GenerateCpu(NovaAnchorageId);
        PlacedModule module = MegastationPrototypeGenerator.CreatePlacedModule(result);

        Assert.Equal(result.LightPlan.Lights.Count, module.GlowLights.Count);
        for (int i = 0; i < result.LightPlan.Lights.Count; i++)
            Assert.Equal(result.LightPlan.Lights[i].Normal, module.GlowLights[i].SurfaceNormal);
    }

    [Fact]
    public void CameraDepthBiasIsBoundedAndBackFacesCannotLeak()
    {
        Vector3 position = new(0f, 0f, -1_620f);

        StationGlowDepthDecision front = SystemSpaceState.ResolveStationGlowDepth(
            position,
            Vector3.UnitZ);
        StationGlowDepthDecision back = SystemSpaceState.ResolveStationGlowDepth(
            position,
            -Vector3.UnitZ);

        Assert.True(front.IsFrontFacing);
        Assert.Equal(SystemSpaceState.MegastationGlowCameraDepthBiasMeters, front.AppliedBiasMeters);
        Assert.Equal(-1_620f + SystemSpaceState.MegastationGlowCameraDepthBiasMeters,
            front.BiasedCameraRelativePosition.Z,
            4);
        Assert.False(back.IsFrontFacing);
        Assert.Equal(0f, back.AppliedBiasMeters);
        Assert.Equal(position, back.BiasedCameraRelativePosition);
    }

    private static (SliceGrid grid, BoundaryTopology topology, BoundaryFaceKey[] faces) SixFaceFixture()
    {
        var grid = new SliceGrid([100f], [100f], [100f], 0..0, 0..0, 0..0);
        var occupancy = new StructuralOccupancy(grid);
        occupancy.MarkUrban(0, 0, 0, regionId: "fixture");
        BoundaryTopology topology = BoundaryTopologyBuilder.Build(occupancy, MegastationPrototypeSettings.Default);
        return (grid, topology, Enum.GetValues<GridDirection>()
            .Select(direction => new BoundaryFaceKey(0, 0, 0, direction))
            .ToArray());
    }

    private static (SliceGrid grid, BoundaryTopology topology, BoundaryFaceKey[] faces) LargeSixFaceFixture()
    {
        var grid = new SliceGrid([1_000f], [1_000f], [1_000f], 0..0, 0..0, 0..0);
        var occupancy = new StructuralOccupancy(grid);
        occupancy.MarkUrban(0, 0, 0, regionId: "large-fixture");
        BoundaryTopology topology = BoundaryTopologyBuilder.Build(
            occupancy,
            MegastationPrototypeSettings.Default);
        return (grid, topology, Enum.GetValues<GridDirection>()
            .Select(direction => new BoundaryFaceKey(0, 0, 0, direction))
            .ToArray());
    }

    private static MegastationLightPlan PlanIndustrialSurface(
        float[] xWidths,
        float physicalArea)
    {
        var grid = new SliceGrid(xWidths, [1_000f], [100f], 0..0, 0..0, 0..0);
        var occupancy = new StructuralOccupancy(grid);
        for (int x = 0; x < xWidths.Length; x++)
            occupancy.MarkUrban(x, 0, 0, regionId: "industrial");
        BoundaryTopology topology = BoundaryTopologyBuilder.Build(
            occupancy,
            MegastationPrototypeSettings.Default);
        BoundaryFaceKey[] faces = Enumerable.Range(0, xWidths.Length)
            .Select(x => new BoundaryFaceKey(x, 0, 0, GridDirection.PositiveZ))
            .ToArray();
        MegastationSemanticZoningResult zoning = Zoning(
            ("industrial", MegastationZoneRole.Industrial, physicalArea, faces));
        return MegastationLightingPlanner.Plan(grid, topology, zoning);
    }

    private static Vector3 ClusterCentre(MegastationLightPlan plan, string clusterIdentity)
    {
        MegastationLightInstance[] lights = plan.Lights
            .Where(light => light.ClusterIdentity == clusterIdentity)
            .ToArray();
        return lights.Aggregate(Vector3.Zero, (sum, light) => sum + light.SurfacePosition)
            / lights.Length;
    }

    private static float MinimumSeparation(MegastationZoneRole role)
        => role switch
        {
            MegastationZoneRole.Industrial =>
                MegastationLightingTuning.Default.IndustrialMinimumClusterSeparation,
            MegastationZoneRole.Logistics =>
                MegastationLightingTuning.Default.LogisticsMinimumClusterSeparation,
            MegastationZoneRole.Utilities =>
                MegastationLightingTuning.Default.UtilitiesMinimumClusterSeparation,
            _ => MegastationLightingTuning.Default.StrategicMinimumClusterSeparation,
        };

    private static MegastationSemanticZoningResult Zoning(
        params (string identity, MegastationZoneRole role, float area, BoundaryFaceKey[] faces)[] specifications)
    {
        MegastationSemanticZone[] zones = specifications.Select((spec, index) =>
        {
            var anchor = new MegastationStructuralAnchor(
                spec.identity, MegastationStructuralAnchorKind.FaceDistrict, GridDirection.PositiveY);
            return new MegastationSemanticZone(
                spec.identity, anchor, spec.role, MegastationZoneCapabilities.None, spec.faces,
                spec.area, Vector3.Zero, Vector3.One, Metrics(spec.area / spec.faces.Length), 2000 + index);
        }).ToArray();
        MegastationSemanticSurface[] surfaces = zones.SelectMany(zone => zone.Faces.Select(face =>
            new MegastationSemanticSurface(
                face, zone.Anchor.Identity, zone.Anchor.SourceFace,
                Metrics(zone.TotalPhysicalArea / zone.Faces.Count), [], []))).ToArray();
        return new MegastationSemanticZoningResult
        {
            Anchors = zones.Select(zone => zone.Anchor).ToArray(),
            Surfaces = surfaces,
            SurfaceByFace = surfaces.ToDictionary(surface => surface.Face),
            Zones = zones,
            ZoneByFace = zones.SelectMany(zone => zone.Faces.Select(face => (face, zone)))
                .ToDictionary(pair => pair.face, pair => pair.zone),
            DebugIndexGroups = [],
            Diagnostics = new(
                surfaces.Length, zones.Length, zones.Sum(zone => zone.TotalPhysicalArea),
                0, zones.Length, 0, 0, 0, 0, 0,
                Enum.GetValues<MegastationZoneRole>().ToDictionary(
                    role => role,
                    role => zones.Where(zone => zone.Role == role).Sum(zone => zone.TotalPhysicalArea))),
        };
    }

    private static MegastationSurfaceMetrics Metrics(float area) => new(
        area,
        Vector3.Zero,
        0.45f,
        0.55f,
        0.35f,
        0.55f,
        0f,
        area,
        0.25f);

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
            ZoneByFace = zones.SelectMany(zone => zone.Faces.Select(face => (face, zone)))
                .ToDictionary(pair => pair.face, pair => pair.zone),
            DebugIndexGroups = source.DebugIndexGroups,
            Diagnostics = source.Diagnostics,
        };
    }

    private static object RegionSignature(MegastationLightingSurfaceRegion region) => new
    {
        region.Identity,
        region.ZoneIdentity,
        region.Role,
        region.Direction,
        region.PlaneGridCoordinate,
        Faces = string.Join('|', region.Faces),
    };

    private static object WindowRegionSignature(MegastationPlanarSurfaceRegion region) => new
    {
        region.Identity,
        Faces = string.Join('|', region.Faces),
        Mask = string.Join('|', region.Mask),
    };

    private static void AssertVectorNear(Vector3 expected, Vector3 actual, float tolerance)
    {
        Assert.InRange(MathF.Abs(expected.X - actual.X), 0f, tolerance);
        Assert.InRange(MathF.Abs(expected.Y - actual.Y), 0f, tolerance);
        Assert.InRange(MathF.Abs(expected.Z - actual.Z), 0f, tolerance);
    }

    private static float Component(Vector3 value, GridAxis axis) => axis switch
    {
        GridAxis.X => value.X,
        GridAxis.Y => value.Y,
        _ => value.Z,
    };

    private static string LightingSummary(MegastationLightingDiagnostics diagnostics)
        => $"[MegastationLighting] industrialZones={diagnostics.IndustrialZoneCount}; " +
           $"logisticsZones={diagnostics.LogisticsZoneCount}; utilitiesZones={diagnostics.UtilitiesZoneCount}; " +
           $"strategicZones={diagnostics.StrategicZoneCount}; " +
           $"industrial={diagnostics.IndustrialLightCount}/{diagnostics.IndustrialClusterCount}/" +
           $"{diagnostics.IndustrialEligibleArea:F0}m2; " +
           $"logistics={diagnostics.LogisticsLightCount}/{diagnostics.LogisticsClusterCount}/" +
           $"{diagnostics.LogisticsEligibleArea:F0}m2; " +
           $"utilities={diagnostics.UtilitiesLightCount}/{diagnostics.UtilitiesClusterCount}/" +
           $"{diagnostics.UtilitiesEligibleArea:F0}m2; " +
           $"strategic={diagnostics.StrategicLightCount}/{diagnostics.StrategicClusterCount}/" +
           $"{diagnostics.StrategicEligibleArea:F0}m2; clusters={diagnostics.ClusterCount}; " +
           $"steady={diagnostics.SteadyLightCount}; animated={diagnostics.AnimatedLightCount}; " +
           $"planningMs={diagnostics.PlanningMilliseconds}";
}
