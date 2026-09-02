using Inferior.Game.StationGen;
using Inferior.Game.StationGen.Megastations;
using Microsoft.Xna.Framework;
using Xunit;

namespace Inferior.Game.Test;

public sealed class MegastationLandingDistrictTests
{
    private const string Nova = "Oranae:Oranae I:Nova Anchorage";
    private static readonly Lazy<MegastationPrototypeCpuResult> Result =
        new(() => MegastationPrototypeGenerator.GenerateCpu(Nova));

    [Fact]
    public void DistrictUsesOneCoherentSixPadLayoutAndAuthoritativeEntranceFrame()
    {
        MegastationPrototypeCpuResult result = Result.Value;
        MegastationLandingDistrictPlan district = result.LandingDistrictPlan;

        Assert.Equal(6, district.Pads.Count);
        Assert.Equal(4, district.Pads.Count(pad => !pad.IsLarge));
        Assert.Equal(2, district.Pads.Count(pad => pad.IsLarge));
        Assert.All(district.Pads.Where(pad => !pad.IsLarge), pad =>
            Assert.Equal(new Vector2(36f, 36f), pad.NominalSize));
        Assert.All(district.Pads.Where(pad => pad.IsLarge), pad =>
            Assert.Equal(new Vector2(36f, 72f), pad.NominalSize));
        Assert.True(Vector3.Dot(district.FloorNormal, result.InteriorPlan.PortalUp) > .9999f);
        Assert.True(Vector3.Dot(district.PreferredHeading, result.InteriorPlan.OutwardNormal) > .9999f);
        Assert.All(district.Pads, pad =>
        {
            Assert.Equal(8, pad.FutureSupportPolygon.Count);
            Assert.True(Vector3.Dot(pad.PadSurface.PreferredHeading,
                result.InteriorPlan.OutwardNormal) > .9999f);
        });
    }

    [Fact]
    public void BerthReservationsIncludeFiveMetreMarginAndDoNotOverlap()
    {
        MegastationLandingDistrictPlan district = Result.Value.LandingDistrictPlan;
        foreach (MegastationLandingPadPlan pad in district.Pads)
        {
            MegastationBerthClearance berth = pad.FutureBerthClearance;
            Assert.Equal(pad.HardClearance, berth);
            Assert.Equal(pad.NominalSize.X + 10f,
                berth.RightMaximum - berth.RightMinimum, 3);
            Assert.Equal(pad.NominalSize.Y + 10f,
                berth.ForwardMaximum - berth.ForwardMinimum, 3);
            Assert.Equal(pad.NominalSize.X + 20f,
                pad.BuildingSetbackClearance.RightMaximum
                    - pad.BuildingSetbackClearance.RightMinimum, 3);
            Assert.Equal(pad.NominalSize.Y + 20f,
                pad.BuildingSetbackClearance.ForwardMaximum
                    - pad.BuildingSetbackClearance.ForwardMinimum, 3);
            Assert.True(pad.OperationalApron.ForwardMaximum
                - pad.OperationalApron.ForwardMinimum
                >= MegastationLandingDistrictPlanner.OperationalApronDepth);
        }
        for (int i = 0; i < district.Pads.Count; i++)
        for (int j = i + 1; j < district.Pads.Count; j++)
            Assert.False(district.Pads[i].FutureBerthClearance.Intersects(
                district.Pads[j].FutureBerthClearance));
    }

    [Fact]
    public void DistrictPlanAndLocalLightExtensionAreDeterministicAndIndependent()
    {
        MegastationPrototypeCpuResult result = Result.Value;
        MegastationLandingDistrictPlan replanned =
            MegastationLandingDistrictPlanner.Plan(result.InteriorPlan);
        MegastationArtificialLightingPlan baseline =
            MegastationArtificialLighting.Plan(result.InteriorPlan);

        Assert.Equal(result.LandingDistrictPlan.Diagnostics.Signature,
            replanned.Diagnostics.Signature);
        Assert.Equal(result.LandingDistrictPlan.Pads.Count, replanned.Pads.Count);
        for (int i = 0; i < replanned.Pads.Count; i++)
        {
            Assert.Equal(result.LandingDistrictPlan.Pads[i].PadId, replanned.Pads[i].PadId);
            Assert.Equal(result.LandingDistrictPlan.Pads[i].PadSurface,
                replanned.Pads[i].PadSurface);
            Assert.True(result.LandingDistrictPlan.Pads[i].FutureSupportPolygon.SequenceEqual(
                replanned.Pads[i].FutureSupportPolygon));
            Assert.Equal(result.LandingDistrictPlan.Pads[i].FutureBerthClearance,
                replanned.Pads[i].FutureBerthClearance);
            Assert.Equal(result.LandingDistrictPlan.Pads[i].HardClearance,
                replanned.Pads[i].HardClearance);
            Assert.Equal(result.LandingDistrictPlan.Pads[i].OperationalApron,
                replanned.Pads[i].OperationalApron);
            Assert.Equal(result.LandingDistrictPlan.Pads[i].BuildingSetbackClearance,
                replanned.Pads[i].BuildingSetbackClearance);
        }
        Assert.Equal(12, baseline.Lights.Count);
        Assert.Equal(20, result.ArtificialLightingPlan.Lights.Count);
        Assert.Equal(baseline.Lights, result.ArtificialLightingPlan.Lights.Take(12));
        Assert.Equal(result.LandingDistrictPlan.ArtificialLights,
            result.ArtificialLightingPlan.Lights.Skip(12));
    }

    [Fact]
    public void DistrictGeometryIsBatchedFiniteNonDegenerateAndHasSelectiveCaster()
    {
        MegastationPrototypeCpuResult result = Result.Value;
        var (vertices, indices) = result.InteriorMesh.ToIntArrays();

        Assert.True(result.InteriorPlan.Diagnostics.LandingDistrictVisibleVertexCount > 0);
        Assert.True(result.InteriorPlan.Diagnostics.LandingDistrictVisibleTriangleCount > 0);
        Assert.True(result.InteriorPlan.Diagnostics.LandingDistrictShadowTriangleCount > 0);
        Assert.Equal(3, result.LandingDistrictPlan.ServiceBuildings.Count);
        Console.WriteLine(
            $"L1b Nova: pads={result.LandingDistrictPlan.Diagnostics.StandardPadCount}+" +
            $"{result.LandingDistrictPlan.Diagnostics.LargePadCount}; " +
            $"apron={result.LandingDistrictPlan.Diagnostics.ApronSize.X:F0}x" +
            $"{result.LandingDistrictPlan.Diagnostics.ApronSize.Y:F0}m; " +
            $"services={result.LandingDistrictPlan.Diagnostics.ServiceBuildingCount}; " +
            $"lights={result.LandingDistrictPlan.Diagnostics.ArtificialLightCount}; " +
            $"mesh={result.LandingDistrictPlan.Diagnostics.VisibleVertexCount}v/" +
            $"{result.LandingDistrictPlan.Diagnostics.VisibleTriangleCount}t; " +
            $"caster={result.LandingDistrictPlan.Diagnostics.ShadowVertexCount}v/" +
            $"{result.LandingDistrictPlan.Diagnostics.ShadowTriangleCount}t; " +
            $"signature={result.LandingDistrictPlan.Diagnostics.Signature}");
        Assert.All(vertices, vertex =>
        {
            Assert.True(IsFinite(vertex.Position));
            Assert.True(IsFinite(vertex.Normal));
        });
        Assert.All(indices, index => Assert.InRange(index, 0, vertices.Length - 1));
        for (int i = 0; i < indices.Length; i += 3)
        {
            Vector3 a = vertices[indices[i]].Position;
            Vector3 b = vertices[indices[i + 1]].Position;
            Vector3 c = vertices[indices[i + 2]].Position;
            Assert.True(Vector3.Cross(b - a, c - a).LengthSquared() > 1e-8f);
        }
    }

    [Fact]
    public void DistrictAddsNoStationOwnedTexturesOrRuntimeLightObjects()
    {
        MegastationPrototypeCpuResult result = Result.Value;
        PlacedModule module = MegastationPrototypeGenerator.CreateInteriorModule(result);

        Assert.Null(module.TextureInstance);
        Assert.Null(module.MaterialInstance);
        Assert.Empty(module.GlowLights);
        Assert.True(module.UsesDecorationVertexIllumination);
    }

    [Fact]
    public void LandingBoxFrameRemainsRightHandedWhenCanonicalPortalRightIsFlipped()
    {
        Vector3 right = Vector3.UnitZ;
        Vector3 up = Vector3.UnitY;
        Vector3 preferredHeading = Vector3.UnitX;
        Matrix frame = MegastationLandingDistrictMeshBuilder.Frame(
            Vector3.Zero, right, up, preferredHeading);
        var mesh = new StationModuleMesh();
        mesh.AddOrientedBox(frame, new Vector3(12f, 8f, 20f), Color.White);

        Assert.True(frame.Determinant() > 0f);
        Assert.Equal(6, mesh.FaceCount);
        for (int face = 0; face < mesh.FaceCount; face++)
        {
            Vector3 centre = mesh.GetFaceBounds(face).center;
            Assert.True(Vector3.Dot(mesh.LocalFaceNormal(face), centre) > 0f);
        }
    }

    [Fact]
    public void EverySubstantialBuildingRespectsTenMetrePadSetbackAcrossSeeds()
    {
        MegastationInteriorPlan baseline = Result.Value.InteriorPlan;
        foreach (int seed in new[] { baseline.Seed, baseline.Seed + 1, baseline.Seed + 29 })
        {
            MegastationLandingDistrictPlan district =
                MegastationLandingDistrictPlanner.Plan(baseline with { Seed = seed });
            foreach (MegastationLandingPadPlan pad in district.Pads)
            foreach (MegastationLandingServiceBuilding building in district.ServiceBuildings)
            {
                MegastationBerthClearance footprint = MegastationLandingDistrictPlanner.Envelope(
                    building.Centre,
                    district.DistrictRight,
                    district.PreferredHeading,
                    building.Size.X,
                    building.Size.Z,
                    0f);
                Assert.False(pad.BuildingSetbackClearance.Intersects(footprint));
                Assert.False(pad.OperationalApron.Intersects(footprint));
            }
        }
    }

    [Fact]
    public void HumanCargoAndAccessCalibrationUsesRequestedPhysicalScale()
    {
        Assert.Equal(.5f, MegastationLandingDistrictMeshBuilder.BlastShieldSlabThickness);
        Assert.Equal(4f, MegastationLandingDistrictMeshBuilder.BlastShieldHeight);
        Assert.Equal(6f, MegastationLandingDistrictMeshBuilder.CargoDoorHeight);
        Assert.Equal(1.4f, MegastationLandingDistrictMeshBuilder.PersonnelDoorWidth);
        Assert.Equal(2.4f, MegastationLandingDistrictMeshBuilder.PersonnelDoorHeight);
        Assert.Equal(.20f, MegastationLandingDistrictMeshBuilder.StairRise);
        Assert.Equal(.30f, MegastationLandingDistrictMeshBuilder.StairTread);
        Assert.Equal(1.05f, MegastationLandingDistrictMeshBuilder.RailingHeight);
        Assert.Equal(1.84f, MegastationLandingDistrictMeshBuilder.HumanReferenceHeight);
        Assert.Equal(new Vector3(2.5f, 2.5f, 6f),
            MegastationLandingDistrictMeshBuilder.ContainerReferenceSize);
    }

    private static bool IsFinite(Vector3 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
