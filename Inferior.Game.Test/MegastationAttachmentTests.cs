using Inferior.Game.StationGen;
using Inferior.Game.StationGen.Megastations;
using Microsoft.Xna.Framework;
using Xunit;

namespace Inferior.Game.Test;

public sealed class MegastationAttachmentTests
{
    private const string NovaAnchorageId = "Oranae:Oranae I:Nova Anchorage";

    [Fact]
    public void AttachmentTransformAlignsPortAtSiteOnAllSixNormals()
    {
        var port = new StationPort
        {
            Id = "fixture",
            LocalPosition = new Vector3(0f, 0f, 8f),
            OutwardNormal = Vector3.UnitZ,
            Size = PortSize.Medium,
        };
        Vector3 site = new(123f, -45f, 67f);
        Vector3[] normals =
        [
            Vector3.UnitX, -Vector3.UnitX,
            Vector3.UnitY, -Vector3.UnitY,
            Vector3.UnitZ, -Vector3.UnitZ,
        ];

        foreach (Vector3 normal in normals)
        foreach (int turn in Enumerable.Range(0, 4))
        {
            Matrix transform = MegastationAttachmentTransform.Solve(site, normal, port, turn);
            AssertVector(site, Vector3.Transform(port.LocalPosition, transform));
            AssertVector(-normal, Vector3.Normalize(Vector3.TransformNormal(port.OutwardNormal, transform)));
        }
    }

    [Fact]
    public void ExactSupportMaskRejectsHolesStepsAndInsufficientMargin()
    {
        MegastationAttachmentSurface surface = Surface(
            (0f, 10f, 0f, 10f),
            (10f, 20f, 0f, 4f),
            (10f, 20f, 6f, 10f));

        Assert.True(MegastationAttachmentPlanner.ContainsFootprint(surface, 2f, 8f, 2f, 8f));
        Assert.False(MegastationAttachmentPlanner.ContainsFootprint(surface, 11f, 19f, 3f, 7f));
        Assert.False(MegastationAttachmentPlanner.ContainsFootprint(surface, 1f, 9.5f, 1f, 9f));
        Assert.False(MegastationAttachmentPlanner.ContainsFootprint(surface, 8f, 18f, 2f, 8f));
    }

    [Fact]
    public void CandidateExtractionKeepsBroadDisconnectedMasksAndRejectsNarrowRegions()
    {
        var grid = new SliceGrid([30f, 30f, 10f, 30f], [30f], [10f, 10f], 0..0, 0..0, 0..0);
        var occupancy = new StructuralOccupancy(grid);
        occupancy.MarkUrban(0, 0, 0, regionId: "zone");
        occupancy.MarkUrban(1, 0, 0, regionId: "zone");
        occupancy.MarkUrban(3, 0, 0, regionId: "zone");
        occupancy.MarkUrban(2, 0, 1, regionId: "zone");
        BoundaryTopology topology = BoundaryTopologyBuilder.Build(
            occupancy, MegastationPrototypeSettings.Default);
        BoundaryFaceKey[] faces =
        [
            new(0, 0, 0, GridDirection.PositiveZ),
            new(1, 0, 0, GridDirection.PositiveZ),
            new(3, 0, 0, GridDirection.PositiveZ),
            new(2, 0, 1, GridDirection.PositiveZ),
        ];
        MegastationSemanticZoningResult zoning = Zoning(faces);

        MegastationAttachmentSurface[] a = MegastationAttachmentPlanner.ExtractCandidateSurfaces(
            grid, topology, zoning);
        MegastationAttachmentSurface[] b = MegastationAttachmentPlanner.ExtractCandidateSurfaces(
            grid, topology, Reverse(zoning));

        Assert.Equal(2, a.Length);
        Assert.Equal(a.Select(surface => surface.StableId), b.Select(surface => surface.StableId));
        Assert.Contains(a, surface => surface.SupportMask.Count == 2
            && MathF.Abs(surface.PhysicalExtents.X - 60f) < 0.001f);
        Assert.Contains(a, surface => surface.SupportMask.Count == 1
            && MathF.Abs(surface.PhysicalArea - 900f) < 0.001f);
        Assert.DoesNotContain(a, surface => surface.Faces.Contains(faces[3]));
    }

    [Fact]
    public void ExteriorClearanceAllowsHostContactAndRejectsOutwardOccupancy()
    {
        var grid = new SliceGrid([10f, 10f], [10f], [10f], 0..0, 0..0, 0..0);
        var hostOnly = new StructuralOccupancy(grid);
        hostOnly.MarkStructural(0, 0, 0);
        Vector3 min = new(0f, -2f, -2f);
        Vector3 max = new(8f, 2f, 2f);

        Assert.True(MegastationAttachmentPlanner.HasExteriorClearance(
            grid, hostOnly, min, max, Vector3.UnitX));

        var blocked = new StructuralOccupancy(grid);
        blocked.MarkStructural(0, 0, 0);
        blocked.MarkStructural(1, 0, 0);
        Assert.False(MegastationAttachmentPlanner.HasExteriorClearance(
            grid, blocked, min, max, Vector3.UnitX));
    }

    [Fact]
    public void SafeWhitelistIsRoleDrivenAndCannotGrowOrDock()
    {
        IReadOnlyList<StationModuleDefinition> safe = MegastationAttachmentPlanner.SafeDefinitions();

        Assert.DoesNotContain(safe, definition => ReferenceEquals(definition, StationModuleRegistry.CoreHub));
        Assert.DoesNotContain(safe, definition => definition.Ports.Any(port => port.IsDocking));
        Assert.Empty(MegastationAttachmentPlanner.DefinitionsFor(MegastationZoneRole.Structural));
        Assert.All(MegastationAttachmentPlanner.DefinitionsFor(MegastationZoneRole.Habitation),
            definition => Assert.Contains(definition.Category, new[] { "hab", "science" }));
    }

    [Fact]
    public void FootprintSuppressionRemovesOnlyDirectOverlapsWithoutRerollingSurvivors()
    {
        var reservation = new MegastationAttachmentReservation(
            "placement", GridDirection.PositiveZ, 0f,
            Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY,
            -5f, 5f, -5f, 5f);
        MegastationWindowInstance coveredWindow = Window("covered", Vector3.Zero);
        MegastationWindowInstance survivingWindow = Window("surviving", new Vector3(20f, 0f, 0f));
        var windowPlan = new MegastationWindowPlan(
            [], [], [coveredWindow, survivingWindow],
            new(0, 0, 0, 0, 0, 2, 2, 0, 0, 0, 0f, 0f, 0));
        MegastationLightInstance coveredLight = Light("covered", Vector3.Zero);
        MegastationLightInstance survivingLight = Light("surviving", new Vector3(20f, 0f, 0f));
        MegastationLightCluster[] clusters =
        [
            new("covered-cluster", "zone", "region", MegastationZoneRole.Industrial,
                coveredLight.SurfaceFace, 1),
            new("surviving-cluster", "zone", "region", MegastationZoneRole.Industrial,
                survivingLight.SurfaceFace, 1),
        ];
        var lightPlan = new MegastationLightPlan(
            [], clusters, [coveredLight, survivingLight],
            new(1, 0, 0, 0, 2, 0, 0, 0, 2, 0, 0, 0, 1, 0, 0, 0,
                100f, 0f, 0f, 0f, 2, 2, 0, 0));

        MegastationWindowPlan windows = MegastationAttachmentPlanner.SuppressWindows(
            windowPlan, [reservation], out int suppressedWindows);
        MegastationLightPlan lights = MegastationAttachmentPlanner.SuppressLights(
            lightPlan, [reservation], out int suppressedLights);

        Assert.Equal(1, suppressedWindows);
        Assert.Equal(1, suppressedLights);
        Assert.Equal(survivingWindow, Assert.Single(windows.Windows));
        Assert.Equal(survivingLight, Assert.Single(lights.Lights));
        Assert.Equal("surviving-cluster", Assert.Single(lights.Clusters).Identity);
    }

    [Fact]
    public void NovaPlanIsDeterministicTraversalIndependentAndNonRecursive()
    {
        MegastationPrototypeCpuResult result = MegastationPrototypeGenerator.GenerateCpu(NovaAnchorageId);
        MegastationSemanticZoningResult reversed = Reverse(result.SemanticZoning);
        MegastationAttachmentPlan rebuilt = MegastationAttachmentPlanner.Plan(
            result.Grid,
            result.RegularisedOccupancy,
            result.BoundaryTopology,
            reversed);

        Assert.NotEmpty(result.AttachmentPlan.Placements);
        Assert.Equal(result.AttachmentPlan.Placements, rebuilt.Placements);
        List<PlacedModule> modules = MegastationAttachmentPlanner.CreatePlacedModules(result.AttachmentPlan);
        Assert.Equal(result.AttachmentPlan.Placements.Count, modules.Count);
        Assert.All(modules, module =>
        {
            Assert.NotNull(module.AttachmentPort);
            Assert.Empty(module.OpenPorts);
            Assert.DoesNotContain(module.Definition.Ports, port => port.IsDocking);
        });

        Console.WriteLine(
            $"G1 Nova candidates={result.AttachmentPlan.Diagnostics.CandidateSurfaceCount}; " +
            $"selected={result.AttachmentPlan.Diagnostics.SelectedCandidateCount}; " +
            $"placed={result.AttachmentPlan.Diagnostics.PlacedModuleCount}; " +
            $"roles=H:{result.AttachmentPlan.Diagnostics.HabitationCount}," +
            $"I:{result.AttachmentPlan.Diagnostics.IndustrialCount}," +
            $"L:{result.AttachmentPlan.Diagnostics.LogisticsCount}," +
            $"U:{result.AttachmentPlan.Diagnostics.UtilitiesCount}," +
            $"S:{result.AttachmentPlan.Diagnostics.StrategicCount}; " +
            $"families={string.Join(',', result.AttachmentPlan.Diagnostics.ModuleFamilyCounts.Select(pair => $"{pair.Key}:{pair.Value}"))}; " +
            $"supportRejects={result.AttachmentPlan.Diagnostics.RejectedSupportCount}; " +
            $"clearanceRejects={result.AttachmentPlan.Diagnostics.RejectedClearanceCount}; " +
            $"windowsSuppressed={result.AttachmentPlan.Diagnostics.SuppressedWindowCount}; " +
            $"lightsSuppressed={result.AttachmentPlan.Diagnostics.SuppressedLightCount}; " +
            $"planningMs={result.AttachmentPlan.Diagnostics.PlanningMilliseconds}; " +
            $"clearanceMs={result.AttachmentPlan.Diagnostics.ClearanceMilliseconds}");
    }

    [Theory]
    [InlineData("Gaanis:Gaanis II:Omega Beacon")]
    [InlineData("Araris:Araris I:Swift Depot")]
    public void OtherMegastationsRemainBoundedAndReportable(string stationIdentity)
    {
        MegastationPrototypeCpuResult result = MegastationPrototypeGenerator
            .GenerateCpu(stationIdentity);
        MegastationAttachmentDiagnostics diagnostics = result.AttachmentPlan.Diagnostics;
        MegastationInfrastructureDiagnostics infrastructure = result.InfrastructurePlan.Diagnostics;

        Assert.InRange(diagnostics.PlacedModuleCount, 10, 80);
        Console.WriteLine(
            $"G1 {stationIdentity} candidates={diagnostics.CandidateSurfaceCount}; " +
            $"selected={diagnostics.SelectedCandidateCount}; placed={diagnostics.PlacedModuleCount}; " +
            $"roles=H:{diagnostics.HabitationCount},I:{diagnostics.IndustrialCount}," +
            $"L:{diagnostics.LogisticsCount},U:{diagnostics.UtilitiesCount},S:{diagnostics.StrategicCount}; " +
            $"families={string.Join(',', diagnostics.ModuleFamilyCounts.Select(pair => $"{pair.Key}:{pair.Value}"))}; " +
            $"supportRejects={diagnostics.RejectedSupportCount}; clearanceRejects={diagnostics.RejectedClearanceCount}; " +
            $"windowsSuppressed={diagnostics.SuppressedWindowCount}; lightsSuppressed={diagnostics.SuppressedLightCount}; " +
            $"planningMs={diagnostics.PlanningMilliseconds}; clearanceMs={diagnostics.ClearanceMilliseconds}");
        Assert.InRange(infrastructure.ClusterCount, 1,
            MegastationInfrastructureTuning.Default.StationClusterCap);
        Assert.True(infrastructure.PrimitiveCount >= infrastructure.ClusterCount * 5);
        Console.WriteLine(
            $"G2 {stationIdentity} candidateArea={infrastructure.CandidateArea:F0}; " +
            $"activeArea={infrastructure.ActiveArea:F0}; clusters={infrastructure.ClusterCount}; " +
            $"primitives={infrastructure.PrimitiveCount}; housings={infrastructure.HousingCount}; " +
            $"vents={infrastructure.VentCount}; tanks={infrastructure.TankCount}; " +
            $"visible={infrastructure.VisibleVertexCount}v/{infrastructure.VisibleTriangleCount}t/" +
            $"{infrastructure.VisibleMeshBytes}B; shadow={infrastructure.ShadowVertexCount}v/" +
            $"{infrastructure.ShadowTriangleCount}t/{infrastructure.ShadowMeshBytes}B; " +
            $"planningMs={infrastructure.PlanningMilliseconds}; meshMs={infrastructure.MeshBuildMilliseconds}");
    }

    [Fact]
    public void PlanningAndSuppressionLeaveStructuralSolidAndSurvivingIdentitiesUnchanged()
    {
        MegastationPrototypeCpuResult result = MegastationPrototypeGenerator.GenerateCpu(NovaAnchorageId);
        string structuralBefore = MegastationMassingSignatureBuilder.ComputeStructuralSolid(result).Body;
        string topologyBefore = result.Diagnostics.BoundaryTopologySignature;
        object[] zoningBefore = result.SemanticZoning.Zones.Select(ZoneSignature).ToArray();
        var (verticesBefore, indicesBefore) = result.Mesh.ToIntArrays();
        MegastationAttachmentPlan replanned = MegastationAttachmentPlanner.Plan(
            result.Grid,
            result.RegularisedOccupancy,
            result.BoundaryTopology,
            result.SemanticZoning);
        MegastationWindowPlan originalWindows = MegastationWindowPlanner.Plan(
            result.Grid, result.BoundaryTopology, result.SemanticZoning);
        MegastationLightPlan originalLights = MegastationLightingPlanner.Plan(
            result.Grid, result.BoundaryTopology, result.SemanticZoning);

        MegastationWindowPlan filteredWindows = MegastationAttachmentPlanner.SuppressWindows(
            originalWindows, replanned.Reservations, out _);
        MegastationLightPlan filteredLights = MegastationAttachmentPlanner.SuppressLights(
            originalLights, replanned.Reservations, out _);

        Assert.Equal(structuralBefore, MegastationMassingSignatureBuilder.ComputeStructuralSolid(result).Body);
        Assert.Equal(topologyBefore, result.Diagnostics.BoundaryTopologySignature);
        Assert.Equal(zoningBefore, result.SemanticZoning.Zones.Select(ZoneSignature));
        Assert.Equal(verticesBefore, result.Mesh.ToIntArrays().verts);
        Assert.Equal(indicesBefore, result.Mesh.ToIntArrays().indices);
        Assert.Subset(originalWindows.Windows.Select(window => window.Identity).ToHashSet(),
            filteredWindows.Windows.Select(window => window.Identity).ToHashSet());
        Assert.Subset(originalLights.Lights.Select(light => light.Identity).ToHashSet(),
            filteredLights.Lights.Select(light => light.Identity).ToHashSet());
        Assert.Equal(result.WindowPlan.Windows.Select(window => window.Identity),
            filteredWindows.Windows.Select(window => window.Identity));
        Assert.Equal(result.LightPlan.Lights.Select(light => light.Identity),
            filteredLights.Lights.Select(light => light.Identity));
    }

    private static MegastationAttachmentSurface Surface(
        params (float MinU, float MaxU, float MinV, float MaxV)[] rectangles)
    {
        MegastationAttachmentMaskRect[] mask = rectangles.Select((rectangle, index) => new MegastationAttachmentMaskRect(
            new BoundaryFaceKey(index, 0, 0, GridDirection.PositiveZ),
            rectangle.MinU,
            rectangle.MaxU,
            rectangle.MinV,
            rectangle.MaxV)).ToArray();
        return new(
            "fixture", "zone", 1, MegastationZoneRole.Habitation,
            GridDirection.PositiveZ, 1, 5f,
            Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY, Vector3.Zero,
            mask.Select(rectangle => rectangle.Face).ToArray(), mask,
            mask.Sum(rectangle => (rectangle.MaxU - rectangle.MinU) * (rectangle.MaxV - rectangle.MinV)),
            new Vector2(20f, 10f), 0f, 0f, 0f, 0f, new Vector2(18f, 8f), 10f);
    }

    private static MegastationSemanticZoningResult Zoning(BoundaryFaceKey[] faces)
    {
        var anchor = new MegastationStructuralAnchor(
            "zone", MegastationStructuralAnchorKind.FaceDistrict, GridDirection.PositiveZ);
        var zone = new MegastationSemanticZone(
            "zone", anchor, MegastationZoneRole.Habitation, MegastationZoneCapabilities.None,
            faces, 1_000f, Vector3.Zero, Vector3.One, Metrics(), 1234);
        MegastationSemanticSurface[] surfaces = faces.Select(face => new MegastationSemanticSurface(
            face, anchor.Identity, anchor.SourceFace, Metrics(), [], [])).ToArray();
        return new()
        {
            Anchors = [anchor],
            Surfaces = surfaces,
            SurfaceByFace = surfaces.ToDictionary(surface => surface.Face),
            Zones = [zone],
            ZoneByFace = faces.ToDictionary(face => face, _ => zone),
            DebugIndexGroups = [],
            Diagnostics = new(1, 1, faces.Length, 0, 1, 0, 0, 0, 0, 0,
                Enum.GetValues<MegastationZoneRole>().ToDictionary(role => role, _ => 0f)),
        };
    }

    private static MegastationSurfaceMetrics Metrics()
        => new(1f, Vector3.Zero, 0.5f, 0.5f, 0.5f, 0.5f, 0f, 1f, 0f);

    private static MegastationWindowInstance Window(string identity, Vector3 position)
        => new(identity, "region", "block", position, Vector3.UnitZ, Vector3.UnitY,
            2f, 2f, MegastationWindowState.Lit, Color.White);

    private static MegastationLightInstance Light(string identity, Vector3 position)
        => new(identity, $"{identity}-cluster", "region", MegastationZoneRole.Industrial,
            new BoundaryFaceKey(0, 0, 0, GridDirection.PositiveZ), position, Vector3.UnitZ,
            position + Vector3.UnitZ * 0.06f, Color.White, GlowType.AmbientMarker, 1f, 0f, 0f,
            LightPattern.Continuous);

    private static MegastationSemanticZoningResult Reverse(MegastationSemanticZoningResult source)
        => new()
        {
            Anchors = source.Anchors.Reverse().ToArray(),
            Surfaces = source.Surfaces.Reverse().ToArray(),
            SurfaceByFace = source.SurfaceByFace,
            Zones = source.Zones.Reverse().Select(zone => zone with
            {
                Faces = zone.Faces.Reverse().ToArray(),
            }).ToArray(),
            ZoneByFace = source.ZoneByFace,
            DebugIndexGroups = source.DebugIndexGroups,
            Diagnostics = source.Diagnostics,
        };

    private static void AssertVector(Vector3 expected, Vector3 actual)
    {
        Assert.InRange(MathF.Abs(expected.X - actual.X), 0f, 0.001f);
        Assert.InRange(MathF.Abs(expected.Y - actual.Y), 0f, 0.001f);
        Assert.InRange(MathF.Abs(expected.Z - actual.Z), 0f, 0.001f);
    }

    private static object ZoneSignature(MegastationSemanticZone zone) => new
    {
        zone.Identity,
        zone.Role,
        Faces = string.Join('|', zone.Faces),
        zone.Seed,
    };
}
