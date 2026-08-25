using Inferior.Game.StationGen;
using Inferior.Game.StationGen.Megastations;
using Inferior.Game.States;
using Inferior.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Xunit;

namespace Inferior.Game.Test;

public sealed class MegastationMegaGreebleTests
{
    private const string Nova = "Oranae:Oranae I:Nova Anchorage";
    private static readonly Lazy<MegastationPrototypeCpuResult> NovaResult =
        new(() => MegastationPrototypeGenerator.GenerateCpu(Nova));

    [Fact]
    public void NovaProducesBothFamiliesAsOneTextureFreeBatchedLayer()
    {
        MegastationPrototypeCpuResult result = NovaResult.Value;
        MegastationMegaGreeblePlan plan = result.MegaGreeblePlan;
        Assert.Contains(plan.Instances, i => i.Family == MegastationMegaGreebleFamily.SolarArray);
        Assert.Contains(plan.Instances, i => i.Family == MegastationMegaGreebleFamily.ParabolicAntenna);
        Assert.Contains(plan.Instances, i => i.Parameters is MegastationSolarParameters
            { Form: MegastationSolarForm.SurfaceArray });
        Assert.Contains(plan.Instances, i => i.Parameters is MegastationSolarParameters
            { Form: MegastationSolarForm.RadialSolarWing });
        Assert.False(result.MegaGreebleMesh.IsEmpty);
        Assert.Equal(0, plan.Diagnostics.OwnedTextureDelta);
        Assert.Equal(4, plan.Diagnostics.GpuBufferDelta);
        Assert.True(plan.Diagnostics.ShadowVertexCount > 0);
        Assert.True(plan.Diagnostics.ShadowVertexCount < plan.Diagnostics.VisibleVertexCount);
        PlacedModule module = Assert.IsType<PlacedModule>(
            MegastationPrototypeGenerator.CreateMegaGreebleModule(result));
        Assert.True(module.HasNativeMegastationMegaGreeble);
        Assert.Same(result.MegaGreebleMesh, module.Mesh);
        MegastationPlanarRegion[] solarRegions = result.PlanarRegions.Where(region =>
            region.ZoneRole is MegastationZoneRole.Utilities or MegastationZoneRole.Logistics
                or MegastationZoneRole.Industrial or MegastationZoneRole.Strategic).ToArray();
        Console.WriteLine($"solarTopology exposure={Distribution(solarRegions.Select(r=>r.Exposure))}; " +
            $"prominence={Distribution(solarRegions.Select(r=>r.Prominence))}; " +
            $"concavity={Distribution(solarRegions.Select(r=>r.Concavity))}; " +
            $"relativeDepth={Distribution(solarRegions.Select(r=>r.RelativeDepth))}");
        MegastationSolarParameters[] radialParameters=plan.Instances
            .Select(i=>i.Parameters).OfType<MegastationSolarParameters>()
            .Where(p=>p.Form==MegastationSolarForm.RadialSolarWing).ToArray();
        Assert.Contains(radialParameters,
            p=>p.FoldOrientation==MegastationSolarFoldOrientation.Radial);
        Assert.Contains(radialParameters,
            p=>p.FoldOrientation==MegastationSolarFoldOrientation.Transverse);
        Console.WriteLine($"radialAzimuthDegrees={string.Join(',',radialParameters.Select(p=>
            MathHelper.ToDegrees(p.AzimuthRadians).ToString("F1")))}; " +
            $"folds={string.Join(',',radialParameters.Select(p=>p.AccordionFoldCount))}; " +
            $"orientations={string.Join(',',radialParameters.Select(p=>p.FoldOrientation))}; " +
            $"paired={radialParameters.Count(p=>p.PairedWing)}; " +
            $"totalOutward={Distribution(radialParameters.Select(p=>p.RadialTotalProtrusion))}");
        Console.WriteLine(Summary(plan.Diagnostics));
    }

    [Fact]
    public void PlanIsDeterministicAndIndependentOfG2Traversal()
    {
        MegastationPrototypeCpuResult result = NovaResult.Value;
        MegastationMegaGreeblePlan replay = MegastationMegaGreeblePlanner.Plan(
            result.PlanarRegions.Reverse().ToArray(), result.AttachmentPlan,
            result.WindowPlan, result.LightPlan, result.InfrastructurePlan,
            result.RegularisedOccupancy, result.Style);
        Assert.Equal(result.MegaGreeblePlan.Diagnostics.PlanSignature,
            replay.Diagnostics.PlanSignature);
        Assert.Equal(result.MegaGreeblePlan.Instances, replay.Instances);
    }

    [Fact]
    public void ParameterRangesStayWithinFamilyContract()
    {
        foreach (MegastationMegaGreebleInstance instance in NovaResult.Value.MegaGreeblePlan.Instances)
        {
            switch (instance.Parameters)
            {
                case MegastationSolarParameters solar:
                    Assert.InRange(solar.Length, 20f, 72f);
                    Assert.InRange(solar.Width, 5f, 20f);
                    Assert.InRange(solar.SupportHeight, 2.8f, 8f);
                    if(solar.Form==MegastationSolarForm.RadialSolarWing)
                    {
                        Assert.InRange(solar.RadialWingHeight/solar.Width,6f,20f);
                        Assert.InRange(solar.AzimuthRadians,0f,MathF.Tau);
                        Assert.InRange(solar.AccordionFoldCount,7,13);
                        Assert.True(Enum.IsDefined(solar.FoldOrientation));
                    }
                    break;
                case MegastationDishParameters dish:
                    Assert.InRange(dish.Diameter, 20f, 100f);
                    Assert.True(dish.Depth > 0f);
                    break;
                default:
                    Assert.Fail("Unknown mega-greeble parameter family.");
                    break;
            }
        }
    }

    [Fact]
    public void EveryAcceptedFootprintHasExactPlanarSupport()
    {
        MegastationPrototypeCpuResult result = NovaResult.Value;
        var byId = result.PlanarRegions.ToDictionary(region => region.StableId);
        foreach (MegastationMegaGreebleInstance instance in result.MegaGreeblePlan.Instances)
            Assert.True(MegastationPlanarRegionExtractor.ContainsFootprint(
                byId[instance.SurfaceStableId], instance.MinU, instance.MaxU,
                instance.MinV, instance.MaxV, 2f), instance.Identity);
    }

    [Theory]
    [InlineData(1,0,0)] [InlineData(-1,0,0)]
    [InlineData(0,1,0)] [InlineData(0,-1,0)]
    [InlineData(0,0,1)] [InlineData(0,0,-1)]
    public void FamilyEmittersWorkOnAllSixSupportDirections(float x,float y,float z)
    {
        Vector3 normal = new(x,y,z);
        Vector3 u = MathF.Abs(normal.Y) < .9f ? Vector3.UnitY : Vector3.UnitX;
        u = Vector3.Normalize(Vector3.Cross(u,normal));
        Vector3 v = Vector3.Normalize(Vector3.Cross(normal,u));
        foreach (IMegastationMegaGreebleParameters parameters in new IMegastationMegaGreebleParameters[]
        {
            new MegastationSolarParameters(MegastationSolarForm.SurfaceArray,
                MegastationSolarArchetype.DoubleWing,50,10,5,.05f,8,2,true,
                0,40,9,MegastationSolarFoldOrientation.Radial,.8f,.4f,4.5f,true,false),
            new MegastationSolarParameters(MegastationSolarForm.RadialSolarWing,
                MegastationSolarArchetype.SingleWing,50,10,5,0,8,2,true,
                1.1f,80,9,MegastationSolarFoldOrientation.Transverse,.8f,.4f,4.5f,true,false),
            new MegastationDishParameters(MegastationDishArchetype.Supported,55,8,10,.2f,20,8,3),
            new MegastationDishParameters(MegastationDishArchetype.SurfaceMounted,70,10,0,0,20,8,3),
        })
        {
            var mesh = new StationModuleMesh();
            var instance = new MegastationMegaGreebleInstance("test",1,
                parameters is MegastationSolarParameters ? MegastationMegaGreebleFamily.SolarArray
                    : MegastationMegaGreebleFamily.ParabolicAntenna,
                "surface","zone",MegastationZoneRole.Utilities,Vector3.Zero,normal,u,v,
                -50,50,-50,50,80,parameters,Color.DarkBlue,Color.Gray,Color.Orange,true);
            MegastationMegaGreebleEmitters.Emit(instance,mesh);
            Assert.False(mesh.IsEmpty);
            var (vertices, indices) = mesh.ToIntArrays();
            Assert.NotEmpty(indices);
            Assert.All(vertices, vertex =>
            {
                Assert.True(float.IsFinite(vertex.Position.X));
                Assert.True(float.IsFinite(vertex.Position.Y));
                Assert.True(float.IsFinite(vertex.Position.Z));
                Assert.True(float.IsFinite(vertex.Normal.X));
                Assert.True(float.IsFinite(vertex.Normal.Y));
                Assert.True(float.IsFinite(vertex.Normal.Z));
            });
        }
    }

    [Fact]
    public void FamilySuitabilityUsesDistinctSemanticAndTopologyPolicies()
    {
        MegastationPlanarRegion utility = NovaResult.Value.PlanarRegions
            .First(region => region.ZoneRole == MegastationZoneRole.Utilities);
        MegastationPlanarRegion habitation = utility with { ZoneRole = MegastationZoneRole.Habitation };
        Assert.True(MegastationMegaGreeblePlanner.Suitability(
            MegastationMegaGreebleFamily.SolarArray, utility) > 0f);
        Assert.Equal(0f, MegastationMegaGreeblePlanner.Suitability(
            MegastationMegaGreebleFamily.SolarArray, habitation));
        Assert.Equal(0f, MegastationMegaGreeblePlanner.Suitability(
            MegastationMegaGreebleFamily.ParabolicAntenna, habitation));
        MegastationPlanarRegion open=utility with
        {
            Exposure=.52f,Prominence=.70f,Extremity=.65f,Concavity=.05f,RelativeDepth=.10f,
        };
        MegastationPlanarRegion ravine=open with
        {
            Exposure=.10f,Prominence=.10f,Extremity=.05f,Concavity=.70f,RelativeDepth=.78f,
        };
        MegastationSolarParameters surface=new(MegastationSolarForm.SurfaceArray,
            MegastationSolarArchetype.DoubleWing,50,10,5,0,8,2,true,0,40,9,
            MegastationSolarFoldOrientation.Radial,.8f,.4f,4.5f,true,false);
        MegastationSolarParameters radial=surface with { Form=MegastationSolarForm.RadialSolarWing };
        float baseSuitability=MegastationMegaGreeblePlanner.Suitability(
            MegastationMegaGreebleFamily.SolarArray,open);
        Assert.True(MegastationMegaGreeblePlanner.CandidateSuitability(
            MegastationMegaGreebleFamily.SolarArray,open,surface,baseSuitability)>0f);
        Assert.True(MegastationMegaGreeblePlanner.CandidateSuitability(
            MegastationMegaGreebleFamily.SolarArray,open,radial,baseSuitability)>0f);
        Assert.Equal(0f,MegastationMegaGreeblePlanner.CandidateSuitability(
            MegastationMegaGreebleFamily.SolarArray,ravine,surface,baseSuitability));
        Assert.Equal(0f,MegastationMegaGreeblePlanner.CandidateSuitability(
            MegastationMegaGreebleFamily.SolarArray,ravine,radial,baseSuitability));
    }

    [Fact]
    public void BatchedLayerUsesMediumAndFullPassesAndSelectiveShadowPolicy()
    {
        PlacedModule module = Assert.IsType<PlacedModule>(
            MegastationPrototypeGenerator.CreateMegaGreebleModule(NovaResult.Value));
        Assert.True(SystemSpaceState.UsesFullDecorationMeshInPass(module, DetailLevel.Full));
        Assert.True(SystemSpaceState.UsesFullDecorationMeshInPass(module, DetailLevel.Medium));
        Assert.False(SystemSpaceState.UsesFullDecorationMeshInPass(module, DetailLevel.Minimal));
        Assert.True(StationDecorator.DecorCastingPolicy[DecorClass.MegastationMegaGreebleMajor]);
        Assert.False(StationDecorator.DecorCastingPolicy[DecorClass.MegastationMegaGreebleMinor]);
        Assert.True(module.IsHullLessPresentationLayer);
    }

    [Fact]
    public void EveryMegaGreebleFormHasExplicitNonEmptySimplifiedCaster()
    {
        MegastationMegaGreeblePlan plan = NovaResult.Value.MegaGreeblePlan!;
        MegastationMegaGreebleMeshBuildResult result =
            MegastationMegaGreebleMeshBuilder.Build(plan);

        Assert.Equal(Enum.GetValues<MegastationMegaGreebleCasterFamily>().Length,
            MegastationMegaGreebleEmitters.ShadowPolicies.Count);
        Assert.Equal(Enum.GetValues<MegastationMegaGreebleCasterFamily>().Length,
            result.Diagnostics.ShadowByFamily.Count);
        Assert.All(result.Diagnostics.ShadowByFamily, family =>
        {
            Assert.Equal(MegastationShadowPolicy.Simplified, family.Policy);
            Assert.True(family.VisibleInstanceCount > 0,
                $"Nova fixture did not enumerate visible {family.Family} geometry.");
            Assert.Equal(family.VisibleInstanceCount, family.ShadowCastingInstanceCount);
            Assert.True(family.VisibleTriangleCount > 0);
            Assert.True(family.CasterVertexCount > 0);
            Assert.True(family.CasterTriangleCount > 0);
            Assert.True(family.CasterTriangleCount < family.VisibleTriangleCount);
        });
    }

    [Fact]
    public void DishShellHasPhysicalFrontBackAndCentralRearSupport()
    {
        Vector3 normal=Vector3.UnitZ,u=Vector3.UnitX,v=Vector3.UnitY;
        var parameters=new MegastationDishParameters(
            MegastationDishArchetype.Supported,60,9,10,.20f,20,8,3);
        var instance=Instance(parameters,normal,u,v);
        var mesh=new StationModuleMesh();
        MegastationMegaGreebleEmitters.Emit(instance,mesh);
        var (vertices,_)=mesh.ToIntArrays();
        Vector3 axis=Vector3.Normalize(normal*MathF.Cos(parameters.TiltRadians)
            +u*MathF.Sin(parameters.TiltRadians));
        float radius=parameters.Diameter*.5f;
        Vector3 frontTip=normal*parameters.PedestalHeight+axis*(radius*.14f);
        var shellVertices=vertices.Where(vertex=>
        {
            Vector3 offset=vertex.Position-frontTip;
            return (offset-axis*Vector3.Dot(offset,axis)).Length()>radius*.30f;
        }).ToArray();
        Assert.Contains(shellVertices,vertex=>Vector3.Dot(vertex.Normal,axis)>.20f);
        Assert.Contains(shellVertices,vertex=>Vector3.Dot(vertex.Normal,axis)<-.20f);
        Vector3 rearHub=frontTip-axis*(MathF.Max(.25f,radius*.012f)+radius*.035f);
        Vector3 connectorMid=(normal*parameters.PedestalHeight+rearHub)*.5f;
        Assert.Contains(vertices,vertex=>Vector3.Distance(vertex.Position,connectorMid)<2.5f);
    }

    [Fact]
    public void RadialWingIsTwoSidedAzimuthRotatedAndSelectivelyShadowed()
    {
        MegastationSolarParameters first=Radial(0f);
        MegastationSolarParameters rotated=Radial(1.20f);
        StationModuleMesh a=Emit(first), b=Emit(rotated);
        var (aVertices,_)=a.ToIntArrays();
        Vector3 front=Vector3.UnitY;
        Assert.Contains(aVertices,vertex=>vertex.Position.Z>10f&&Vector3.Dot(vertex.Normal,front)>.5f);
        Assert.Contains(aVertices,vertex=>vertex.Position.Z>10f&&Vector3.Dot(vertex.Normal,front)<-.5f);
        float aSpanX=aVertices.Max(v=>v.Position.X)-aVertices.Min(v=>v.Position.X);
        float aSpanY=aVertices.Max(v=>v.Position.Y)-aVertices.Min(v=>v.Position.Y);
        var (bVertices,_)=b.ToIntArrays();
        float bSpanX=bVertices.Max(v=>v.Position.X)-bVertices.Min(v=>v.Position.X);
        float bSpanY=bVertices.Max(v=>v.Position.Y)-bVertices.Min(v=>v.Position.Y);
        Assert.True(MathF.Abs(aSpanX-bSpanX)>5f||MathF.Abs(aSpanY-bSpanY)>5f);

        // The low foundation remains aligned to support U/V despite the collector azimuth.
        VertexPositionNormalColorTexture[] aBase=aVertices.Where(v=>v.Color==Color.Gray
            &&v.Position.Z<first.SupportHeight*.65f).ToArray();
        VertexPositionNormalColorTexture[] bBase=bVertices.Where(v=>v.Color==Color.Gray
            &&v.Position.Z<rotated.SupportHeight*.65f).ToArray();
        Assert.NotEmpty(aBase);
        Assert.Equal(Span(aBase,Vector3.UnitX),Span(bBase,Vector3.UnitX),3);
        Assert.Equal(Span(aBase,Vector3.UnitY),Span(bBase,Vector3.UnitY),3);

        StationMeshCpuData aShadow=Assert.IsType<StationMeshCpuData>(a.PrepareIndexRanges(
            a.DecorClassRanges.Where(r=>r.decorClass==DecorClass.MegastationMegaGreebleMajor)
                .Select(r=>(r.indexStart,r.indexCount)).ToArray()));
        StationMeshCpuData bShadow=Assert.IsType<StationMeshCpuData>(b.PrepareIndexRanges(
            b.DecorClassRanges.Where(r=>r.decorClass==DecorClass.MegastationMegaGreebleMajor)
                .Select(r=>(r.indexStart,r.indexCount)).ToArray()));
        Assert.True(MathF.Abs(Span(aShadow.Vertices,Vector3.UnitX)-Span(bShadow.Vertices,Vector3.UnitX))>5f
            ||MathF.Abs(Span(aShadow.Vertices,Vector3.UnitY)-Span(bShadow.Vertices,Vector3.UnitY))>5f);
        int major=a.DecorClassRanges.Where(r=>r.decorClass==DecorClass.MegastationMegaGreebleMajor)
            .Sum(r=>r.indexCount);
        int minor=a.DecorClassRanges.Where(r=>r.decorClass==DecorClass.MegastationMegaGreebleMinor)
            .Sum(r=>r.indexCount);
        Assert.True(major>0);
        Assert.True(minor>0);
        Assert.True(major<a.IndexCount);

        static float Span(IEnumerable<VertexPositionNormalColorTexture> vertices,Vector3 axis)
        {
            float[] projected=vertices.Select(v=>Vector3.Dot(v.Position,axis)).ToArray();
            return projected.Max()-projected.Min();
        }

        static MegastationSolarParameters Radial(float azimuth)=>new(
            MegastationSolarForm.RadialSolarWing,MegastationSolarArchetype.SingleWing,
            50,10,5,0,8,2,true,azimuth,80,9,
            MegastationSolarFoldOrientation.Radial,.8f,.4f,4.5f,true,false);
        static StationModuleMesh Emit(MegastationSolarParameters parameters)
        {
            var mesh=new StationModuleMesh();
            MegastationMegaGreebleEmitters.Emit(Instance(parameters,
                Vector3.UnitZ,Vector3.UnitX,Vector3.UnitY),mesh);
            return mesh;
        }
    }

    [Fact]
    public void RadialAndTransverseAccordionOrientationsEmitDifferentPhysicalFolds()
    {
        MegastationSolarParameters radial=new(
            MegastationSolarForm.RadialSolarWing,MegastationSolarArchetype.SingleWing,
            50,10,5,0,8,2,true,.65f,90,9,
            MegastationSolarFoldOrientation.Radial,.8f,.4f,4.5f,true,false);
        MegastationSolarParameters transverse=radial with
        {
            FoldOrientation=MegastationSolarFoldOrientation.Transverse,
        };
        StationModuleMesh radialMesh=new(), transverseMesh=new();
        MegastationMegaGreebleEmitters.Emit(Instance(radial,
            Vector3.UnitZ,Vector3.UnitX,Vector3.UnitY),radialMesh);
        MegastationMegaGreebleEmitters.Emit(Instance(transverse,
            Vector3.UnitZ,Vector3.UnitX,Vector3.UnitY),transverseMesh);
        Vector3[] radialFold=radialMesh.ToIntArrays().verts
            .Where(v=>v.Color==Color.DarkBlue).Select(v=>v.Position).ToArray();
        Vector3[] transverseFold=transverseMesh.ToIntArrays().verts
            .Where(v=>v.Color==Color.DarkBlue).Select(v=>v.Position).ToArray();

        Assert.NotEmpty(radialFold);
        Assert.Equal(radialFold.Length,transverseFold.Length);
        Assert.False(radialFold.SequenceEqual(transverseFold));
    }

    private static MegastationMegaGreebleInstance Instance(
        IMegastationMegaGreebleParameters parameters,Vector3 normal,Vector3 u,Vector3 v)
        =>new("test",1,parameters is MegastationSolarParameters
                ?MegastationMegaGreebleFamily.SolarArray:MegastationMegaGreebleFamily.ParabolicAntenna,
            "surface","zone",MegastationZoneRole.Utilities,Vector3.Zero,normal,u,v,
            -60,60,-60,60,100,parameters,Color.DarkBlue,Color.Gray,Color.Orange,true);

    private static string Summary(MegastationMegaGreebleDiagnostics d)
        => $"solar={d.SolarSurfaceArrayCount+d.SolarRadialWingCount}; forms={d.SolarSurfaceArrayCount}/{d.SolarRadialWingCount}; " +
           $"solarArchetypes={d.SolarSingleWingCount}/{d.SolarDoubleWingCount}/{d.SolarBroadCollectorCount}/{d.SolarSmallFieldCount}; " +
           $"dish={d.SupportedDishCount+d.SurfaceMountedDishCount}; " +
           $"dishArchetypes={d.SupportedDishCount}/{d.SurfaceMountedDishCount}; " +
           $"solarLengths={d.SolarMinimumLength:F1}/{d.SolarMedianLength:F1}/{d.SolarMaximumLength:F1}; " +
           $"radialHeights={d.RadialWingMinimumHeight:F1}/{d.RadialWingMedianHeight:F1}/{d.RadialWingMaximumHeight:F1}; " +
           $"radialWidths={d.RadialWingMinimumWidth:F1}/{d.RadialWingMedianWidth:F1}/{d.RadialWingMaximumWidth:F1}; " +
           $"radialFolds={d.RadialFoldOrientationCount}/{d.TransverseFoldOrientationCount}; " +
           $"dishDiameters={d.DishMinimumDiameter:F1}/{d.DishMedianDiameter:F1}/{d.DishMaximumDiameter:F1}; " +
           $"visible={d.VisibleVertexCount}v/{d.VisibleTriangleCount}t/{d.VisibleMeshBytes}B; " +
           $"shadow={d.ShadowVertexCount}v/{d.ShadowTriangleCount}t/{d.ShadowMeshBytes}B; " +
           $"shadowFamilies={string.Join(',', d.ShadowByFamily.Select(f=>$"{f.Family}:{f.Policy}:{f.ShadowCastingInstanceCount}/{f.VisibleInstanceCount}:{f.CasterVertexCount}v/{f.CasterTriangleCount}t"))}; " +
           $"planningMs={d.PlanningMilliseconds}; meshMs={d.MeshBuildMilliseconds}; " +
           $"families={string.Join(';',d.ByFamily.Select(p=>$"{p.Key}:regions={p.Value.EligibleRegionCount},area={p.Value.EligibleArea:F0},candidates={p.Value.CandidateCount},accepted={p.Value.AcceptedCount},rejects=mask:{p.Value.ExactMaskRejectCount}/g1:{p.Value.G1RejectCount}/window:{p.Value.WindowRejectCount}/light:{p.Value.LightRejectCount}/g2:{p.Value.G2RejectCount}/other:{p.Value.MegaGreebleRejectCount}/suitability:{p.Value.SuitabilityRejectCount}/clearance:{p.Value.OutwardClearanceRejectCount}/density:{p.Value.DensityRejectCount}/cap:{p.Value.CapRejectCount}"))}; " +
           $"largest={d.LargestInstanceWidth:F1}x{d.LargestInstanceLength:F1}x{d.LargestInstanceProtrusion:F1}; signature={d.PlanSignature}";

    private static string Distribution(IEnumerable<float> source)
    {
        float[] values=source.Order().ToArray();
        return $"{values[0]:F2}/{values[values.Length/2]:F2}/{values[^1]:F2}";
    }
}
