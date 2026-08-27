using Inferior.Game.StationGen;
using Inferior.Game.StationGen.Megastations;
using Xunit;

namespace Inferior.Game.Test;

public sealed class MegastationFabricTests
{
    private const string Nova = "Oranae:Oranae I:Nova Anchorage";
    private static readonly Lazy<MegastationPrototypeCpuResult> Result =
        new(() => MegastationPrototypeGenerator.GenerateCpu(Nova));

    [Fact]
    public void NovaProducesAllFabricFamiliesAsOneTextureFreeBatchedLayer()
    {
        MegastationPrototypeCpuResult result = Result.Value;
        MegastationFabricPlan plan = result.FabricPlan;
        Assert.All(Enum.GetValues<MegastationFabricArchetype>(), archetype =>
            Assert.Contains(plan.Instances, instance => instance.Archetype == archetype));
        Assert.True(plan.Instances.Count >= 200);
        Assert.False(result.FabricMesh.IsEmpty);
        Assert.Equal(0, plan.Diagnostics.OwnedTextureDelta);
        Assert.Equal(4, plan.Diagnostics.GpuBufferDelta);
        Assert.True(plan.Diagnostics.ShadowTriangleCount > 0);
        Assert.True(plan.Diagnostics.ShadowTriangleCount < plan.Diagnostics.VisibleTriangleCount);
        PlacedModule module = Assert.IsType<PlacedModule>(
            MegastationPrototypeGenerator.CreateFabricModule(result));
        Assert.True(module.HasNativeMegastationFabric);
        Assert.True(module.IsHullLessPresentationLayer);
        Assert.Same(result.FabricMesh, module.Mesh);
        Assert.DoesNotContain(plan.Instances, i => i.ZoneRole == MegastationZoneRole.Structural);
        Console.WriteLine(Summary(plan.Diagnostics));
    }

    [Fact]
    public void FabricPlanAndMeshAreTraversalIndependent()
    {
        MegastationPrototypeCpuResult result = Result.Value;
        MegastationFabricPlan replay = MegastationFabricPlanner.Plan(
            result.PlanarRegions.Reverse().ToArray(), result.AttachmentPlan,
            result.WindowPlan, result.LightPlan, result.InfrastructurePlan,
            result.MegaGreeblePlan, result.RegularisedOccupancy,
            result.ServiceChannelPlan);
        MegastationFabricMeshBuildResult mesh = MegastationFabricMeshBuilder.Build(replay);
        Assert.Equal(result.FabricPlan.Diagnostics.PlanSignature, replay.Diagnostics.PlanSignature);
        Assert.Equal(result.FabricPlan.Instances, replay.Instances);
        Assert.Equal(result.FabricMesh.ToIntArrays().verts, mesh.Mesh.ToIntArrays().verts);
        Assert.Equal(result.FabricMesh.ToIntArrays().indices, mesh.Mesh.ToIntArrays().indices);
    }

    [Fact]
    public void EveryFabricArchetypeHasExplicitSimplifiedCasterGeometry()
    {
        Assert.True(StationDecorator.DecorCastingPolicy[DecorClass.MegastationFabricMajor]);
        Assert.False(StationDecorator.DecorCastingPolicy[DecorClass.MegastationFabricMinor]);
        foreach (MegastationFabricArchetype archetype in Enum.GetValues<MegastationFabricArchetype>())
        {
            MegastationFabricInstance instance = Assert.Single(Result.Value.FabricPlan.Instances
                .Where(i => i.Archetype == archetype).Take(1));
            MegastationFabricMeshBuildResult built = MegastationFabricMeshBuilder.Build(
                new MegastationFabricPlan([instance], Result.Value.FabricPlan.Diagnostics, []));
            Assert.Contains(built.Mesh.DecorClassRanges,
                range => range.decorClass == DecorClass.MegastationFabricMajor && range.indexCount > 0);
            Assert.True(built.Diagnostics.ShadowTriangleCount > 0);
        }
    }

    [Fact]
    public void PhysicalSizesAndExactSupportRemainWithinFabricContract()
    {
        MegastationPrototypeCpuResult result = Result.Value;
        Dictionary<string, MegastationPlanarRegion> regions = result.PlanarRegions
            .ToDictionary(r => r.StableId);
        Assert.All(result.FabricPlan.Instances, instance =>
        {
            Assert.InRange(instance.Width, 8f, 65f);
            Assert.InRange(instance.Length, 8f, 65f);
            Assert.InRange(instance.Height, 8f, 65f);
            Assert.True(MegastationPlanarRegionExtractor.ContainsFootprint(
                regions[instance.SurfaceStableId], instance.MinU, instance.MaxU,
                instance.MinV, instance.MaxV, 1.25f));
        });
    }

    private static string Summary(MegastationFabricDiagnostics d)
        => $"fabric candidates={d.CandidateCount}; accepted={d.AcceptedCount}; " +
           $"archetypes={string.Join(',', d.ByArchetype.Select(x => $"{x.Key}:{x.Value}"))}; " +
           $"roles={string.Join(',', d.ByRole.Select(x => $"{x.Key}:{x.Value}"))}; " +
           $"patterns={string.Join(',', d.ByPattern.Select(x => $"{x.Key}:{x.Value}"))}; " +
           $"size={d.MinimumWidth:F1}/{d.MedianWidth:F1}/{d.MaximumWidth:F1} x " +
           $"{d.MinimumLength:F1}/{d.MedianLength:F1}/{d.MaximumLength:F1} x " +
           $"{d.MinimumHeight:F1}/{d.MedianHeight:F1}/{d.MaximumHeight:F1}; " +
           $"mesh={d.VisibleVertexCount}v/{d.VisibleTriangleCount}t/{d.VisibleMeshBytes}B; " +
           $"shadow={d.ShadowVertexCount}v/{d.ShadowTriangleCount}t/{d.ShadowMeshBytes}B; " +
           $"time={d.PlanningMilliseconds}+{d.MeshBuildMilliseconds}ms; " +
           $"dense={string.Join('|', d.DensestRegions.Select(x => $"{x.Role}:{x.Direction}:{x.StructureCount}@{x.Centre}"))}";
}
