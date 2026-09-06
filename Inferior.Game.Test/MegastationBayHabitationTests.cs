using Inferior.Game.StationGen;
using Inferior.Game.StationGen.Megastations;
using Microsoft.Xna.Framework;
using Xunit;

namespace Inferior.Game.Test;

[Trait("Category", "Slow")]
public sealed class MegastationBayHabitationTests
{
    private const string Nova = "Oranae:Oranae I:Nova Anchorage";
    private static readonly Lazy<MegastationPrototypeCpuResult> Result =
        new(() => MegastationPrototypeGenerator.GenerateCpu(Nova));

    [Fact]
    public void KnownStationUsesSparseVerticalWallRegionsAndSharedHumanScaleWindows()
    {
        MegastationPrototypeCpuResult result = Result.Value;
        MegastationBayHabitationPlan plan = result.BayHabitationPlan;

        Assert.Equal(4, plan.Walls.Count);
        Assert.InRange(plan.Diagnostics.ActiveWallCount, 1, 3);
        Assert.True(plan.Diagnostics.BlankWallCount >= 1);
        Assert.Equal(plan.Windows.Count, plan.Diagnostics.WindowCount);
        HashSet<string> enhancedRegions = result.BayFacilityPlan.Facilities
            .Select(facility => facility.RegionIdentity).ToHashSet();
        int emittedBaseWindows = plan.Windows.Count(window =>
            !enhancedRegions.Contains(window.RegionIdentity));
        Assert.Equal(emittedBaseWindows * 4, plan.Diagnostics.MeshVertexCount);
        Assert.Equal(emittedBaseWindows * 2, plan.Diagnostics.MeshTriangleCount);
        Assert.DoesNotContain(plan.Windows, window =>
            Wall(plan, window.WallIdentity).Kind == MegastationBayWallKind.Entrance);
        foreach (MegastationBayHabitationWindow window in plan.Windows)
        {
            MegastationBayWallSurface wall = Wall(plan, window.WallIdentity);
            MegastationBayHabitationRegion region = Assert.Single(plan.Regions,
                candidate => candidate.Identity == window.RegionIdentity);
            Assert.True(Vector3.Dot(window.Normal, wall.Normal) > .9999f);
            Assert.True(Vector3.Dot(window.Up, result.InteriorPlan.PortalUp) > .9999f);
            Assert.Equal(.045f,
                Vector3.Dot(window.Centre - wall.Centre, wall.Normal), 3);
            Assert.InRange(window.Width, 1.18f, 1.6001f);
            Assert.InRange(window.Height, .96f, 1.3001f);
            float x = Vector3.Dot(window.Centre - wall.Centre, wall.Right);
            float y = Vector3.Dot(window.Centre - wall.Centre, wall.Up);
            Assert.True(x - window.Width * .5f >= region.Centre.X - region.Size.X * .5f);
            Assert.True(x + window.Width * .5f <= region.Centre.X + region.Size.X * .5f);
            Assert.True(y - window.Height * .5f >= region.Centre.Y - region.Size.Y * .5f);
            Assert.True(y + window.Height * .5f <= region.Centre.Y + region.Size.Y * .5f);
        }
        var windowMesh = new StationModuleMesh();
        MegastationBayHabitationMeshResult emitted = MegastationBayHabitationMeshBuilder.Append(
            windowMesh, plan, result.BayFacilityPlan);
        Assert.Equal(emittedBaseWindows, windowMesh.FaceCount);
        Assert.All(Enumerable.Range(0, windowMesh.FaceCount), face =>
        {
            Assert.True(IsFinite(windowMesh.LocalFaceNormal(face)));
            Assert.All(windowMesh.GetFaceVertexPositions(face), vertex =>
                Assert.True(IsFinite(vertex)));
        });

        Console.WriteLine(
            $"L3a {Nova}: {plan.Diagnostics.WallSummary}; " +
            $"regions={plan.Diagnostics.RegionCount}; groups={plan.Diagnostics.WindowGroupCount}; " +
            $"windows={plan.Diagnostics.WindowCount} " +
            $"({plan.Diagnostics.LitWindowCount}lit/{plan.Diagnostics.DimWindowCount}dim/" +
            $"{plan.Diagnostics.DarkWindowCount}dark); " +
            $"mesh={plan.Diagnostics.MeshVertexCount}v/{plan.Diagnostics.MeshTriangleCount}t");
    }

    [Fact]
    public void RegionsStayInsideAuthoritativeWallFramesAndAboveFloorActivity()
    {
        MegastationBayHabitationPlan plan = Result.Value.BayHabitationPlan;
        foreach (MegastationBayHabitationRegion region in plan.Regions)
        {
            MegastationBayWallSurface wall = Wall(plan, region.WallIdentity);
            Assert.True(region.Centre.X - region.Size.X * .5f >= -wall.Width * .5f + 13.99f);
            Assert.True(region.Centre.X + region.Size.X * .5f <= wall.Width * .5f - 13.99f);
            Assert.True(region.Centre.Y - region.Size.Y * .5f >= -wall.Height * .5f + 31.99f);
            Assert.True(region.Centre.Y + region.Size.Y * .5f <= wall.Height * .5f - 17.99f);
            Assert.DoesNotContain(Result.Value.LandingDistrictPlan.ServiceBuildings,
                building => IntersectsWallRegion(wall, region, building));
        }
    }

    [Fact]
    public void PlanIsDeterministicAndDoesNotAddArtificialLightSources()
    {
        MegastationPrototypeCpuResult result = Result.Value;
        MegastationBayHabitationPlan replanned = MegastationBayHabitationPlanner.Plan(
            result.InteriorPlan, result.LandingDistrictPlan);

        Assert.Equal(result.BayHabitationPlan.Diagnostics.Signature,
            replanned.Diagnostics.Signature);
        Assert.Equal(result.BayHabitationPlan.Regions, replanned.Regions);
        Assert.Equal(result.BayHabitationPlan.Windows, replanned.Windows);
        Assert.Equal(12 + result.LandingDistrictPlan.ArtificialLights.Count,
            result.ArtificialLightingPlan.Lights.Count);
    }

    [Fact]
    public void SeedSampleIncludesBlankSingleAndMultipleRegionWallsWithPatternVariation()
    {
        MegastationPrototypeCpuResult result = Result.Value;
        var regionCounts = new HashSet<int>();
        var patterns = new HashSet<MegastationBayWindowPattern>();
        var verticalPositions = new HashSet<int>();
        for (int offset = 0; offset < 80; offset++)
        {
            int seed = unchecked(result.InteriorPlan.Seed + offset * 0x45d9f3b);
            MegastationInteriorPlan interior = result.InteriorPlan with { Seed = seed };
            MegastationBayHabitationPlan plan = MegastationBayHabitationPlanner.Plan(
                interior, result.LandingDistrictPlan);
            foreach (MegastationBayWallSurface wall in plan.Walls)
                regionCounts.Add(plan.Regions.Count(region =>
                    region.WallIdentity == wall.Identity));
            foreach (MegastationBayHabitationWindow window in plan.Windows)
            {
                patterns.Add(window.Pattern);
                MegastationBayWallSurface wall = Wall(plan, window.WallIdentity);
                verticalPositions.Add((int)MathF.Round(
                    Vector3.Dot(window.Centre - wall.Centre, wall.Up) / 5f));
            }
        }

        Assert.Contains(0, regionCounts);
        Assert.Contains(1, regionCounts);
        Assert.Contains(regionCounts, count => count >= 2);
        Assert.Equal(Enum.GetValues<MegastationBayWindowPattern>().Order(), patterns.Order());
        Assert.True(verticalPositions.Count >= 6);
    }

    [Fact]
    public void WallFacilitiesAreSparseRegionOwnedBatchedAndArtificiallyShadowed()
    {
        MegastationPrototypeCpuResult result = Result.Value;
        MegastationBayHabitationPlan habitation = result.BayHabitationPlan;
        MegastationBayFacilityPlan plan = result.BayFacilityPlan;
        int partCount = plan.Facilities.Sum(facility => facility.Parts.Count);
        int facilityWindows = plan.Facilities.Sum(facility => facility.Windows?.Count ?? 0);
        int casterParts = plan.Facilities.Sum(facility => facility.Parts.Count(part =>
            part.CastsArtificialShadow));

        Assert.NotEmpty(plan.Facilities);
        Assert.True(plan.Facilities.Count < habitation.Regions.Count);
        Assert.Equal(habitation.Regions.Count - plan.Facilities.Count,
            plan.Diagnostics.PlainRegionCount);
        Assert.Equal(partCount * 24 + facilityWindows * 4,
            plan.Diagnostics.MeshVertexCount);
        Assert.Equal(partCount * 12 + facilityWindows * 2,
            plan.Diagnostics.MeshTriangleCount);
        Assert.Equal(casterParts * 24, plan.Diagnostics.ShadowVertexCount);
        Assert.Equal(casterParts * 12, plan.Diagnostics.ShadowTriangleCount);
        Assert.Equal(casterParts, plan.Diagnostics.ArtificialShadowPartCount);
        Assert.Equal(facilityWindows, plan.Diagnostics.FacilityWindowCount);
        Assert.InRange(plan.Diagnostics.CutoutCount, 1, plan.Facilities.Count);
        Assert.InRange(plan.Diagnostics.MaximumProjection, 0f, 6f);
        Assert.True(result.InteriorPlan.Diagnostics.ArtificialOccluderCount > casterParts);
        Assert.Equal(12 + result.LandingDistrictPlan.ArtificialLights.Count,
            result.ArtificialLightingPlan.Lights.Count);

        foreach (MegastationBayFacility facility in plan.Facilities)
        {
            Assert.Contains(habitation.Regions, region =>
                region.Identity == facility.RegionIdentity);
            Assert.True(Vector3.Dot(Vector3.Cross(facility.Right, facility.Up),
                facility.Normal) > .9999f);
            Assert.InRange(facility.MaximumProjection, 0f, 6f);
            MegastationBayWallSurface facilityWall = Wall(habitation, facility.WallIdentity);
            Vector3 envelopeOffset = facility.EnvelopeCentre - facilityWall.Centre;
            float envelopeX = Vector3.Dot(envelopeOffset, facilityWall.Right);
            float envelopeY = Vector3.Dot(envelopeOffset, facilityWall.Up);
            Assert.True(MathF.Abs(envelopeX) + facility.EnvelopeSize.X * .5f
                <= facilityWall.Width * .5f);
            Assert.True(MathF.Abs(envelopeY) + facility.EnvelopeSize.Y * .5f
                <= facilityWall.Height * .5f);
            Assert.All(facility.Parts, part =>
            {
                Assert.True(IsFinite(part.Centre));
                Assert.True(IsFinite(part.Size));
                Assert.True(part.Size.X > 0f && part.Size.Y > 0f && part.Size.Z > 0f);
                MegastationBayWallSurface wall = Wall(habitation, facility.WallIdentity);
                float lowerEdge = Vector3.Dot(part.Centre - wall.Centre, wall.Up)
                    - part.Size.Y * .5f;
                Assert.True(lowerEdge >= -wall.Height * .5f + 23.99f);
                float rearEdge = Vector3.Dot(part.Centre - wall.Centre, wall.Normal)
                    - part.Size.Z * .5f;
                // Rear railing posts are centred on the façade connection, so half
                // their thin member depth is intentionally embedded in the wall.
                Assert.True(rearEdge >= -(facility.Cutout?.Depth ?? 0f) - .051f);
            });
            if (facility.Cutout is { } cutout)
            {
                Assert.Equal(MegastationBayFacilityArchetype.RecessedFacility,
                    facility.Archetype);
                Assert.InRange(cutout.Depth, 1f, 5f);
                Assert.InRange(cutout.Size.X, 10f, 100f);
                Assert.InRange(cutout.Size.Y, 8f, 100f);
                Assert.NotEmpty(facility.Windows ?? []);
            }
        }
        for (int first = 0; first < plan.Facilities.Count; first++)
        for (int second = first + 1; second < plan.Facilities.Count; second++)
        {
            MegastationBayFacility a = plan.Facilities[first];
            MegastationBayFacility b = plan.Facilities[second];
            if (a.WallIdentity == b.WallIdentity)
                Assert.False(EnvelopeOverlaps(a, b));
        }

        Console.WriteLine(
            $"L3b {Nova}: facilities={plan.Diagnostics.EnhancedRegionCount}/" +
            $"{plan.Diagnostics.EligibleRegionCount}, plain={plan.Diagnostics.PlainRegionCount}; " +
            $"recessed={plan.Diagnostics.RecessedFacilityCount}, " +
            $"galleries={plan.Diagnostics.ProjectingGalleryCount}, " +
            $"embedded={plan.Diagnostics.EmbeddedBlockCount}, " +
            $"apertures={plan.Diagnostics.ServiceApertureCount}, " +
            $"secondary={plan.Diagnostics.SecondaryFormCount}; " +
            $"parts={partCount}, rails={plan.Diagnostics.RailingPartCount}, " +
            $"occluders={casterParts}; cutouts={plan.Diagnostics.CutoutCount}, " +
            $"facilityWindows={plan.Diagnostics.FacilityWindowCount}, " +
            $"balconies={plan.Diagnostics.BalconyCount}; " +
            $"projection={plan.Diagnostics.MaximumProjection:F1}m; " +
            $"mesh={plan.Diagnostics.MeshVertexCount}v/{plan.Diagnostics.MeshTriangleCount}t");
    }

    [Fact]
    public void WallFacilityPlanIsDeterministicAndDoesNotMoveL3aWindows()
    {
        MegastationPrototypeCpuResult result = Result.Value;
        MegastationBayHabitationWindow[] windows = result.BayHabitationPlan.Windows.ToArray();
        MegastationBayFacilityPlan replanned = MegastationBayFacilityPlanner.Plan(
            result.InteriorPlan, result.BayHabitationPlan,
            result.RegularisedOccupancy, result.BoundaryTopology);

        Assert.Equal(result.BayFacilityPlan.Diagnostics.Signature,
            replanned.Diagnostics.Signature);
        Assert.Equal(result.BayFacilityPlan.Facilities.Select(facility =>
                (facility.Identity, facility.Archetype, facility.EnvelopeCentre,
                    facility.EnvelopeSize, facility.MaximumProjection,
                    facility.HasSecondaryForm)),
            replanned.Facilities.Select(facility =>
                (facility.Identity, facility.Archetype, facility.EnvelopeCentre,
                    facility.EnvelopeSize, facility.MaximumProjection,
                    facility.HasSecondaryForm)));
        Assert.Equal(result.BayFacilityPlan.Facilities.SelectMany(facility => facility.Parts),
            replanned.Facilities.SelectMany(facility => facility.Parts));
        Assert.Equal(result.BayFacilityPlan.Facilities.Select(facility => facility.Cutout),
            replanned.Facilities.Select(facility => facility.Cutout));
        Assert.Equal(result.BayFacilityPlan.Facilities.SelectMany(facility =>
                facility.Windows ?? []),
            replanned.Facilities.SelectMany(facility => facility.Windows ?? []));
        Assert.Equal(windows, result.BayHabitationPlan.Windows);
    }

    [Fact]
    public void FacilitySeedSampleExercisesEveryArchetypeSecondaryFormsAndPlainRegions()
    {
        MegastationPrototypeCpuResult result = Result.Value;
        var archetypes = new HashSet<MegastationBayFacilityArchetype>();
        bool sawSecondary = false;
        bool sawPlain = false;
        for (int offset = 0; offset < 96; offset++)
        {
            int seed = unchecked(result.InteriorPlan.Seed + offset * 0x45d9f3b);
            MegastationInteriorPlan interior = result.InteriorPlan with { Seed = seed };
            MegastationLandingDistrictPlan district =
                MegastationLandingDistrictPlanner.Plan(interior);
            MegastationBayHabitationPlan habitation =
                MegastationBayHabitationPlanner.Plan(interior, district);
            MegastationBayFacilityPlan plan =
                MegastationBayFacilityPlanner.Plan(interior, habitation);
            foreach (MegastationBayFacility facility in plan.Facilities)
                archetypes.Add(facility.Archetype);
            sawSecondary |= plan.Diagnostics.SecondaryFormCount > 0;
            sawPlain |= plan.Diagnostics.PlainRegionCount > 0;
        }

        Assert.Equal(Enum.GetValues<MegastationBayFacilityArchetype>().Order(),
            archetypes.Order());
        Assert.True(sawSecondary);
        Assert.True(sawPlain);
    }

    [Fact]
    public void FacilityGeometryUsesFiniteCorrectlyWoundCuboids()
    {
        MegastationPrototypeCpuResult result = Result.Value;
        MegastationBayFacilityPlan plan = result.BayFacilityPlan;
        var mesh = new StationModuleMesh();
        MegastationBayFacilityMeshBuilder.Append(mesh, plan, null);
        for (int face = 0; face < mesh.FaceCount; face++)
        {
            Vector3 normal = mesh.LocalFaceNormal(face);
            Vector3[] vertices = mesh.GetFaceVertexPositions(face);
            Assert.True(IsFinite(normal));
            Assert.True(normal.LengthSquared() > .999f);
            Assert.All(vertices, vertex => Assert.True(IsFinite(vertex)));
            Assert.True(Vector3.Cross(vertices[1] - vertices[0],
                vertices[2] - vertices[0]).LengthSquared() > 1e-8f);
        }
    }

    [Fact]
    public void ProductionRecessesReplaceOneBoundaryFaceWithAClosedNineFaceShell()
    {
        MegastationPrototypeCpuResult result = Result.Value;
        MegastationBayWallCutout[] cutouts = result.BayFacilityPlan.Facilities
            .Where(facility => facility.Cutout is not null)
            .Select(facility => facility.Cutout!)
            .ToArray();

        Assert.NotEmpty(cutouts);
        Assert.Equal(cutouts.Length,
            cutouts.Select(cutout => cutout.SupportingFace).Distinct().Count());
        Assert.Equal(result.BoundaryTopology.Stats.BoundaryFaceCount + cutouts.Length * 8,
            result.MeshStats.ExposedQuadCount);
        Assert.True(result.MeshStats.ChamferedValidation.IsValid,
            result.MeshStats.ChamferedValidation.Summary);

        var (vertices, _) = result.Mesh.ToIntArrays();
        foreach (MegastationBayWallCutout cutout in cutouts)
        {
            Vector3 rearCentre = cutout.Centre - cutout.Normal * cutout.Depth;
            int rearVertices = vertices.Count(vertex =>
                MathF.Abs(Vector3.Dot(vertex.Position - rearCentre, cutout.Normal)) < .001f
                && MathF.Abs(Vector3.Dot(vertex.Position - rearCentre, cutout.Right))
                    <= cutout.Size.X * .5f + .001f
                && MathF.Abs(Vector3.Dot(vertex.Position - rearCentre, cutout.Up))
                    <= cutout.Size.Y * .5f + .001f);
            Assert.True(rearVertices >= 4);
        }
    }

    [Fact]
    public void RecessFacadesHaveHumanScaleRowsAndBalconyAccessArchitecture()
    {
        MegastationBayFacility[] recessed = Result.Value.BayFacilityPlan.Facilities
            .Where(facility => facility.Cutout is not null).ToArray();
        Assert.NotEmpty(recessed);
        foreach (MegastationBayFacility facility in recessed)
        {
            MegastationBayWallCutout cutout = facility.Cutout!;
            MegastationBayHabitationWindow[] windows = (facility.Windows ?? [])
                .Where(window => window.Identity.Contains("/recess/", StringComparison.Ordinal))
                .ToArray();
            Assert.NotEmpty(windows);
            Assert.All(windows, window =>
            {
                Assert.InRange(window.Width, 1.2f, 1.5f);
                Assert.InRange(window.Height, 1f, 1.25f);
                Assert.Equal(-cutout.Depth + .045f,
                    Vector3.Dot(window.Centre - cutout.Centre, cutout.Normal), 3);
            });

            MegastationBayFacilityPart[] balconies = facility.Parts.Where(part =>
                part.Role == MegastationBayFacilityPartRole.GallerySlab).ToArray();
            if (cutout.Size.Y >= 14f)
            {
                Assert.InRange(balconies.Length, 1, 2);
                Assert.Equal(balconies.Length, facility.Parts.Count(part =>
                    part.Role == MegastationBayFacilityPartRole.PersonnelDoor));
                Assert.True(facility.Parts.Count(part =>
                    part.Role == MegastationBayFacilityPartRole.Railing) >=
                    balconies.Length * 5);
                foreach (int level in Enumerable.Range(0, balconies.Length))
                    Assert.Contains(windows, window => window.Identity.Contains(
                        $"/balcony:{level}/window:", StringComparison.Ordinal));
            }
        }
    }

    [Fact]
    public void ValidBaySeedSampleAlwaysRetainsHabitationAndARealCutout()
    {
        MegastationPrototypeCpuResult baseline = Result.Value;
        for (int offset = 0; offset < 12; offset++)
        {
            int seed = unchecked(baseline.InteriorPlan.Seed + offset * 0x45d9f3b);
            MegastationInteriorPlan interior = baseline.InteriorPlan with { Seed = seed };
            MegastationLandingDistrictPlan district =
                MegastationLandingDistrictPlanner.Plan(interior);
            MegastationBayHabitationPlan habitation =
                MegastationBayHabitationPlanner.Plan(interior, district);
            MegastationBayFacilityPlan facilities = MegastationBayFacilityPlanner.Plan(
                interior, habitation, baseline.RegularisedOccupancy,
                baseline.BoundaryTopology);

            Assert.NotEmpty(habitation.Regions);
            Assert.NotEmpty(habitation.Windows);
            Assert.Contains(facilities.Facilities, facility => facility.Cutout is not null);
        }
    }

    private static MegastationBayWallSurface Wall(
        MegastationBayHabitationPlan plan,
        string identity)
        => Assert.Single(plan.Walls, wall => wall.Identity == identity);

    private static bool IsFinite(Vector3 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool IntersectsWallRegion(
        MegastationBayWallSurface wall,
        MegastationBayHabitationRegion region,
        MegastationLandingServiceBuilding building)
    {
        Vector3 buildingRight = Vector3.Normalize(building.Frontage.Right);
        Vector3 buildingUp = Vector3.Normalize(building.Frontage.Up);
        Vector3 buildingForward = Vector3.Normalize(building.Frontage.Normal);
        float Radius(Vector3 axis) =>
            MathF.Abs(Vector3.Dot(buildingRight, axis)) * building.Size.X * .5f
            + MathF.Abs(Vector3.Dot(buildingUp, axis)) * building.Size.Y * .5f
            + MathF.Abs(Vector3.Dot(buildingForward, axis)) * building.Size.Z * .5f;
        if (MathF.Abs(Vector3.Dot(building.Centre - wall.Centre, wall.Normal))
            > Radius(wall.Normal) + 2f)
            return false;
        float x = Vector3.Dot(building.Centre - wall.Centre, wall.Right);
        float y = Vector3.Dot(building.Centre - wall.Centre, wall.Up);
        return MathF.Abs(region.Centre.X - x) * 2f
                < region.Size.X + Radius(wall.Right) * 2f + 8f
            && MathF.Abs(region.Centre.Y - y) * 2f
                < region.Size.Y + Radius(wall.Up) * 2f + 8f;
    }

    private static bool EnvelopeOverlaps(
        MegastationBayFacility a,
        MegastationBayFacility b)
    {
        Vector3 delta = b.EnvelopeCentre - a.EnvelopeCentre;
        float x = MathF.Abs(Vector3.Dot(delta, a.Right));
        float y = MathF.Abs(Vector3.Dot(delta, a.Up));
        float z = MathF.Abs(Vector3.Dot(delta, a.Normal));
        return x * 2f < a.EnvelopeSize.X + b.EnvelopeSize.X
            && y * 2f < a.EnvelopeSize.Y + b.EnvelopeSize.Y
            && z * 2f < a.EnvelopeSize.Z + b.EnvelopeSize.Z;
    }
}
