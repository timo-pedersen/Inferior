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
        MegastationInfrastructurePlan baselineInfrastructure =
            MegastationInfrastructurePlanner.Plan(result.PlanarRegions.Reverse().ToArray(),
                result.AttachmentPlan, result.WindowPlan, result.LightPlan);
        MegastationFabricPlan baselineFabric = MegastationFabricPlanner.Plan(
            result.PlanarRegions.Reverse().ToArray(), result.AttachmentPlan,
            result.WindowPlan, result.LightPlan, baselineInfrastructure,
            result.MegaGreeblePlan, result.RegularisedOccupancy);
        MegastationServiceChannelPlan replay = MegastationServiceChannelPlanner.Plan(
            result.PlanarRegions.Reverse().ToArray(), result.AttachmentPlan,
            result.WindowPlan, result.LightPlan, baselineInfrastructure,
            result.MegaGreeblePlan, baselineFabric);
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
    public void GeometryEndpointResolutionUsesTopologyIdentityWhenDistinctNodesAreVeryClose()
    {
        var runA = new MegastationServiceChannelRun("run-a", "route-a",
            MegastationServiceChannelRunScale.Primary,
            Vector2.Zero, new Vector2(10f, 0f), 10f, 2f, 4);
        var runB = new MegastationServiceChannelRun("run-b", "route-b",
            MegastationServiceChannelRunScale.Secondary,
            new Vector2(.05f, 0f), new Vector2(.05f, 10f), 10f, 2f, 4);
        MegastationServiceChannelNode Node(string id, Vector2 position, string run) => new(
            id, position, MegastationServiceChannelNodeKind.DeadEnd,
            MegastationServiceChannelNodeVariant.Exposed,
            MainAlongU: false, HousingWidth: 0f, HousingLength: 0f, HousingHeight: 0f,
            IncidentRunIdentities: [run], Endpoint: MegastationServiceChannelEndpoint.SealedCap);
        var network = new MegastationServiceChannelNetwork(
            "close-node-network", 1, "surface", "zone", MegastationZoneRole.Utilities,
            MegastationServiceChannelDensity.Light, GridDirection.PositiveZ,
            0f, Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY, 10f,
            [runA, runB],
            [
                Node("a-start", runA.Start, runA.Identity),
                Node("a-end", runA.End, runA.Identity),
                Node("b-start", runB.Start, runB.Identity),
                Node("b-end", runB.End, runB.Identity),
            ], []);
        var plan = new MegastationServiceChannelPlan([network],
            Result.Value.ServiceChannelPlan.Diagnostics, []);

        MegastationServiceChannelMeshBuildResult built =
            MegastationServiceChannelMeshBuilder.Build(plan, Result.Value.MaterialAssignment);

        Assert.False(built.Mesh.IsEmpty);
        AssertMesh(built.Mesh);
    }

    [Theory]
    [InlineData(MegastationServiceChannelNodeKind.Turn, 2)]
    [InlineData(MegastationServiceChannelNodeKind.TJunction, 3)]
    [InlineData(MegastationServiceChannelNodeKind.FourWay, 4)]
    public void ExposedTurnAndJunctionPiersReceiveARecessedRoof(
        MegastationServiceChannelNodeKind kind, int armCount)
    {
        Vector2[] directions = [Vector2.UnitX, Vector2.UnitY, -Vector2.UnitX, -Vector2.UnitY];
        MegastationServiceChannelRun[] runs = Enumerable.Range(0, armCount).Select(index =>
            new MegastationServiceChannelRun($"run:{index}", $"route:{index}",
                MegastationServiceChannelRunScale.Secondary, Vector2.Zero,
                directions[index] * 20f, 10f, 2f, 4)).ToArray();
        var nodes = new List<MegastationServiceChannelNode>
        {
            new("centre", Vector2.Zero, kind, MegastationServiceChannelNodeVariant.Exposed,
                MainAlongU: runs.Count(run => run.AlongU) >= 2,
                HousingWidth: 0f, HousingLength: 0f, HousingHeight: 0f,
                IncidentRunIdentities: runs.Select(run => run.Identity).ToArray(), Endpoint: null),
        };
        nodes.AddRange(runs.Select((run, index) => new MegastationServiceChannelNode(
            $"end:{index}", run.End, MegastationServiceChannelNodeKind.DeadEnd,
            MegastationServiceChannelNodeVariant.Exposed, run.AlongU, 0f, 0f, 0f,
            [run.Identity], MegastationServiceChannelEndpoint.SealedCap)));
        var network = new MegastationServiceChannelNetwork(
            $"roof:{kind}", 1, "surface", "zone", MegastationZoneRole.Utilities,
            MegastationServiceChannelDensity.Light, GridDirection.PositiveZ,
            0f, Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY, 10f, runs, nodes, []);
        var plan = new MegastationServiceChannelPlan([network],
            Result.Value.ServiceChannelPlan.Diagnostics, []);

        StationModuleMesh mesh = MegastationServiceChannelMeshBuilder.Build(
            plan, Result.Value.MaterialAssignment).Mesh;
        StationMeshCpuData cpu = new(mesh.ToIntArrays().verts, mesh.ToIntArrays().indices);

        Assert.True(cpu.Vertices.Count(vertex =>
            MathF.Abs(vertex.Position.Z - 2.225f) < .001f
            && MathF.Abs(MathF.Abs(vertex.Position.X) - 4f) < .001f
            && MathF.Abs(MathF.Abs(vertex.Position.Y) - 4f) < .001f) >= 4);
        AssertMesh(mesh);
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
    public void ParallelRunsWithOverlappingExtentsClearTheWidestChannel()
    {
        Assert.All(Result.Value.ServiceChannelPlan.Networks, network =>
        {
            for (int a = 0; a < network.Runs.Count; a++)
            for (int b = a + 1; b < network.Runs.Count; b++)
            {
                MegastationServiceChannelRun first = network.Runs[a];
                MegastationServiceChannelRun second = network.Runs[b];
                if (first.AlongU != second.AlongU)
                    continue;
                float first0 = first.AlongU ? MathF.Min(first.Start.X, first.End.X)
                    : MathF.Min(first.Start.Y, first.End.Y);
                float first1 = first.AlongU ? MathF.Max(first.Start.X, first.End.X)
                    : MathF.Max(first.Start.Y, first.End.Y);
                float second0 = second.AlongU ? MathF.Min(second.Start.X, second.End.X)
                    : MathF.Min(second.Start.Y, second.End.Y);
                float second1 = second.AlongU ? MathF.Max(second.Start.X, second.End.X)
                    : MathF.Max(second.Start.Y, second.End.Y);
                if (MathF.Min(first1, second1) - MathF.Max(first0, second0) <= .01f)
                    continue;
                float separation = first.AlongU
                    ? MathF.Abs(first.Start.Y - second.Start.Y)
                    : MathF.Abs(first.Start.X - second.Start.X);
                Assert.True(separation > MathF.Max(first.Width, second.Width),
                    $"{network.Identity}: {first.Identity} and {second.Identity} " +
                    $"are {separation:F2}m apart for {MathF.Max(first.Width, second.Width):F2}m width");
            }
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

    [Fact]
    public void Sc3CompositionIsSemanticSupportedReservedAndDensityNeutral()
    {
        MegastationPrototypeCpuResult result = Result.Value;
        MegastationInfrastructureDiagnostics g2 = result.InfrastructurePlan.Diagnostics;
        MegastationFabricDiagnostics fabric = result.FabricPlan.Diagnostics;
        Assert.Equal(g2.ClusterCount, g2.IndependentPlacementCount
            + g2.ChannelEdgePlacementCount + g2.ChannelNodePlacementCount
            + g2.ChannelEndpointPlacementCount);
        Assert.Equal(fabric.AcceptedCount, fabric.IndependentStructureCount
            + fabric.ChannelRowStructureCount + fabric.ChannelClusterStructureCount
            + fabric.ChannelNodeStructureCount + fabric.ChannelEndpointStructureCount);
        Assert.True(g2.ChannelEdgePlacementCount + g2.ChannelNodePlacementCount
            + g2.ChannelEndpointPlacementCount > 0);
        Assert.True(fabric.ChannelRowStructureCount + fabric.ChannelClusterStructureCount
            + fabric.ChannelNodeStructureCount + fabric.ChannelEndpointStructureCount > 0);
        Assert.True(g2.ChannelNodePlacementCount + fabric.ChannelNodeStructureCount > 0);
        Assert.True(g2.ChannelEndpointPlacementCount + fabric.ChannelEndpointStructureCount > 0);
        Assert.True(g2.IndependentPlacementCount > 0);
        Assert.True(fabric.IndependentStructureCount > 0);
        Assert.Contains(result.FabricPlan.Instances
                .Where(instance => instance.ChannelAssociation ==
                    MegastationChannelAssociationKind.ChannelEdge)
                .GroupBy(instance => instance.ChannelFeatureIdentity),
            group => group.Count() >= 2);

        Dictionary<string, MegastationPlanarRegion> regions = result.PlanarRegions
            .ToDictionary(region => region.StableId, StringComparer.Ordinal);
        Dictionary<string, MegastationServiceChannelRun> runs = result.ServiceChannelPlan.Runs
            .ToDictionary(run => run.Identity, StringComparer.Ordinal);
        HashSet<string> nodes = result.ServiceChannelPlan.Nodes
            .Select(node => node.Identity).ToHashSet(StringComparer.Ordinal);
        Assert.All(result.InfrastructurePlan.Clusters.Where(cluster =>
                cluster.ChannelAssociation != MegastationChannelAssociationKind.Independent), cluster =>
        {
            Assert.NotNull(cluster.ChannelFeatureIdentity);
            Assert.True(cluster.ChannelAssociation == MegastationChannelAssociationKind.ChannelEdge
                ? runs.ContainsKey(cluster.ChannelFeatureIdentity!)
                : nodes.Contains(cluster.ChannelFeatureIdentity!));
            MegastationPlanarRegion region = regions[cluster.SurfaceStableId];
            Assert.True(MegastationPlanarRegionExtractor.ContainsFootprint(region,
                cluster.MinU, cluster.MaxU, cluster.MinV, cluster.MaxV, 1f));
            Assert.False(MegastationChannelComposition.OverlapsReserved(region,
                result.ServiceChannelPlan, cluster.MinU, cluster.MaxU,
                cluster.MinV, cluster.MaxV, 3.6f));
            if (cluster.ChannelAssociation == MegastationChannelAssociationKind.ChannelEdge)
            {
                MegastationServiceChannelRun run = runs[cluster.ChannelFeatureIdentity!];
                Vector3 expected = run.AlongU ? region.TangentU : region.TangentV;
                Assert.True(MathF.Abs(Vector3.Dot(expected, cluster.TangentU)) > .999f);
            }
        });
        Assert.All(result.FabricPlan.Instances.Where(instance =>
                instance.ChannelAssociation != MegastationChannelAssociationKind.Independent), instance =>
        {
            Assert.NotNull(instance.ChannelFeatureIdentity);
            Assert.True(instance.ChannelAssociation == MegastationChannelAssociationKind.ChannelEdge
                ? runs.ContainsKey(instance.ChannelFeatureIdentity!)
                : nodes.Contains(instance.ChannelFeatureIdentity!));
            MegastationPlanarRegion region = regions[instance.SurfaceStableId];
            Assert.True(MegastationPlanarRegionExtractor.ContainsFootprint(region,
                instance.MinU, instance.MaxU, instance.MinV, instance.MaxV, 1.25f));
            Assert.False(MegastationChannelComposition.OverlapsReserved(region,
                result.ServiceChannelPlan, instance.MinU, instance.MaxU,
                instance.MinV, instance.MaxV, 3.6f));
            if (instance.ChannelAssociation == MegastationChannelAssociationKind.ChannelEdge)
            {
                MegastationServiceChannelRun run = runs[instance.ChannelFeatureIdentity!];
                Assert.Equal(run.AlongU, instance.Width >= instance.Length);
            }
        });

        MegastationInfrastructurePlan baselineInfrastructure =
            MegastationInfrastructurePlanner.Plan(result.PlanarRegions,
                result.AttachmentPlan, result.WindowPlan, result.LightPlan);
        MegastationFabricPlan baselineFabric = MegastationFabricPlanner.Plan(
            result.PlanarRegions, result.AttachmentPlan, result.WindowPlan, result.LightPlan,
            baselineInfrastructure, result.MegaGreeblePlan, result.RegularisedOccupancy);
        Assert.InRange(result.InfrastructurePlan.Clusters.Count,
            (int)(baselineInfrastructure.Clusters.Count * .70f), baselineInfrastructure.Clusters.Count);
        Assert.InRange(result.FabricPlan.Instances.Count,
            (int)(baselineFabric.Instances.Count * .70f), baselineFabric.Instances.Count);
        Assert.Equal(0, fabric.OwnedTextureDelta);
        Assert.True(result.ServiceChannelPlan.Diagnostics.RunsWithAdjacentG2Count > 0);
        Assert.True(result.ServiceChannelPlan.Diagnostics.RunsWithAdjacentFabricCount > 0);

        HashSet<string> channelSurfaces = result.ServiceChannelPlan.Networks
            .Select(network => network.SurfaceStableId).ToHashSet(StringComparer.Ordinal);
        int g2OnChannelSurfaces = result.InfrastructurePlan.Clusters.Count(cluster =>
            channelSurfaces.Contains(cluster.SurfaceStableId));
        int fabricOnChannelSurfaces = result.FabricPlan.Instances.Count(instance =>
            channelSurfaces.Contains(instance.SurfaceStableId));
        Console.WriteLine($"SC3 G2={g2.ClusterCount} " +
            $"(ind:{g2.IndependentPlacementCount},edge:{g2.ChannelEdgePlacementCount}," +
            $"node:{g2.ChannelNodePlacementCount},end:{g2.ChannelEndpointPlacementCount}," +
            $"channelSurface:{g2OnChannelSurfaces}); " +
            $"Fabric={fabric.AcceptedCount} (ind:{fabric.IndependentStructureCount}," +
            $"row:{fabric.ChannelRowStructureCount},cluster:{fabric.ChannelClusterStructureCount}," +
            $"node:{fabric.ChannelNodeStructureCount},end:{fabric.ChannelEndpointStructureCount}," +
            $"channelSurface:{fabricOnChannelSurfaces}); " +
            $"utilization=runsG2:{result.ServiceChannelPlan.Diagnostics.RunsWithAdjacentG2Count}," +
            $"runsFabric:{result.ServiceChannelPlan.Diagnostics.RunsWithAdjacentFabricCount}," +
            $"junctions:{result.ServiceChannelPlan.Diagnostics.JunctionsWithDevelopmentCount}," +
            $"endpoints:{result.ServiceChannelPlan.Diagnostics.EndpointsWithDevelopmentCount}");
    }

    [Fact]
    public void Sc3LeavesLockedMegaGreebleAndChannelPlansUnchanged()
    {
        MegastationPrototypeCpuResult result = Result.Value;
        MegastationInfrastructurePlan baselineInfrastructure =
            MegastationInfrastructurePlanner.Plan(result.PlanarRegions.Reverse().ToArray(),
                result.AttachmentPlan, result.WindowPlan, result.LightPlan);
        MegastationMegaGreeblePlan replayMega = MegastationMegaGreeblePlanner.Plan(
            result.PlanarRegions.Reverse().ToArray(), result.AttachmentPlan,
            result.WindowPlan, result.LightPlan, baselineInfrastructure,
            result.RegularisedOccupancy, result.Style);
        MegastationFabricPlan baselineFabric = MegastationFabricPlanner.Plan(
            result.PlanarRegions.Reverse().ToArray(), result.AttachmentPlan,
            result.WindowPlan, result.LightPlan, baselineInfrastructure,
            replayMega, result.RegularisedOccupancy);
        MegastationServiceChannelPlan replayChannels = MegastationServiceChannelPlanner.Plan(
            result.PlanarRegions.Reverse().ToArray(), result.AttachmentPlan,
            result.WindowPlan, result.LightPlan, baselineInfrastructure,
            replayMega, baselineFabric);

        Assert.Equal(result.MegaGreeblePlan.Diagnostics.PlanSignature,
            replayMega.Diagnostics.PlanSignature);
        Assert.Equal(result.MegaGreeblePlan.Instances, replayMega.Instances);
        Assert.Equal(result.ServiceChannelPlan.Diagnostics.PlanSignature,
            replayChannels.Diagnostics.PlanSignature);
        Assert.Equal(result.ServiceChannelPlan.Networks.Select(network => network.Identity),
            replayChannels.Networks.Select(network => network.Identity));
        Assert.Equal(result.ServiceChannelPlan.Runs.Select(run => run.Identity),
            replayChannels.Runs.Select(run => run.Identity));
        Assert.Equal(result.ServiceChannelPlan.Nodes.Select(node => node.Identity),
            replayChannels.Nodes.Select(node => node.Identity));
    }

    [Theory]
    [InlineData("Gaanis:Gaanis II:Omega Beacon")]
    [InlineData("Araris:Araris I:Swift Depot")]
    public void OtherMegastationsProduceDeterministicTextureFreeNetworks(string identity)
    {
        MegastationPrototypeCpuResult result = MegastationPrototypeGenerator.GenerateCpu(
            identity, systemMaterials: Materials);
        MegastationInfrastructurePlan baselineInfrastructure =
            MegastationInfrastructurePlanner.Plan(result.PlanarRegions.Reverse().ToArray(),
                result.AttachmentPlan, result.WindowPlan, result.LightPlan);
        MegastationFabricPlan baselineFabric = MegastationFabricPlanner.Plan(
            result.PlanarRegions.Reverse().ToArray(), result.AttachmentPlan,
            result.WindowPlan, result.LightPlan, baselineInfrastructure,
            result.MegaGreeblePlan, result.RegularisedOccupancy);
        MegastationServiceChannelPlan replay = MegastationServiceChannelPlanner.Plan(
            result.PlanarRegions.Reverse().ToArray(), result.AttachmentPlan,
            result.WindowPlan, result.LightPlan, baselineInfrastructure,
            result.MegaGreeblePlan, baselineFabric);
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
           $"rejects=mask:{d.ExactMaskRejectCount},parallel:{d.ParallelClearanceRejectCount}," +
           $"g1:{d.G1RejectCount},windows:{d.WindowRejectCount}," +
           $"lights:{d.LightRejectCount},g2:{d.G2RejectCount},mega:{d.MegaGreebleRejectCount}," +
           $"fabric:{d.FabricRejectCount},density:{d.DensityRejectCount},cap:{d.CapRejectCount}; " +
           $"time={d.PlanningMilliseconds}+{d.MeshBuildMilliseconds}ms; signature={d.PlanSignature}";
}
