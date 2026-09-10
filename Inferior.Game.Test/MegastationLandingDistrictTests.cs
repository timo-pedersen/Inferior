using Inferior.Game.Containers;
using Inferior.Game.StationGen;
using Inferior.Game.StationGen.Megastations;
using Microsoft.Xna.Framework;
using Xunit;

namespace Inferior.Game.Test;

[Trait("Category", "Slow")]
public sealed class MegastationLandingDistrictTests
{
    private const string Nova = "Oranae:Oranae I:Nova Anchorage";
    private static readonly Lazy<MegastationPrototypeCpuResult> Result =
        new(() => MegastationPrototypeGenerator.GenerateCpu(Nova));

    [Fact]
    public void DistrictUsesCoherentCardinalLandingSitesAndAuthoritativeFloorFrame()
    {
        MegastationPrototypeCpuResult result = Result.Value;
        MegastationLandingDistrictPlan district = result.LandingDistrictPlan;

        Assert.InRange(district.Sites.Count, 1, 3);
        Assert.True(district.Pads.Count >= 8);
        Assert.True(district.Pads.Count(pad => pad.IsLarge) >= 2);
        Assert.All(district.Pads.Where(pad => !pad.IsLarge), pad =>
            Assert.Equal(new Vector2(36f, 36f), pad.NominalSize));
        Assert.All(district.Pads.Where(pad => pad.IsLarge), pad =>
            Assert.Equal(new Vector2(36f, 72f), pad.NominalSize));
        Assert.True(Vector3.Dot(district.FloorNormal, result.InteriorPlan.PortalUp) > .9999f);
        Assert.Equal(district.Sites.Count, district.Diagnostics.SiteCount);
        foreach (MegastationLandingSitePlan site in district.Sites)
        {
            Assert.InRange(site.PadIds.Count, 1, 15);
            Assert.Equal(site.PadIds.Count <= 3 ? MegastationLandingSiteSize.Small
                : site.PadIds.Count <= 8 ? MegastationLandingSiteSize.Medium
                : MegastationLandingSiteSize.Large, site.SizeClass);
            Assert.Contains(site.Orientation,
                Enum.GetValues<MegastationLandingSiteOrientation>());
            Assert.True(Vector3.Dot(site.FloorNormal, district.FloorNormal) > .9999f);
            Assert.True(MathF.Abs(Vector3.Dot(site.Right, district.DistrictRight)) > .9999f
                || MathF.Abs(Vector3.Dot(site.Right, district.PreferredHeading)) > .9999f);
        }
        Assert.All(district.Pads, pad =>
        {
            Assert.Equal(8, pad.FutureSupportPolygon.Count);
            Assert.True(Vector3.Dot(pad.PadSurface.Normal, district.FloorNormal) > .9999f);
            Assert.Contains(district.Sites, site => site.PadIds.Contains(pad.PadId)
                && Vector3.Dot(site.PreferredHeading, pad.PadSurface.PreferredHeading) > .9999f);
        });
    }

    [Fact]
    public void SiteEnvelopesAreSeparatedAndSeedSampleExercisesL2aVocabulary()
    {
        MegastationInteriorPlan baseline = Result.Value.InteriorPlan;
        var siteCounts = new HashSet<int>();
        var sizes = new HashSet<MegastationLandingSiteSize>();
        var characters = new HashSet<MegastationLandingSiteCharacter>();
        var orientations = new HashSet<MegastationLandingSiteOrientation>();
        for (int offset = 0; offset < 96; offset++)
        {
            int sampledSeed = unchecked(baseline.Seed + offset * 0x45d9f3b);
            MegastationLandingDistrictPlan plan =
                MegastationLandingDistrictPlanner.Plan(baseline with { Seed = sampledSeed });
            siteCounts.Add(plan.Sites.Count);
            foreach (MegastationLandingSitePlan site in plan.Sites)
            {
                sizes.Add(site.SizeClass);
                characters.Add(site.Character);
                orientations.Add(site.Orientation);
            }
            for (int i = 0; i < plan.Sites.Count; i++)
            for (int j = i + 1; j < plan.Sites.Count; j++)
                if (plan.Sites[i].LandingSurfaceIdentity == plan.Sites[j].LandingSurfaceIdentity)
                    Assert.False(plan.Sites[i].InfrastructureEnvelope.Intersects(
                        plan.Sites[j].InfrastructureEnvelope));
            Assert.All(plan.Pads, pad =>
                Assert.True(Vector3.Dot(pad.PadSurface.Normal, plan.FloorNormal) > .9999f));
            if (plan.Diagnostics.PadCapacityDeficit == 0)
                Assert.True(plan.Pads.Count >= 8);
            if (plan.Diagnostics.LargePadCapacityDeficit == 0)
                Assert.True(plan.Pads.Count(pad => pad.IsLarge) >= 2);
        }

        Assert.Equal([1, 2, 3], siteCounts.Order());
        Assert.Equal(Enum.GetValues<MegastationLandingSiteSize>().Order(), sizes.Order());
        Assert.Equal(Enum.GetValues<MegastationLandingSiteCharacter>().Order(), characters.Order());
        Assert.Equal(Enum.GetValues<MegastationLandingSiteOrientation>().Order(),
            orientations.Order());
    }

    [Fact]
    public void ConstrainedFloorDegradesToObservableCapacityDeficitWithoutInvalidPlacement()
    {
        MegastationInteriorPlan baseline = Result.Value.InteriorPlan;
        Vector3 centre = (baseline.CavityEnvelope.Minimum + baseline.CavityEnvelope.Maximum) * .5f;
        MegastationInteriorVolume constrained = baseline.CavityEnvelope with
        {
            Minimum = centre - new Vector3(45f),
            Maximum = centre + new Vector3(45f),
        };
        MegastationLandingDistrictPlan plan = MegastationLandingDistrictPlanner.Plan(
            baseline with
            {
                CavityEnvelope = constrained,
                AddedStructuralSolids = null,
                AdditionalLandingSurfaces = null,
            });

        Assert.True(plan.Diagnostics.PadCapacityDeficit > 0);
        Assert.True(plan.Diagnostics.LargePadCapacityDeficit > 0);
        Assert.Empty(plan.Sites);
        Assert.Empty(plan.Pads);
    }

    [Fact]
    public void BerthReservationsIncludeFiveMetreMarginAndDoNotOverlap()
    {
        MegastationLandingDistrictPlan district = Result.Value.LandingDistrictPlan;
        foreach (MegastationLandingPadPlan pad in district.Pads)
        {
            MegastationBerthClearance berth = pad.FutureBerthClearance;
            Assert.Equal(pad.HardClearance, berth);
            Assert.Equal(MegastationLandingDistrictPlanner.OrientedEnvelope(
                pad.PadSurface.Centre, district.DistrictRight, district.PreferredHeading,
                pad.PadSurface.Right, pad.PadSurface.PreferredHeading,
                pad.NominalSize.X, pad.NominalSize.Y,
                MegastationLandingDistrictPlanner.BerthMargin), berth);
            Assert.Equal(MegastationLandingDistrictPlanner.OrientedEnvelope(
                pad.PadSurface.Centre, district.DistrictRight, district.PreferredHeading,
                pad.PadSurface.Right, pad.PadSurface.PreferredHeading,
                pad.NominalSize.X, pad.NominalSize.Y,
                MegastationLandingDistrictPlanner.BuildingSetback),
                pad.BuildingSetbackClearance);
            Assert.True(pad.OperationalApron.ForwardMaximum
                - pad.OperationalApron.ForwardMinimum
                >= MegastationLandingDistrictPlanner.OperationalApronDepth);
        }
        for (int i = 0; i < district.Pads.Count; i++)
        for (int j = i + 1; j < district.Pads.Count; j++)
        {
            MegastationLandingSitePlan first = district.Sites.Single(site =>
                site.PadIds.Contains(district.Pads[i].PadId));
            MegastationLandingSitePlan second = district.Sites.Single(site =>
                site.PadIds.Contains(district.Pads[j].PadId));
            if (first.LandingSurfaceIdentity == second.LandingSurfaceIdentity)
                Assert.False(district.Pads[i].FutureBerthClearance.Intersects(
                    district.Pads[j].FutureBerthClearance));
        }
    }

    [Fact]
    public void DistrictPlanAndLocalLightExtensionAreDeterministicAndIndependent()
    {
        MegastationPrototypeCpuResult result = Result.Value;
        MegastationLandingDistrictPlan replanned =
            MegastationLandingDistrictPlanner.Plan(
                result.InteriorPlan, result.RegularisedOccupancy,
                result.MegaShelfPlan, forceLandingSiteOnShelf: true);
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
        Assert.Equal(baseline.Lights.Count + result.LandingDistrictPlan.ArtificialLights.Count
                + result.BayFacilityPlan.ArtificialLights.Count,
            result.ArtificialLightingPlan.Lights.Count);
        Assert.Equal(baseline.Lights, result.ArtificialLightingPlan.Lights.Take(12));
        Assert.Equal(result.LandingDistrictPlan.ArtificialLights,
            result.ArtificialLightingPlan.Lights.Skip(12)
                .Take(result.LandingDistrictPlan.ArtificialLights.Count));
        Assert.Equal(result.BayFacilityPlan.ArtificialLights,
            result.ArtificialLightingPlan.Lights.Skip(
                12 + result.LandingDistrictPlan.ArtificialLights.Count));
    }

    [Fact]
    public void ProductionSitesClearAuthoritativeThreeDimensionalStructure()
    {
        MegastationPrototypeCpuResult result = Result.Value;
        MegastationLandingDistrictPlan district = result.LandingDistrictPlan;
        MegastationLandingSiteStructuralClearance clearance =
            MegastationLandingSiteStructuralClearance.Create(
                result.RegularisedOccupancy, result.InteriorPlan);

        foreach (MegastationLandingServiceBuilding building in district.ServiceBuildings)
            Assert.True(clearance.IsOrientedBoxClear(
                building.Centre,
                building.Frontage.Right,
                building.Frontage.Up,
                building.Frontage.Normal,
                building.Size,
                out string blocker),
                $"{building.Identity} intersects {blocker}");

        Assert.True(district.Diagnostics.StructuralEnvelopeRejectCount > 0);
        Assert.Equal(0, district.Diagnostics.StructuralComponentRejectCount);
    }

    [Fact]
    public void StructuralMassOverClearPadPlanRejectsWholeSiteAndReplansDeterministically()
    {
        MegastationPrototypeCpuResult result = Result.Value;
        MegastationInteriorPlan interior = result.InteriorPlan;
        MegastationLandingDistrictPlan unvalidated =
            MegastationLandingDistrictPlanner.Plan(interior);
        MegastationLandingServiceBuilding obstructed = unvalidated.ServiceBuildings[0];
        SliceGrid grid = result.RegularisedOccupancy.Grid;
        (int x, int y, int z) = CellContaining(grid, obstructed.Centre);
        var obstacle = new StructuralOccupancy(grid);
        obstacle.MarkStructural(x, y, z);

        MegastationLandingSiteStructuralClearance clearance =
            MegastationLandingSiteStructuralClearance.Create(obstacle, interior);
        Assert.False(clearance.IsOrientedBoxClear(
            obstructed.Centre,
            obstructed.Frontage.Right,
            obstructed.Frontage.Up,
            obstructed.Frontage.Normal,
            obstructed.Size,
            out _));
        Assert.Contains(unvalidated.Pads, pad =>
        {
            float floor = FloorHeight(interior);
            MegastationBerthClearance footprint =
                MegastationLandingDistrictPlanner.OrientedEnvelope(
                    pad.PadSurface.Centre,
                    unvalidated.DistrictRight,
                    unvalidated.PreferredHeading,
                    pad.PadSurface.Right,
                    pad.PadSurface.PreferredHeading,
                    pad.NominalSize.X,
                    pad.NominalSize.Y,
                    0f);
            return clearance.IsPrismClear(
                footprint, floor, floor + 2f,
                unvalidated.DistrictRight,
                unvalidated.PreferredHeading,
                unvalidated.FloorNormal,
                out _);
        });

        MegastationLandingDistrictPlan replanned =
            MegastationLandingDistrictPlanner.Plan(interior, obstacle);
        MegastationLandingDistrictPlan repeated =
            MegastationLandingDistrictPlanner.Plan(interior, obstacle);

        Assert.NotEqual(unvalidated.Diagnostics.Signature, replanned.Diagnostics.Signature);
        Assert.Equal(replanned.Diagnostics.Signature, repeated.Diagnostics.Signature);
        Assert.True(replanned.Diagnostics.StructuralEnvelopeRejectCount > 0);
        Assert.All(replanned.ServiceBuildings, building =>
            Assert.True(clearance.IsOrientedBoxClear(
                building.Centre,
                building.Frontage.Right,
                building.Frontage.Up,
                building.Frontage.Normal,
                building.Size,
                out _)));
    }

    [Fact]
    public void DistrictGeometryIsBatchedFiniteNonDegenerateAndHasSelectiveCaster()
    {
        MegastationPrototypeCpuResult result = Result.Value;
        var (vertices, indices) = result.InteriorMesh.ToIntArrays();

        Assert.True(result.InteriorPlan.Diagnostics.LandingDistrictVisibleVertexCount > 0);
        Assert.True(result.InteriorPlan.Diagnostics.LandingDistrictVisibleTriangleCount > 0);
        Assert.True(result.InteriorPlan.Diagnostics.LandingDistrictShadowTriangleCount > 0);
        Assert.NotEmpty(result.LandingDistrictPlan.ServiceBuildings);
        Console.WriteLine(
            $"L2a Nova: sites={result.LandingDistrictPlan.Diagnostics.SiteSummary}; " +
            $"pads={result.LandingDistrictPlan.Diagnostics.StandardPadCount}+" +
            $"{result.LandingDistrictPlan.Diagnostics.LargePadCount}; " +
            $"apron={result.LandingDistrictPlan.Diagnostics.ApronSize.X:F0}x" +
            $"{result.LandingDistrictPlan.Diagnostics.ApronSize.Y:F0}m; " +
            $"services={result.LandingDistrictPlan.Diagnostics.ServiceBuildingCount}; " +
            $"cargoDoors={result.LandingDistrictPlan.ServiceBuildings.Sum(building => building.Frontage.CargoEntrances.Count)}; " +
            $"personnelDoors={result.LandingDistrictPlan.ServiceBuildings.Sum(building => building.Frontage.PersonnelEntrances.Count)}; " +
            $"windowGroups={result.LandingDistrictPlan.ServiceBuildings.Sum(building => building.Frontage.WindowGroups.Count)}; " +
            $"facilityIds={result.LandingDistrictPlan.ServiceBuildings.Count(building => building.Frontage.FacilityIdentifier is not null)}; " +
            $"facadeLights={result.LandingDistrictPlan.ServiceBuildings.Sum(building => building.Frontage.Floodlights.Count)}; " +
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
    public void ApronReceiverGridCoversExactSurfaceWithContinuousUvsAndPadSamples()
    {
        MegastationPrototypeCpuResult generated = Result.Value;
        MegastationLandingDistrictPlan plan = generated.LandingDistrictPlan;
        var mesh = new StationModuleMesh();
        MegastationLandingDistrictMeshResult result =
            MegastationLandingDistrictMeshBuilder.Append(
                mesh, plan, generated.MaterialAssignment);
        var (vertices, _) = mesh.ToIntArrays();
        var repeatedMesh = new StationModuleMesh();
        MegastationLandingDistrictMeshBuilder.Append(
            repeatedMesh, plan, generated.MaterialAssignment);
        Assert.Equal(mesh.ToIntArrays().verts, repeatedMesh.ToIntArrays().verts);
        Assert.Equal(mesh.ToIntArrays().indices, repeatedMesh.ToIntArrays().indices);
        Vector3 up = Vector3.Normalize(plan.FloorNormal);
        Vector3 arbitrary = MathF.Abs(up.Y) < .85f ? Vector3.UnitY : Vector3.UnitX;
        Vector3 textureU = Vector3.Normalize(Vector3.Cross(up, arbitrary));
        Vector3 textureV = Vector3.Normalize(Vector3.Cross(up, textureU));
        float tileSize = SystemMaterialRecipes.Get(
            SystemMaterialFamilyId.HeavyIndustrialPlate).TileSizeMeters;

        Assert.Equal(plan.Sites.Count, result.ApronReceiverRanges.Count);
        Assert.Equal(result.ApronReceiverFaceCount * 2,
            result.Diagnostics.ApronReceiverTriangleCount);
        int totalFaces = 0;
        int totalVertices = 0;
        for (int i = 0; i < plan.Sites.Count; i++)
        {
            MegastationLandingSitePlan site = plan.Sites[i];
            var range = result.ApronReceiverRanges[i];
            Vector3 right = Vector3.Normalize(site.Right);
            Vector3 depth = Vector3.Normalize(Vector3.Cross(right, up));
            float halfWidth = site.ApronSize.X * .5f;
            float halfDepth = site.ApronSize.Y * .5f;
            Vector3 origin = site.ApronCentre - right * halfWidth
                + up * (MegastationLandingPadAssemblyStandards.ApronThickness * .5f)
                + depth * halfDepth;
            var receiverVertices = vertices.Skip(range.FirstVertex)
                .Take(range.VertexCount).ToArray();
            int expectedColumns = (int)MathF.Ceiling(
                site.ApronSize.X / MegastationLandingDistrictMeshBuilder.ApronReceiverSpacing);
            int expectedRows = (int)MathF.Ceiling(
                site.ApronSize.Y / MegastationLandingDistrictMeshBuilder.ApronReceiverSpacing);
            Assert.Equal(expectedColumns * expectedRows, range.FaceCount);
            Assert.Equal(range.FaceCount * 4, receiverVertices.Length);
            totalFaces += range.FaceCount;
            totalVertices += range.VertexCount;

            foreach (var vertex in receiverVertices)
            {
                Vector3 offset = vertex.Position - origin;
                float localRight = Vector3.Dot(offset, right);
                float localDepth = -Vector3.Dot(offset, depth);
                Assert.InRange(localRight, -.001f, site.ApronSize.X + .001f);
                Assert.InRange(localDepth, -.001f, site.ApronSize.Y + .001f);
                Assert.True(Vector3.Dot(vertex.Normal, up) > .9999f);
                Vector2 expectedUv = new(
                    Vector3.Dot(offset, textureU) / tileSize,
                    Vector3.Dot(offset, textureV) / tileSize);
                Assert.InRange(Vector2.Distance(vertex.TextureCoordinate, expectedUv), 0f, 1e-5f);
            }

            for (int face = range.FirstFace; face < range.FirstFace + range.FaceCount; face++)
            {
                Vector3[] points = mesh.GetFaceVertexPositions(face);
                Assert.Equal(4, points.Length);
                Assert.True(Vector3.Dot(mesh.LocalFaceNormal(face), up) > .9999f);
                Assert.True(Vector3.Cross(points[1] - points[0], points[2] - points[0])
                    .LengthSquared() > 1e-8f);
            }
        }
        Assert.Equal(totalFaces, result.ApronReceiverFaceCount);
        Assert.Equal(totalVertices, result.ApronReceiverVertexCount);
        Assert.Equal(result.ApronReceiverFaceCount * 2,
            result.Diagnostics.ApronReceiverTriangleCount);
        Assert.Equal(plan.Pads.Count, result.PadTopReceiverRanges.Count);
        Assert.All(result.PadTopReceiverRanges, range =>
        {
            Assert.True(range.FirstFace >= result.FirstFace);
            Assert.True(range.FaceCount > 100);
            Assert.True(range.FirstFace + range.FaceCount
                <= result.FirstFace + result.FaceCount);
        });
        Assert.InRange(result.Diagnostics.ApronReceiverMaximumSpacing, 0f,
            MegastationLandingDistrictMeshBuilder.ApronReceiverSpacing);

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
                MegastationBerthClearance footprint =
                    MegastationLandingDistrictPlanner.OrientedEnvelope(
                        building.Centre, district.DistrictRight, district.PreferredHeading,
                        building.Frontage.Right, building.Frontage.Normal,
                        building.Size.X, building.Size.Z, 0f);
                Assert.False(pad.BuildingSetbackClearance.Intersects(footprint));
                Assert.False(pad.OperationalApron.Intersects(footprint));
            }
        }
    }

    [Fact]
    public void ServiceBuildingsExposeSparseLandingFacingOperationalFrontages()
    {
        MegastationLandingDistrictPlan district = Result.Value.LandingDistrictPlan;
        Assert.NotEmpty(district.ServiceBuildings);

        foreach (MegastationLandingServiceBuilding building in district.ServiceBuildings)
        {
            MegastationOperationalFrontagePlan frontage = building.Frontage;
            MegastationLandingSitePlan site = district.Sites.Single(candidate =>
                candidate.Identity == building.SiteIdentity);
            Assert.True(Vector3.Dot(frontage.Normal, site.PreferredHeading) > .9999f);
            Assert.True(Vector3.Dot(frontage.Right, site.Right) > .9999f);
            Assert.True(Vector3.Dot(frontage.Up, district.FloorNormal) > .9999f);
            Assert.NotEmpty(frontage.ServedPadIds);
            Assert.All(frontage.ServedPadIds, id =>
                Assert.Contains(district.Pads, pad => pad.PadId == id));
            Assert.InRange(frontage.PersonnelEntrances.Count, 1, 3);
            Assert.InRange(frontage.CargoEntrances.Count, 1, 2);
            Assert.InRange(frontage.WindowGroups.Count, 1, 3);

            foreach (MegastationPersonnelEntrancePlan entrance in frontage.PersonnelEntrances)
            {
                Assert.Equal(new Vector2(1.4f, 2.4f), entrance.Size);
                AssertOnFrontage(entrance.Centre);
            }
            foreach (MegastationCargoEntrancePlan entrance in frontage.CargoEntrances)
            {
                Assert.InRange(entrance.Size.Y, 5.5f, 6.5f);
                Assert.True(entrance.Size.X >= 8.5f);
                AssertOnFrontage(entrance.Centre);
            }
            foreach (MegastationFrontageWindowGroupPlan group in frontage.WindowGroups)
            {
                Assert.InRange(group.WindowSize.X, 1f, 2f);
                Assert.InRange(group.WindowSize.Y, 1f, 1.5f);
                Assert.InRange(group.WindowCount, 3, 5);
                AssertOnFrontage(group.Centre);
            }
            if (frontage.FacilityIdentifier is { } identifier)
            {
                MegastationCargoEntrancePlan anchor = frontage.CargoEntrances[0];
                float labelBaseline = Vector3.Dot(identifier.Origin, frontage.Up);
                float assemblyTop = Vector3.Dot(anchor.Centre, frontage.Up)
                    + anchor.Size.Y * .5f
                    + MegastationLandingDistrictMeshBuilder.CargoDoorFrameThickness;
                Assert.Equal(MegastationLandingDistrictMeshBuilder.FacilityLabelClearance,
                    labelBaseline - assemblyTop, 3);
                var (_, textUp, textNormal) = PlanarTextGeometry.DeriveFrame(
                    frontage.Normal, identifier.ReadingDirection);
                Assert.True(Vector3.Dot(textUp, frontage.Up) > .9999f);
                Assert.True(Vector3.Dot(textNormal, frontage.Normal) > .9999f);
            }

            void AssertOnFrontage(Vector3 point)
            {
                float depth = Vector3.Dot(point - building.Centre, frontage.Normal);
                Assert.Equal(building.Size.Z * .5f, depth, 3);
            }
        }
    }

    [Fact]
    public void OperationsLoadingAreaAndKeepClearZonesConsumePlannedFrontageEntrances()
    {
        MegastationLandingDistrictPlan district = Result.Value.LandingDistrictPlan;
        Assert.Equal(district.Sites.Count, district.LoadingAreas.Count);
        foreach (MegastationLoadingAreaPlan area in district.LoadingAreas)
        {
            MegastationLandingServiceBuilding building = district.ServiceBuildings.Single(candidate =>
                candidate.Identity == area.ServiceBuildingIdentity);
            Assert.Contains(area.PadId, building.Frontage.ServedPadIds);
            MegastationCargoEntrancePlan cargo = building.Frontage.CargoEntrances[0];
            MegastationPersonnelEntrancePlan personnel = building.Frontage.PersonnelEntrances[0];
            MegastationKeepClearZonePlan cargoZone = district.KeepClearZones.Single(zone =>
                zone.SiteIdentity == area.SiteIdentity
                && zone.Purpose == MegastationKeepClearPurpose.CargoDoor);
            MegastationKeepClearZonePlan personnelZone = district.KeepClearZones.Single(zone =>
                zone.SiteIdentity == area.SiteIdentity
                && zone.Purpose == MegastationKeepClearPurpose.PersonnelDoor);
            Vector3 cargoFloor = cargo.Centre - district.FloorNormal * (cargo.Size.Y * .5f);
            Vector3 personnelFloor = personnel.Centre
                - district.FloorNormal * (personnel.Size.Y * .5f);

            AssertVector(cargoFloor + area.PreferredHeading * 2.5f, cargoZone.Centre);
            AssertVector(personnelFloor + area.PreferredHeading * 1.5f, personnelZone.Centre);
            Assert.False(area.Bounds.Intersects(cargoZone.Bounds));
            Assert.False(area.Bounds.Intersects(personnelZone.Bounds));
        }
    }

    [Fact]
    public void FrontageVariationAndFacadeFloodlightsAreDeterministic()
    {
        MegastationPrototypeCpuResult result = Result.Value;
        MegastationLandingDistrictPlan replanned =
            MegastationLandingDistrictPlanner.Plan(
                result.InteriorPlan, result.RegularisedOccupancy,
                result.MegaShelfPlan, forceLandingSiteOnShelf: true);

        Assert.Equal(result.LandingDistrictPlan.ServiceBuildings.Count,
            replanned.ServiceBuildings.Count);
        for (int i = 0; i < replanned.ServiceBuildings.Count; i++)
        {
            MegastationLandingServiceBuilding expected =
                result.LandingDistrictPlan.ServiceBuildings[i];
            MegastationLandingServiceBuilding actual = replanned.ServiceBuildings[i];
            Assert.Equal(expected.Identity, actual.Identity);
            Assert.Equal(expected.Centre, actual.Centre);
            Assert.Equal(expected.Size, actual.Size);
            Assert.Equal(expected.Frontage.Identity, actual.Frontage.Identity);
            Assert.Equal(expected.Frontage.Seed, actual.Frontage.Seed);
            Assert.Equal(expected.Frontage.ServedPadIds, actual.Frontage.ServedPadIds);
            Assert.Equal(expected.Frontage.PersonnelEntrances,
                actual.Frontage.PersonnelEntrances);
            Assert.Equal(expected.Frontage.CargoEntrances, actual.Frontage.CargoEntrances);
            Assert.Equal(expected.Frontage.WindowGroups, actual.Frontage.WindowGroups);
            Assert.Equal(expected.Frontage.Floodlights, actual.Frontage.Floodlights);
            Assert.Equal(expected.Frontage.FacilityIdentifier,
                actual.Frontage.FacilityIdentifier);
        }
        Assert.Equal(result.LandingDistrictPlan.Pads.Count * 5
                + result.LandingDistrictPlan.Sites.Count * 2
                + result.LandingDistrictPlan.ServiceBuildings.Count * 2,
            result.LandingDistrictPlan.ArtificialLights.Count);
        Assert.Equal(12 + result.LandingDistrictPlan.ArtificialLights.Count
                + result.BayFacilityPlan.ArtificialLights.Count,
            result.ArtificialLightingPlan.Lights.Count);
        Assert.All(result.LandingDistrictPlan.ServiceBuildings, building =>
            Assert.Contains("frontage", building.Frontage.Identity, StringComparison.Ordinal));
    }

    [Fact]
    public void EveryActiveFrontageProducesTwoFiniteWorkingAreaFloodlightsOutsideBuildings()
    {
        MegastationLandingDistrictPlan district = Result.Value.LandingDistrictPlan;
        MegastationArtificialLight[] facadeSources = district.ArtificialLights
            .Where(light => light.Identity.Contains("/floodlight:", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(district.ServiceBuildings.Count * 2, facadeSources.Length);
        foreach (MegastationLandingServiceBuilding building in district.ServiceBuildings)
        {
            Assert.Equal(2, building.Frontage.Floodlights.Count);
            foreach (MegastationFrontageFloodlightPlan floodlight in building.Frontage.Floodlights)
            {
                Assert.Contains(facadeSources, source => source.Identity == floodlight.Identity);
                Assert.InRange(floodlight.Range, 48f, 60f);
                Assert.InRange(floodlight.Intensity, .62f, .76f);
                Assert.InRange(floodlight.AngularCutoffCosine, -.35f, -.25f);
                AssertFinite(floodlight.FixtureCentre);
                AssertFinite(floodlight.SourcePosition);
                AssertFinite(floodlight.Forward);
                Assert.Equal(1f, floodlight.Forward.Length(), 4);
                Assert.True(Vector3.Dot(floodlight.Forward, building.Frontage.Normal) > .80f);
                Assert.True(Vector3.Dot(floodlight.Forward, building.Frontage.Up) < -.45f);
                Vector3 emitterUp = Vector3.Normalize(Vector3.Cross(
                    floodlight.Forward, building.Frontage.Right));
                float nearestEmitterDepth =
                    MegastationLandingDistrictMeshBuilder.FloodlightEmitterOffset
                        * Vector3.Dot(floodlight.Forward, building.Frontage.Normal)
                    - MegastationLandingDistrictMeshBuilder.FloodlightEmitterHeight * .5f
                        * MathF.Abs(Vector3.Dot(emitterUp, building.Frontage.Normal))
                    - MegastationLandingDistrictMeshBuilder.FloodlightEmitterDepth * .5f
                        * MathF.Abs(Vector3.Dot(
                            floodlight.Forward, building.Frontage.Normal));
                Assert.True(nearestEmitterDepth
                    > MegastationLandingDistrictMeshBuilder.FloodlightFixtureHousingDepth * .5f
                        + .02f);
                float fixtureDepth = Vector3.Dot(
                    floodlight.FixtureCentre - building.Centre,
                    building.Frontage.Normal);
                Assert.Equal(building.Size.Z * .5f
                    + MegastationLandingDistrictMeshBuilder.FloodlightFixtureOffset,
                    fixtureDepth, 3);
                Assert.True(Vector3.Dot(
                    floodlight.SourcePosition - floodlight.FixtureCentre,
                    building.Frontage.Normal) > 2f);

                foreach (MegastationLandingServiceBuilding obstacle in district.ServiceBuildings)
                    Assert.False(IsInsideBuilding(floodlight.SourcePosition, obstacle));
            }
        }
    }

    [Fact]
    public void EveryPadFixtureFeedsACompactColourMatchedStaticLight()
    {
        MegastationLandingDistrictPlan district = Result.Value.LandingDistrictPlan;
        MegastationArtificialLight[] fixtureSources = district.ArtificialLights
            .Where(light => light.Identity.Contains("/fixture:", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(district.Pads.Count * 4, fixtureSources.Length);
        foreach (MegastationLandingPadPlan pad in district.Pads)
        foreach (MegastationLandingPadFixturePlan fixture in
                 MegastationLandingPadAssemblyStandards.Fixtures(pad))
        {
            MegastationArtificialLight source = Assert.Single(fixtureSources,
                light => light.Identity == fixture.Identity);
            Assert.Equal(fixture.Colour, source.Colour);
            Assert.Equal(fixture.SourcePosition, source.Position);
            Assert.Equal(fixture.SourceForward, source.Forward);
            Assert.Equal(MegastationLandingPadAssemblyStandards.FixtureLightIntensity,
                source.Intensity);
            Assert.Equal(MegastationLandingPadAssemblyStandards.FixtureLightRange,
                source.Range);
            Assert.Equal(MegastationLandingPadAssemblyStandards.FixtureLightAngularCutoffCosine,
                source.AngularCutoffCosine);

            Vector3 up = pad.PadSurface.Normal;
            Vector3 receiver = fixture.SourcePosition - up * .40f;
            var near = MegastationArtificialLighting.EvaluateComponents(
                receiver, up, [source]);
            Assert.True(near.Direct.LengthSquared() > 0f);
            if (fixture.Colour == MegastationLandingPadAssemblyStandards.UpperFixtureColour)
                Assert.True(near.Direct.Z > near.Direct.X);
            else
                Assert.True(near.Direct.X > near.Direct.Z);

            // The local source aims into the pad instead of relighting and washing out
            // its own already-emissive fixture geometry.
            var fixtureTop = MegastationArtificialLighting.EvaluateComponents(
                fixture.Centre + up * .14f, up, [source]);
            Assert.Equal(Vector3.Zero, fixtureTop.Direct);

            Vector3 farReceiver = fixture.SourcePosition
                + pad.PadSurface.Right * (source.Range
                    * MegastationArtificialLighting.IndirectRangeScale + .1f);
            var far = MegastationArtificialLighting.EvaluateComponents(
                farReceiver, up, [source]);
            Assert.Equal(Vector3.Zero, far.Direct);
            Assert.Equal(Vector3.Zero, far.Indirect);

            MegastationArtificialOcclusion blocker =
                MegastationArtificialOcclusion.CreateForTests(
                    new MegastationArtificialOccluder(
                    MegastationArtificialOccluderRole.MajorStructuralMass,
                    (fixture.SourcePosition + receiver) * .5f,
                    new Vector3(1f, .05f, 1f),
                    pad.PadSurface.Right,
                    up,
                    pad.PadSurface.PreferredHeading));
            var blocked = MegastationArtificialLighting.EvaluateComponents(
                receiver, up, [source], blocker);
            Assert.Equal(Vector3.Zero, blocked.Direct);
            Assert.True(blocked.Indirect.LengthSquared() > 0f);
            AssertFinite(blocked.Direct);
            AssertFinite(blocked.Indirect);
        }
    }

    [Fact]
    public void PadTopReceiverGridCanResolveCompactFixturePools()
    {
        MegastationPrototypeCpuResult result = Result.Value;
        StationModuleMesh mesh = result.InteriorMesh;
        foreach (MegastationLandingPadPlan pad in result.LandingDistrictPlan.Pads)
        {
            Vector3 centre = pad.PadSurface.Centre;
            Vector3 up = pad.PadSurface.Normal;
            Vector3 right = pad.PadSurface.Right;
            Vector3 forward = pad.PadSurface.PreferredHeading;
            int receiverFaces = 0;
            for (int face = 0; face < mesh.FaceCount; face++)
            {
                Vector3[] points = mesh.GetFaceVertexPositions(face);
                if (points.Any(point => MathF.Abs(Vector3.Dot(point - centre, up)) > 1e-4f)
                    || Vector3.Dot(mesh.LocalFaceNormal(face), up) < .999f
                    || points.Any(point =>
                        MathF.Abs(Vector3.Dot(point - centre, right))
                            > pad.NominalSize.X * .5f + 1e-4f
                        || MathF.Abs(Vector3.Dot(point - centre, forward))
                            > pad.NominalSize.Y * .5f + 1e-4f))
                    continue;

                receiverFaces++;
                float rightSpan = points.Max(point => Vector3.Dot(point, right))
                    - points.Min(point => Vector3.Dot(point, right));
                float forwardSpan = points.Max(point => Vector3.Dot(point, forward))
                    - points.Min(point => Vector3.Dot(point, forward));
                Assert.InRange(rightSpan, 0f,
                    MegastationLandingDistrictMeshBuilder.PadTopReceiverSpacing + 1e-3f);
                Assert.InRange(forwardSpan, 0f,
                    MegastationLandingDistrictMeshBuilder.PadTopReceiverSpacing + 1e-3f);
            }
            Assert.True(receiverFaces > 100);
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
        Assert.Equal(.14f, MegastationLandingPadAssemblyStandards.CargoRampDeckThickness);
        Assert.Equal(.10f, MegastationLandingPadAssemblyStandards.CargoRampSupportThickness);
        Assert.Equal(.08f, MegastationLandingPadAssemblyStandards.StairTreadThickness);
        Assert.Equal(.10f, MegastationLandingPadAssemblyStandards.StairStringerThickness);
        Assert.Equal(.06f, MegastationLandingPadAssemblyStandards.RailingMemberThickness);
        Assert.Equal(.25f, MegastationLandingDistrictMeshBuilder.ApronAdditionalDarkening);
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
        foreach (MegastationLandingPadPlan pad in district.Pads)
        {
            MegastationLandingSitePlan site = district.Sites.Single(candidate =>
                candidate.PadIds.Contains(pad.PadId));
            float apronTop = Vector3.Dot(site.ApronCentre, district.FloorNormal)
                + MegastationLandingPadAssemblyStandards.ApronThickness * .5f;
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
    public void CargoRampTopSurfaceMeetsPadAndApronWithoutAnEndLip()
    {
        MegastationLandingDistrictPlan district = Result.Value.LandingDistrictPlan;
        foreach (MegastationLandingPadPlan pad in district.Pads)
        {
            MegastationCargoRampGeometry ramp =
                MegastationLandingPadAssemblyStandards.CargoRamp(pad);
            Vector3 up = pad.PadSurface.Normal;
            Assert.InRange(MathF.Abs(Vector3.Dot(
                ramp.High - pad.PadSurface.Centre, up)), 0f, 1e-5f);
            Assert.Equal(-MegastationLandingPadAssemblyStandards.PadTopHeightAboveApron,
                Vector3.Dot(ramp.Low - pad.PadSurface.Centre, up), 5);
            Assert.True(Vector3.Dot(ramp.SurfaceNormal, up) > 0f);
            Assert.Equal(1f, ramp.Axis.Length(), 5);
            Assert.Equal(1f, ramp.SurfaceNormal.Length(), 5);

            Vector3 deckCentre = (ramp.High + ramp.Low) * .5f
                - ramp.SurfaceNormal * (ramp.Thickness * .5f);
            Vector3 topCentre = deckCentre + ramp.SurfaceNormal * (ramp.Thickness * .5f);
            AssertVector((ramp.High + ramp.Low) * .5f, topCentre);
        }
    }

    [Fact]
    public void PersonnelStairUsesThinOpenTreadsAndTwoSimpleStringers()
    {
        var mesh = new StationModuleMesh();
        MegastationLandingDistrictMeshBuilder.EmitOpenStairAssembly(
            mesh,
            new Vector3(0f, 1f, 0f),
            Vector3.UnitZ,
            Vector3.UnitX,
            Vector3.UnitY,
            Vector3.UnitZ,
            5,
            MegastationLandingPadAssemblyStandards.PersonnelStairWidth,
            Color.Gray,
            Color.DarkGray);

        // Five six-faced tread boxes plus two six-faced stringer prisms. The former
        // five stacked full-height boxes would also have far larger vertical bounds.
        Assert.Equal(42, mesh.FaceCount);
        var (vertices, indices) = mesh.ToIntArrays();
        Assert.Equal(168, vertices.Length);
        Assert.Equal(84, indices.Length / 3);
        Assert.All(vertices, vertex => Assert.True(
            float.IsFinite(vertex.Position.X)
            && float.IsFinite(vertex.Position.Y)
            && float.IsFinite(vertex.Position.Z)));
    }

    [Fact]
    public void LoadingAreaContainsOrganizedStandardContainersAndStaysOutsideBerth()
    {
        MegastationLandingDistrictPlan district = Result.Value.LandingDistrictPlan;
        Assert.Equal(district.Sites.Count, district.LoadingAreas.Count);
        Assert.Equal(.10f, MegastationLandingDistrictPlanner.LoadingAreaOutlineWidth);
        foreach (MegastationLoadingAreaPlan area in district.LoadingAreas)
        {
        Assert.Contains(district.Sites, site => site.Identity == area.SiteIdentity);
        Assert.Contains(district.Pads, pad => pad.PadId == area.PadId);
        Assert.StartsWith("LOADING AREA ", area.Label, StringComparison.Ordinal);
        Assert.InRange(area.Containers.Count, 2, 8);
        foreach (MegastationLandingContainerPlan container in area.Containers)
        {
            Assert.Equal(MegastationLandingDistrictPlanner.StandardContainerSize, container.Size);
            Assert.True(area.Bounds.Contains(container.Footprint));
        }

        float occupiedFraction = area.Containers.Select(container => container.Footprint)
            .Distinct().Sum(footprint =>
                (footprint.RightMaximum - footprint.RightMinimum)
                * (footprint.ForwardMaximum - footprint.ForwardMinimum))
            / (area.Size.X * area.Size.Y);
        Assert.InRange(occupiedFraction, .10f, .75f);
        MegastationLandingPadPlan pad = district.Pads.Single(candidate => candidate.PadId == area.PadId);
        Assert.False(area.Bounds.Intersects(pad.FutureBerthClearance));
        }
    }

    [Fact]
    public void KeepClearGrammarIsSparsePurposefulAndSeparateFromStoredCargo()
    {
        MegastationLandingDistrictPlan district = Result.Value.LandingDistrictPlan;
        Assert.Equal(district.Sites.Count * 4, district.KeepClearZones.Count);
        foreach (MegastationLandingSitePlan site in district.Sites)
        {
            MegastationLoadingAreaPlan area = district.LoadingAreas.Single(candidate =>
                candidate.SiteIdentity == site.Identity);
            MegastationKeepClearZonePlan[] zones = district.KeepClearZones.Where(zone =>
                zone.SiteIdentity == site.Identity).ToArray();
            Assert.Equal(Enum.GetValues<MegastationKeepClearPurpose>().Order(),
                zones.Select(zone => zone.Purpose).Order());
            Assert.Equal(2, zones.Count(zone => zone.ShowLabel));
            foreach (MegastationKeepClearZonePlan zone in zones)
            {
                Assert.False(area.Bounds.Intersects(zone.Bounds));
                Assert.All(area.Containers, container =>
                    Assert.False(zone.Bounds.Intersects(container.Footprint)));
            }
        }
    }

    [Fact]
    public void OperationalFloorPlanningIsDeterministicWithoutChangingBaseLighting()
    {
        MegastationPrototypeCpuResult result = Result.Value;
        MegastationLandingDistrictPlan replanned =
            MegastationLandingDistrictPlanner.Plan(
                result.InteriorPlan, result.RegularisedOccupancy,
                result.MegaShelfPlan, forceLandingSiteOnShelf: true);

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
        Assert.Equal(12 + result.LandingDistrictPlan.ArtificialLights.Count
                + result.BayFacilityPlan.ArtificialLights.Count,
            result.ArtificialLightingPlan.Lights.Count);
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
        MegastationLandingPadPlan pad = Result.Value.LandingDistrictPlan.Pads[0];

        Vector3 feet = MegastationLandingDistrictMeshBuilder.ScaleHumanFeetPosition(pad);

        Assert.InRange(MathF.Abs(Vector3.Dot(
            feet - pad.PadSurface.Centre,
            pad.PadSurface.Normal)), 0f, 1e-5f);
    }

    [Fact]
    public void ScaleHumanFeetContactFloorPadAndStairSupportSurfaces()
    {
        MegastationLandingPadPlan pad = Result.Value.LandingDistrictPlan.Pads[0];
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

    private static void AssertFinite(Vector3 value)
    {
        Assert.True(IsFinite(value));
    }

    private static bool IsInsideBuilding(
        Vector3 point,
        MegastationLandingServiceBuilding building)
    {
        Vector3 offset = point - building.Centre;
        return MathF.Abs(Vector3.Dot(offset, building.Frontage.Right)) < building.Size.X * .5f
            && MathF.Abs(Vector3.Dot(offset, building.Frontage.Up)) < building.Size.Y * .5f
            && MathF.Abs(Vector3.Dot(offset, building.Frontage.Normal)) < building.Size.Z * .5f;
    }

    private static void AssertVector(Vector3 expected, Vector3 actual)
    {
        Assert.Equal(expected.X, actual.X, 3);
        Assert.Equal(expected.Y, actual.Y, 3);
        Assert.Equal(expected.Z, actual.Z, 3);
    }

    private static (int X, int Y, int Z) CellContaining(SliceGrid grid, Vector3 point)
        => (CellContaining(grid, GridAxis.X, point.X),
            CellContaining(grid, GridAxis.Y, point.Y),
            CellContaining(grid, GridAxis.Z, point.Z));

    private static int CellContaining(SliceGrid grid, GridAxis axis, float coordinate)
    {
        for (int i = 0; i < grid.Count(axis); i++)
            if (coordinate >= grid.GetCellMinimum(axis, i)
                && coordinate <= grid.GetCellMaximum(axis, i))
                return i;
        throw new InvalidOperationException($"No {axis} cell contains {coordinate}.");
    }

    private static float FloorHeight(MegastationInteriorPlan interior)
    {
        Vector3 up = Vector3.Normalize(interior.PortalUp);
        float minimum = float.MaxValue;
        foreach (float x in new[] { interior.CavityEnvelope.Minimum.X, interior.CavityEnvelope.Maximum.X })
        foreach (float y in new[] { interior.CavityEnvelope.Minimum.Y, interior.CavityEnvelope.Maximum.Y })
        foreach (float z in new[] { interior.CavityEnvelope.Minimum.Z, interior.CavityEnvelope.Maximum.Z })
            minimum = MathF.Min(minimum, Vector3.Dot(new Vector3(x, y, z), up));
        return minimum;
    }
}
