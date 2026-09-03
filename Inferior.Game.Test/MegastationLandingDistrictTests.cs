using Inferior.Game.Containers;
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
            $"L1d Nova: pads={result.LandingDistrictPlan.Diagnostics.StandardPadCount}+" +
            $"{result.LandingDistrictPlan.Diagnostics.LargePadCount}; " +
            $"apron={result.LandingDistrictPlan.Diagnostics.ApronSize.X:F0}x" +
            $"{result.LandingDistrictPlan.Diagnostics.ApronSize.Y:F0}m; " +
            $"services={result.LandingDistrictPlan.Diagnostics.ServiceBuildingCount}; " +
            $"lights={result.LandingDistrictPlan.Diagnostics.ArtificialLightCount}; " +
            $"loading={result.LandingDistrictPlan.Diagnostics.LoadingAreaCount}; " +
            $"containers={result.LandingDistrictPlan.Diagnostics.ContainerCount}; " +
            $"keepClear={result.LandingDistrictPlan.Diagnostics.KeepClearZoneCount}; " +
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
        Assert.Equal(.5f, MegastationLandingPadAssemblyStandards.PadSlabThickness);
        Assert.Equal(.5f, MegastationLandingPadAssemblyStandards.UnderPadClearGap);
        Assert.Equal(2.2f, MegastationLandingPadAssemblyStandards.PersonnelStairWidth);
        Assert.Equal(1.5f, MegastationLandingPadAssemblyStandards.PersonnelStairRun);
        Assert.Equal(6f, MegastationLandingPadAssemblyStandards.CargoRampWidth);
        Assert.Equal(8f, MegastationLandingPadAssemblyStandards.CargoRampRun);
        Assert.Equal(6f, MegastationLandingDistrictMeshBuilder.CargoDoorHeight);
        Assert.Equal(1.4f, MegastationLandingDistrictMeshBuilder.PersonnelDoorWidth);
        Assert.Equal(2.4f, MegastationLandingDistrictMeshBuilder.PersonnelDoorHeight);
        Assert.Equal(.20f, MegastationLandingDistrictMeshBuilder.StairRise);
        Assert.Equal(.30f, MegastationLandingDistrictMeshBuilder.StairTread);
        Assert.Equal(1.05f, MegastationLandingDistrictMeshBuilder.RailingHeight);
        Assert.Equal(1.84f, MegastationLandingDistrictMeshBuilder.HumanReferenceHeight);
        Assert.Equal(new Vector3(6f, 2.5f, 2.5f),
            MegastationLandingDistrictPlanner.StandardContainerSize);
    }

    [Fact]
    public void InstalledPadSurfaceAndOpenGapUseTheRequestedVerticalStack()
    {
        MegastationLandingDistrictPlan district = Result.Value.LandingDistrictPlan;
        float apronTop = Vector3.Dot(district.ApronCentre, district.FloorNormal)
            + MegastationLandingPadAssemblyStandards.ApronThickness * .5f;
        foreach (MegastationLandingPadPlan pad in district.Pads)
        {
            float padTop = Vector3.Dot(pad.PadSurface.Centre, district.FloorNormal);
            Assert.Equal(MegastationLandingPadAssemblyStandards.PadTopHeightAboveApron,
                padTop - apronTop, 3);
        }

        Assert.Equal(MegastationLandingPadAssemblyStandards.UnderPadClearGap,
            MegastationLandingPadAssemblyStandards.PadTopHeightAboveApron
                - MegastationLandingPadAssemblyStandards.PadSlabThickness,
            3);
    }

    [Fact]
    public void PadOwnedStairAndCargoRampStartAtServiceEdgeOutsideLandingFootprint()
    {
        MegastationLandingDistrictPlan district = Result.Value.LandingDistrictPlan;
        foreach (MegastationLandingPadPlan pad in district.Pads)
        {
            (Vector3 stair, Vector3 ramp, Vector3 service) =
                MegastationLandingPadAssemblyStandards.AccessAnchors(pad);
            Vector3 forward = pad.PadSurface.PreferredHeading;
            Vector3 right = pad.PadSurface.Right;
            float rear = Vector3.Dot(pad.PadSurface.Centre, forward)
                - pad.NominalSize.Y * .5f;

            Assert.True(Vector3.Dot(service, forward) < -.9999f);
            Assert.Equal(rear, Vector3.Dot(stair, forward), 3);
            Assert.Equal(rear, Vector3.Dot(ramp, forward), 3);
            Assert.True(MathF.Abs(Vector3.Dot(stair - pad.PadSurface.Centre, right))
                + MegastationLandingPadAssemblyStandards.PersonnelStairWidth * .5f
                < pad.NominalSize.X * .5f);
            Assert.True(MathF.Abs(Vector3.Dot(ramp - pad.PadSurface.Centre, right))
                + MegastationLandingPadAssemblyStandards.CargoRampWidth * .5f
                < pad.NominalSize.X * .5f);
            Assert.True(MegastationLandingPadAssemblyStandards.CargoRampRun
                <= pad.OperationalApron.ForwardMaximum - pad.OperationalApron.ForwardMinimum);
        }
    }

    [Fact]
    public void LoadingAreaContainsOrganizedStandardContainersAndStaysOutsideBerth()
    {
        MegastationLandingDistrictPlan district = Result.Value.LandingDistrictPlan;
        MegastationLoadingAreaPlan area = Assert.Single(district.LoadingAreas);
        Assert.Equal("LD-05", area.PadId);
        Assert.EndsWith("operations", area.ServiceBuildingIdentity, StringComparison.Ordinal);
        Assert.Equal("LOADING AREA 05", area.Label);
        Assert.Equal(new Vector2(28f, 12f), area.Size);
        Assert.Equal(.10f, MegastationLandingDistrictPlanner.LoadingAreaOutlineWidth);
        Assert.Equal(6, area.Containers.Count);

        foreach (MegastationLandingContainerPlan container in area.Containers)
        {
            Assert.Equal(MegastationLandingDistrictPlanner.StandardContainerSize, container.Size);
            Assert.True(area.Bounds.Contains(container.Footprint));
        }

        Assert.Equal(2, area.Containers.GroupBy(container => container.Footprint)
            .Single(group => group.Count() == 2).Count());
        float occupiedFraction = area.Containers.Select(container => container.Footprint)
            .Distinct().Sum(footprint =>
                (footprint.RightMaximum - footprint.RightMinimum)
                * (footprint.ForwardMaximum - footprint.ForwardMinimum))
            / (area.Size.X * area.Size.Y);
        Assert.InRange(occupiedFraction, .20f, .60f);
        MegastationLandingPadPlan pad = district.Pads.Single(candidate => candidate.PadId == area.PadId);
        Assert.False(area.Bounds.Intersects(pad.FutureBerthClearance));
    }

    [Fact]
    public void KeepClearGrammarIsSparsePurposefulAndSeparateFromStoredCargo()
    {
        MegastationLandingDistrictPlan district = Result.Value.LandingDistrictPlan;
        MegastationLoadingAreaPlan area = Assert.Single(district.LoadingAreas);
        Assert.Equal(4, district.KeepClearZones.Count);
        Assert.Equal(Enum.GetValues<MegastationKeepClearPurpose>().Order(),
            district.KeepClearZones.Select(zone => zone.Purpose).Order());
        Assert.Equal(2, district.KeepClearZones.Count(zone => zone.ShowLabel));

        foreach (MegastationKeepClearZonePlan zone in district.KeepClearZones)
        {
            Assert.False(area.Bounds.Intersects(zone.Bounds));
            Assert.All(area.Containers, container =>
                Assert.False(zone.Bounds.Intersects(container.Footprint)));
        }
    }

    [Fact]
    public void OperationalFloorPlanningIsDeterministicWithoutChangingBaseLighting()
    {
        MegastationPrototypeCpuResult result = Result.Value;
        MegastationLandingDistrictPlan replanned =
            MegastationLandingDistrictPlanner.Plan(result.InteriorPlan);

        Assert.Equal(result.LandingDistrictPlan.Diagnostics.Signature,
            replanned.Diagnostics.Signature);
        Assert.Equal(result.LandingDistrictPlan.LoadingAreas.Count, replanned.LoadingAreas.Count);
        for (int i = 0; i < replanned.LoadingAreas.Count; i++)
        {
            MegastationLoadingAreaPlan expected = result.LandingDistrictPlan.LoadingAreas[i];
            MegastationLoadingAreaPlan actual = replanned.LoadingAreas[i];
            Assert.Equal(expected with { Containers = actual.Containers }, actual);
            Assert.Equal(expected.Containers, actual.Containers);
        }
        Assert.Equal(result.LandingDistrictPlan.KeepClearZones, replanned.KeepClearZones);
        Assert.Equal(20, result.ArtificialLightingPlan.Lights.Count);
        Assert.Equal(8, result.LandingDistrictPlan.ArtificialLights.Count);
    }

    [Fact]
    public void ReusedContainerGeometryReceivesExistingStaticArtificialLight()
    {
        var (vertices, indices) = ShippingContainerFactory.GenerateVertices(
            Color.Gray, .2f, 12345, "TEST", LockGrade.Civilian);
        var mesh = new StationModuleMesh();
        mesh.MergeTransformed(vertices, indices, Matrix.Identity);
        Assert.Equal(0, mesh.FaceCount);

        mesh.SetVertexRangeArtificialLight(0, mesh.VertexCount,
            (_, _) => new Vector3(.25f, .5f, .75f));
        var (litVertices, _) = mesh.ToIntArrays();
        Assert.All(litVertices, vertex =>
        {
            Assert.InRange(vertex.ArtificialLight.R, (byte)63, (byte)64);
            Assert.InRange(vertex.ArtificialLight.G, (byte)127, (byte)128);
            Assert.InRange(vertex.ArtificialLight.B, (byte)191, (byte)192);
        });
    }

    [Fact]
    public void ScaleHumanUsesRaisedPadSurfaceAsFeetContactPlane()
    {
        MegastationLandingPadPlan pad = Result.Value.LandingDistrictPlan.Pads
            .Single(candidate => candidate.PadId == "LD-05");

        Vector3 feet = MegastationLandingDistrictMeshBuilder.ScaleHumanFeetPosition(pad);

        Assert.InRange(MathF.Abs(Vector3.Dot(
            feet - pad.PadSurface.Centre,
            pad.PadSurface.Normal)), 0f, 1e-5f);
    }

    [Fact]
    public void ScaleHumanFeetContactFloorPadAndStairSupportSurfaces()
    {
        MegastationLandingPadPlan pad = Result.Value.LandingDistrictPlan.Pads
            .Single(candidate => candidate.PadId == "LD-05");
        Vector3 normal = pad.PadSurface.Normal;
        Vector3 bayFloor = pad.PadSurface.Centre - normal
            * (MegastationLandingPadAssemblyStandards.PadTopHeightAboveApron
                + MegastationLandingPadAssemblyStandards.ApronThickness);
        (Vector3 stairTop, _, _) =
            MegastationLandingPadAssemblyStandards.AccessAnchors(pad);

        AssertContact(bayFloor);
        AssertContact(pad.PadSurface.Centre);
        AssertContact(stairTop);

        void AssertContact(Vector3 feet)
        {
            var mesh = new StationModuleMesh();
            MegastationLandingDistrictMeshBuilder.EmitScaleHuman(
                mesh, feet, normal, pad.PadSurface.PreferredHeading);
            var (vertices, _) = mesh.ToIntArrays();
            float minimumHeight = vertices.Min(vertex =>
                Vector3.Dot(vertex.Position - feet, normal));
            float maximumHeight = vertices.Max(vertex =>
                Vector3.Dot(vertex.Position - feet, normal));

            Assert.InRange(MathF.Abs(minimumHeight), 0f, 1e-5f);
            Assert.InRange(MathF.Abs(
                maximumHeight - MegastationLandingDistrictMeshBuilder.HumanReferenceHeight),
                0f, 1e-5f);
            Assert.All(vertices, vertex => Assert.True(
                Vector3.Dot(vertex.Position - feet, normal) >= -1e-5f));
        }
    }

    private static bool IsFinite(Vector3 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
