using Inferior.Game.StationGen;
using Inferior.Game.StationGen.Megastations;
using Microsoft.Xna.Framework;
using Xunit;

namespace Inferior.Game.Test;

public sealed class MegastationServiceChannelTests
{
    private const string Nova = "Oranae:Oranae I:Nova Anchorage";
    private static readonly SystemMaterialAssignmentContext Materials =
        SystemMaterialCpuLibraryGenerator.CreateAssignmentContext(0x534332);
    private static readonly Lazy<MegastationPrototypeCpuResult> Result =
        new(() => MegastationPrototypeGenerator.GenerateCpu(Nova, systemMaterials: Materials));

    [Fact]
    public void NovaProducesInspectableTextureFreeBatchedServiceNetworks()
    {
        MegastationPrototypeCpuResult result = Result.Value;
        MegastationServiceChannelDiagnostics d = result.ServiceChannelPlan.Diagnostics;
        Assert.InRange(d.NetworkSurfaceCount, 1, 18);
        Assert.Equal(d.NetworkSurfaceCount, d.PrimaryTrunkCount);
        Assert.True(d.SecondaryBranchCount >= d.NetworkSurfaceCount);
        Assert.True(d.RunSegmentCount > d.PrimaryTrunkCount + d.SecondaryBranchCount);
        Assert.True(d.TJunctionCount > 0);
        Assert.True(d.TurnCount > 0);
        Assert.InRange(d.CoveredTJunctionCount, 1, d.TJunctionCount);
        Assert.Equal(d.TJunctionCount,
            d.CoveredTJunctionCount + d.UncoveredTJunctionCount);
        Assert.True(d.CoveredNodeVisibleTriangleCount > 0);
        Assert.True(d.CoveredNodeShadowTriangleCount > 0);
        Assert.True(d.CoveredNodeShadowTriangleCount
            < d.CoveredNodeVisibleTriangleCount);
        Assert.True(d.TotalChannelLength >= 200f);
        Assert.True(d.BridgeCount > 0);
        Assert.False(result.ServiceChannelMesh.IsEmpty);
        Assert.Equal(0, d.OwnedTextureDelta);
        Assert.Equal(4, d.GpuBufferDelta);
        Assert.InRange(d.MaterialRangeCount, 1, SystemMaterialRecipes.All.Count);
        Assert.InRange(d.ShadowTriangleCount, 1, d.VisibleTriangleCount - 1);

        PlacedModule module = Assert.IsType<PlacedModule>(
            MegastationPrototypeGenerator.CreateServiceChannelModule(result));
        Assert.True(module.HasNativeMegastationServiceChannels);
        Assert.True(module.IsHullLessPresentationLayer);
        Assert.Null(module.TextureInstance);
        Assert.Null(module.MaterialInstance);
        Assert.Same(result.ServiceChannelMesh, module.Mesh);
        Assert.NotEmpty(module.DecorationMaterialRanges);
        Console.WriteLine(Summary(d));
    }

    [Fact]
    public void PlanAndMaterialGroupedGeometryAreTraversalIndependent()
    {
        MegastationPrototypeCpuResult result = Result.Value;
        MegastationServiceChannelPlan replay = MegastationServiceChannelPlanner.Plan(
            result.PlanarRegions.Reverse().ToArray(), result.AttachmentPlan,
            result.WindowPlan, result.LightPlan, result.InfrastructurePlan,
            result.MegaGreeblePlan, result.FabricPlan);
        MegastationServiceChannelMeshBuildResult replayMesh =
            MegastationServiceChannelMeshBuilder.Build(replay, result.MaterialAssignment);

        Assert.Equal(result.ServiceChannelPlan.Diagnostics.PlanSignature,
            replay.Diagnostics.PlanSignature);
        Assert.Equal(result.ServiceChannelPlan.Networks.Select(n => n.Identity),
            replay.Networks.Select(n => n.Identity));
        Assert.Equal(result.ServiceChannelMesh.ToIntArrays().verts,
            replayMesh.Mesh.ToIntArrays().verts);
        Assert.Equal(result.ServiceChannelMesh.ToIntArrays().indices,
            replayMesh.Mesh.ToIntArrays().indices);
    }

    [Fact]
    public void PlanningDoesNotMutateStructuralOrAcceptedDecorationResults()
    {
        MegastationPrototypeCpuResult result = Result.Value;
        MegastationStructuralSolidSignature structuralBefore =
            MegastationMassingSignatureBuilder.ComputeStructuralSolid(result);
        var structuralMeshBefore = result.Mesh.ToIntArrays();
        MegastationAttachmentPlacement[] g1 = result.AttachmentPlan.Placements.ToArray();
        MegastationInfrastructureInstance[] g2 = result.InfrastructurePlan.Instances.ToArray();
        MegastationMegaGreebleInstance[] mega = result.MegaGreeblePlan.Instances.ToArray();
        MegastationFabricInstance[] fabric = result.FabricPlan.Instances.ToArray();

        _ = MegastationServiceChannelPlanner.Plan(result.PlanarRegions,
            result.AttachmentPlan, result.WindowPlan, result.LightPlan,
            result.InfrastructurePlan, result.MegaGreeblePlan, result.FabricPlan);

        Assert.Equal(structuralBefore,
            MegastationMassingSignatureBuilder.ComputeStructuralSolid(result));
        Assert.Equal(structuralMeshBefore.verts, result.Mesh.ToIntArrays().verts);
        Assert.Equal(structuralMeshBefore.indices, result.Mesh.ToIntArrays().indices);
        Assert.Equal(g1, result.AttachmentPlan.Placements);
        Assert.Equal(g2, result.InfrastructurePlan.Instances);
        Assert.Equal(mega, result.MegaGreeblePlan.Instances);
        Assert.Equal(fabric, result.FabricPlan.Instances);
    }

    [Fact]
    public void NetworksAreRectilinearConnectedAndStayOnExactPlanarSupport()
    {
        MegastationPrototypeCpuResult result = Result.Value;
        Dictionary<string, MegastationPlanarRegion> regions = result.PlanarRegions
            .ToDictionary(region => region.StableId);
        Assert.All(result.ServiceChannelPlan.Networks, network =>
        {
            MegastationPlanarRegion region = regions[network.SurfaceStableId];
            Assert.NotEqual(MegastationZoneRole.Structural, network.ZoneRole);
            Assert.InRange(network.Normal.Length(), .999f, 1.001f);
            Assert.InRange(MathF.Abs(Vector3.Dot(network.Normal, network.TangentU)), 0f, .001f);
            Assert.InRange(MathF.Abs(Vector3.Dot(network.Normal, network.TangentV)), 0f, .001f);
            Assert.Equal(network.Runs.Count * 2,
                network.Nodes.Sum(node => node.IncidentRunIdentities.Count));
            Assert.Equal(network.Runs.Count, network.Runs.Select(run => SegmentKey(run)).Distinct().Count());

            Assert.All(network.Runs, run =>
            {
                Assert.True(run.Length > 1f);
                Assert.True(MathF.Abs(run.End.X - run.Start.X) < .01f
                    || MathF.Abs(run.End.Y - run.Start.Y) < .01f);
                Assert.InRange(run.Width, 10f, 23f);
                Assert.InRange(run.ApparentDepth, 1.6f, 4.5f);
                Assert.InRange(run.CableCount, 4, 8);
                (float minU, float maxU, float minV, float maxV) = Footprint(run);
                Assert.True(MegastationPlanarRegionExtractor.ContainsFootprint(
                    region, minU, maxU, minV, maxV, .5f));
            });
            Assert.All(network.Nodes, node => Assert.Contains(node.Kind,
                Enum.GetValues<MegastationServiceChannelNodeKind>()));
            Assert.All(network.Nodes.Where(node => node.Kind != MegastationServiceChannelNodeKind.DeadEnd),
                node =>
                {
                    float channelHalf = network.Runs.Where(run => node.IncidentRunIdentities.Contains(run.Identity))
                        .Max(run => run.Width) * .5f;
                    float halfU = node.Variant == MegastationServiceChannelNodeVariant.Exposed
                        ? channelHalf
                        : (node.MainAlongU ? node.HousingLength : node.HousingWidth) * .5f + 2.3f;
                    float halfV = node.Variant == MegastationServiceChannelNodeVariant.Exposed
                        ? channelHalf
                        : (node.MainAlongU ? node.HousingWidth : node.HousingLength) * .5f + 2.3f;
                    Assert.True(MegastationPlanarRegionExtractor.ContainsFootprint(region,
                        node.Position.X - halfU, node.Position.X + halfU,
                        node.Position.Y - halfV, node.Position.Y + halfV, .5f));
                });
            Assert.All(network.Nodes.Where(node => node.Kind != MegastationServiceChannelNodeKind.DeadEnd),
                node => Assert.Null(node.Endpoint));
            Assert.All(network.Nodes.Where(node => node.Kind == MegastationServiceChannelNodeKind.DeadEnd),
                node => Assert.NotNull(node.Endpoint));
        });
    }

    [Fact]
    public void VisibleAndCasterGeometryAreFiniteIndexedAndNonDegenerate()
    {
        MegastationPrototypeCpuResult result = Result.Value;
        AssertMesh(result.ServiceChannelMesh);
        StationMeshCpuData? caster = result.ServiceChannelMesh.PrepareIndexRanges(
            result.ServiceChannelMesh.DecorClassRanges
                .Where(range => range.decorClass == DecorClass.MegastationServiceChannelMajor)
                .Select(range => (range.indexStart, range.indexCount)).ToArray());
        Assert.NotNull(caster);
        AssertCpuMesh(caster!);
    }

    [Fact]
    public void ShadowAndResidencyPoliciesAreExplicit()
    {
        Assert.True(StationDecorator.DecorCastingPolicy[
            DecorClass.MegastationServiceChannelMajor]);
        Assert.False(StationDecorator.DecorCastingPolicy[
            DecorClass.MegastationServiceChannelMinor]);
        Assert.Contains(DecorClass.MegastationServiceChannelMajor,
            StationDecorator.MegastationCasterClasses);
        Assert.Contains(Result.Value.ServiceChannelMesh.DecorClassRanges,
            range => range.decorClass == DecorClass.MegastationServiceChannelMajor
                && range.indexCount > 0);
        Assert.Contains(Result.Value.ServiceChannelMesh.DecorClassRanges,
            range => range.decorClass == DecorClass.MegastationServiceChannelMinor
                && range.indexCount > 0);
    }

    [Fact]
    public void AcceptedRunsRemainClearOfEveryUpstreamReservationClass()
    {
        MegastationPrototypeCpuResult result = Result.Value;
        Assert.All(result.ServiceChannelPlan.Networks, network =>
        {
            Assert.All(network.Runs, run => AssertClear(result, network, Footprint(run)));
            Assert.All(network.Nodes.Where(node =>
                    node.Variant != MegastationServiceChannelNodeVariant.Exposed), node =>
            {
                float halfU = (node.MainAlongU ? node.HousingLength : node.HousingWidth) * .5f + 2.3f;
                float halfV = (node.MainAlongU ? node.HousingWidth : node.HousingLength) * .5f + 2.3f;
                AssertClear(result, network,
                    (node.Position.X - halfU, node.Position.X + halfU,
                        node.Position.Y - halfV, node.Position.Y + halfV));
            });
        });
    }

    [Fact]
    public void CoveredJunctionNodesHaveStableValidArmRelationships()
    {
        MegastationServiceChannelPlan plan = Result.Value.ServiceChannelPlan;
        MegastationServiceChannelNode[] covered = plan.Networks.SelectMany(network => network.Nodes)
            .Where(node => node.Variant != MegastationServiceChannelNodeVariant.Exposed).ToArray();
        Assert.NotEmpty(covered);
        Assert.Contains(covered, node => node.Variant == MegastationServiceChannelNodeVariant.ConverterHouse);
        Assert.Contains(covered, node => node.Variant == MegastationServiceChannelNodeVariant.SwitchingNode);
        Assert.Contains(covered, node => node.Variant == MegastationServiceChannelNodeVariant.HeavyDistribution);

        Assert.All(plan.Networks, network =>
        {
        MegastationServiceChannelNode[] networkCovered = network.Nodes
            .Where(node => node.Variant != MegastationServiceChannelNodeVariant.Exposed).ToArray();
        Assert.All(networkCovered, node =>
        {
            MegastationServiceChannelRun[] arms = network.Runs
                .Where(run => node.IncidentRunIdentities.Contains(run.Identity)).ToArray();
            int alongU = arms.Count(run => run.AlongU);
            int alongV = arms.Length - alongU;
            if (node.Kind == MegastationServiceChannelNodeKind.TJunction)
            {
                Assert.Equal(3, arms.Length);
                Assert.True((alongU == 2 && alongV == 1) || (alongU == 1 && alongV == 2));
                Assert.Equal(alongU == 2, node.MainAlongU);
            }
            else
            {
                Assert.Equal(MegastationServiceChannelNodeKind.FourWay, node.Kind);
                Assert.Equal(2, alongU);
                Assert.Equal(2, alongV);
            }
            float channelWidth = arms.Max(run => run.Width);
            Assert.True(node.HousingWidth > channelWidth);
            Assert.True(node.HousingLength > channelWidth);
            Assert.InRange(node.HousingHeight, 4.4f, 9.2f);
        });
        for (int a = 0; a < networkCovered.Length; a++)
        for (int b = a + 1; b < networkCovered.Length; b++)
            Assert.False(Overlaps(NodeFootprint(networkCovered[a]),
                NodeFootprint(networkCovered[b]), 0f));
        Assert.All(network.Bridges, bridge =>
        {
            MegastationServiceChannelRun run = network.Runs.Single(r => r.Identity == bridge.RunIdentity);
            Vector2 centre = Vector2.Lerp(run.Start, run.End, bridge.PositionAlongRun);
            Assert.DoesNotContain(networkCovered, node => Vector2.Distance(node.Position, centre)
                < MathF.Max(node.HousingWidth, node.HousingLength) * .5f + 8f);
        });
        });
    }

    [Fact]
    public void ChannelRichSurfacesReceiveMoreBranchOpportunitiesWithoutUniformCoverage()
    {
        MegastationServiceChannelNetwork[] networks = Result.Value.ServiceChannelPlan.Networks.ToArray();
        MegastationServiceChannelNetwork[] rich = networks
            .Where(network => network.Density == MegastationServiceChannelDensity.ChannelRich).ToArray();
        MegastationServiceChannelNetwork[] light = networks
            .Where(network => network.Density == MegastationServiceChannelDensity.Light).ToArray();
        Assert.NotEmpty(rich);
        double richBranches = rich.Average(network => network.Runs
            .Where(run => run.Scale == MegastationServiceChannelRunScale.Secondary)
            .Select(run => run.RouteIdentity).Distinct().Count());
        Assert.True(richBranches > 2d);
        if (light.Length > 0)
        {
            double lightBranches = light.Average(network => network.Runs
                .Where(run => run.Scale == MegastationServiceChannelRunScale.Secondary)
                .Select(run => run.RouteIdentity).Distinct().Count());
            Assert.True(richBranches > lightBranches);
        }
        Assert.True(Result.Value.ServiceChannelPlan.Diagnostics.NetworkSurfaceCount
            < Result.Value.ServiceChannelPlan.Diagnostics.EligibleRegionCount / 4);
    }

    [Theory]
    [InlineData("Gaanis:Gaanis II:Omega Beacon")]
    [InlineData("Araris:Araris I:Swift Depot")]
    public void OtherMegastationsProduceDeterministicTextureFreeNetworks(string identity)
    {
        MegastationPrototypeCpuResult result = MegastationPrototypeGenerator.GenerateCpu(
            identity, systemMaterials: Materials);
        MegastationServiceChannelPlan replay = MegastationServiceChannelPlanner.Plan(
            result.PlanarRegions.Reverse().ToArray(), result.AttachmentPlan,
            result.WindowPlan, result.LightPlan, result.InfrastructurePlan,
            result.MegaGreeblePlan, result.FabricPlan);
        Assert.Equal(result.ServiceChannelPlan.Diagnostics.PlanSignature,
            replay.Diagnostics.PlanSignature);
        Assert.Equal(0, result.ServiceChannelPlan.Diagnostics.OwnedTextureDelta);
        Assert.InRange(result.ServiceChannelPlan.Diagnostics.NetworkSurfaceCount, 1, 18);
        Console.WriteLine($"{identity}: {Summary(result.ServiceChannelPlan.Diagnostics)}");
    }

    private static (float minU, float maxU, float minV, float maxV) Footprint(
        MegastationServiceChannelRun run)
    {
        float half = run.Width * .5f;
        return run.AlongU
            ? (MathF.Min(run.Start.X, run.End.X), MathF.Max(run.Start.X, run.End.X),
                run.Start.Y - half, run.Start.Y + half)
            : (run.Start.X - half, run.Start.X + half,
                MathF.Min(run.Start.Y, run.End.Y), MathF.Max(run.Start.Y, run.End.Y));
    }

    private static string SegmentKey(MegastationServiceChannelRun run)
    {
        string a = $"{run.Start.X:F3},{run.Start.Y:F3}";
        string b = $"{run.End.X:F3},{run.End.Y:F3}";
        return string.CompareOrdinal(a, b) <= 0 ? $"{a}>{b}" : $"{b}>{a}";
    }

    private static bool Coplanar(MegastationServiceChannelNetwork network,
        Vector3 normal, float plane)
        => Vector3.Dot(network.Normal, normal) > .999f
            && MathF.Abs(network.PlaneCoordinateMetres - plane) < .2f;

    private static void AssertClear(MegastationPrototypeCpuResult result,
        MegastationServiceChannelNetwork network,
        (float minU, float maxU, float minV, float maxV) footprint)
    {
        Assert.DoesNotContain(result.AttachmentPlan.Reservations, reservation =>
        {
            if (Vector3.Dot(network.Normal, reservation.Normal) < .999f
                || MathF.Abs(network.PlaneCoordinateMetres - reservation.PlaneCoordinateMetres) >= .2f)
                return false;
            Vector3 origin = reservation.Normal * reservation.PlaneCoordinateMetres;
            Vector3[] corners =
            [
                origin + reservation.TangentU * reservation.MinU + reservation.TangentV * reservation.MinV,
                origin + reservation.TangentU * reservation.MaxU + reservation.TangentV * reservation.MinV,
                origin + reservation.TangentU * reservation.MaxU + reservation.TangentV * reservation.MaxV,
                origin + reservation.TangentU * reservation.MinU + reservation.TangentV * reservation.MaxV,
            ];
            return Overlaps(footprint, Project(network, corners), 1.25f);
        });
        Assert.DoesNotContain(result.WindowPlan.Windows, window =>
            Coplanar(network, window.Normal, Vector3.Dot(window.Centre, window.Normal))
            && Overlaps(footprint, PointRect(network, window.Centre,
                MathF.Max(window.Width, window.Height) * .5f + 1f), 1.25f));
        Assert.DoesNotContain(result.LightPlan.Lights, light =>
            Coplanar(network, light.Normal, Vector3.Dot(light.SurfacePosition, light.Normal))
            && Overlaps(footprint, PointRect(network, light.SurfacePosition, 2.5f), 1.25f));
        Assert.DoesNotContain(result.InfrastructurePlan.Clusters, cluster =>
            cluster.SurfaceStableId == network.SurfaceStableId
            && Overlaps(footprint, (cluster.MinU, cluster.MaxU, cluster.MinV, cluster.MaxV), 1.25f));
        Assert.DoesNotContain(result.MegaGreeblePlan.Instances, mega =>
            mega.SurfaceStableId == network.SurfaceStableId
            && Overlaps(footprint, (mega.MinU, mega.MaxU, mega.MinV, mega.MaxV), 1.25f));
        Assert.DoesNotContain(result.FabricPlan.Instances, fabric =>
            fabric.SurfaceStableId == network.SurfaceStableId
            && Overlaps(footprint, (fabric.MinU, fabric.MaxU, fabric.MinV, fabric.MaxV), 1.25f));
    }

    private static (float minU, float maxU, float minV, float maxV) PointRect(
        MegastationServiceChannelNetwork network, Vector3 point, float radius)
    {
        float u = Vector3.Dot(point, network.TangentU);
        float v = Vector3.Dot(point, network.TangentV);
        return (u - radius, u + radius, v - radius, v + radius);
    }

    private static (float minU, float maxU, float minV, float maxV) NodeFootprint(
        MegastationServiceChannelNode node)
    {
        float halfU = (node.MainAlongU ? node.HousingLength : node.HousingWidth) * .5f + 2.3f;
        float halfV = (node.MainAlongU ? node.HousingWidth : node.HousingLength) * .5f + 2.3f;
        return (node.Position.X - halfU, node.Position.X + halfU,
            node.Position.Y - halfV, node.Position.Y + halfV);
    }

    private static (float minU, float maxU, float minV, float maxV) Project(
        MegastationServiceChannelNetwork network, IReadOnlyList<Vector3> points)
    {
        float[] u = points.Select(point => Vector3.Dot(point, network.TangentU)).ToArray();
        float[] v = points.Select(point => Vector3.Dot(point, network.TangentV)).ToArray();
        return (u.Min(), u.Max(), v.Min(), v.Max());
    }

    private static bool Overlaps(
        (float minU, float maxU, float minV, float maxV) a,
        (float minU, float maxU, float minV, float maxV) b, float margin)
        => a.minU < b.maxU + margin && a.maxU > b.minU - margin
            && a.minV < b.maxV + margin && a.maxV > b.minV - margin;

    private static void AssertMesh(StationModuleMesh mesh)
    {
        var arrays = mesh.ToIntArrays();
        AssertCpuMesh(new StationMeshCpuData(arrays.verts, arrays.indices));
    }

    private static void AssertCpuMesh(StationMeshCpuData mesh)
    {
        Assert.NotEmpty(mesh.Vertices);
        Assert.NotEmpty(mesh.Indices);
        Assert.Equal(0, mesh.Indices.Length % 3);
        Assert.All(mesh.Vertices, vertex =>
        {
            Assert.True(IsFinite(vertex.Position));
            Assert.True(IsFinite(vertex.Normal));
            Assert.True(float.IsFinite(vertex.TextureCoordinate.X));
            Assert.True(float.IsFinite(vertex.TextureCoordinate.Y));
            Assert.InRange(vertex.Normal.Length(), .999f, 1.001f);
        });
        Assert.All(mesh.Indices, index => Assert.InRange(index, 0, mesh.Vertices.Length - 1));
        for (int index = 0; index < mesh.Indices.Length; index += 3)
        {
            Vector3 a = mesh.Vertices[mesh.Indices[index]].Position;
            Vector3 b = mesh.Vertices[mesh.Indices[index + 1]].Position;
            Vector3 c = mesh.Vertices[mesh.Indices[index + 2]].Position;
            Assert.True(Vector3.Cross(b - a, c - a).LengthSquared() > 1e-8f);
        }
    }

    private static bool IsFinite(Vector3 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static string Summary(MegastationServiceChannelDiagnostics d)
        => $"SC2a regions={d.EligibleRegionCount}; networks={d.NetworkSurfaceCount}; " +
           $"primary={d.PrimaryTrunkCount}; secondary={d.SecondaryBranchCount}; runs={d.RunSegmentCount}; " +
           $"nodes={d.TurnCount}/{d.TJunctionCount}/{d.FourWayJunctionCount}/{d.DeadEndCount}; " +
           $"covered={d.CoveredTJunctionCount}t/{d.UncoveredTJunctionCount}minor/{d.CoveredFourWayJunctionCount}four; " +
           $"length={d.TotalChannelLength:F0}m ({d.MinimumPrimaryLength:F1}/{d.MedianPrimaryLength:F1}/{d.MaximumPrimaryLength:F1}); " +
           $"bridges={d.BridgeCount}; roles={string.Join(',', d.ByRole.Select(x => $"{x.Key}:{x.Value}"))}; " +
           $"mesh={d.VisibleVertexCount}v/{d.VisibleTriangleCount}t/{d.VisibleMeshBytes}B; " +
           $"shadow={d.ShadowVertexCount}v/{d.ShadowTriangleCount}t/{d.ShadowMeshBytes}B; " +
           $"nodeMesh={d.CoveredNodeVisibleVertexCount}v/{d.CoveredNodeVisibleTriangleCount}t," +
           $"caster:{d.CoveredNodeShadowVertexCount}v/{d.CoveredNodeShadowTriangleCount}t; " +
           $"rejects=mask:{d.ExactMaskRejectCount},g1:{d.G1RejectCount},windows:{d.WindowRejectCount}," +
           $"lights:{d.LightRejectCount},g2:{d.G2RejectCount},mega:{d.MegaGreebleRejectCount}," +
           $"fabric:{d.FabricRejectCount},density:{d.DensityRejectCount},cap:{d.CapRejectCount}; " +
           $"time={d.PlanningMilliseconds}+{d.MeshBuildMilliseconds}ms; signature={d.PlanSignature}";
}
