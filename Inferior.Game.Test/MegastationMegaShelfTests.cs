using Inferior.Game.StationGen;
using Inferior.Game.StationGen.Megastations;
using Microsoft.Xna.Framework;
using Xunit;
using Xunit.Abstractions;

namespace Inferior.Game.Test;

[Trait("Category", "Slow")]
public sealed class MegastationMegaShelfTests(ITestOutputHelper output)
{
    private const string Nova = "Oranae:Oranae I:Nova Anchorage";
    private static readonly Lazy<MegastationPrototypeCpuResult> Result =
        new(() => MegastationPrototypeGenerator.GenerateCpu(Nova,
            MegastationPrototypeSettings.Default with
            {
                MegaShelfDevelopment = new(ForcedShelfCount: 1,
                    ForceLandingSiteOnShelf: true),
            }));

    [Theory]
    [InlineData(MegastationShelfMacroLayout.Back)]
    [InlineData(MegastationShelfMacroLayout.Left)]
    [InlineData(MegastationShelfMacroLayout.Right)]
    [InlineData(MegastationShelfMacroLayout.SymmetricSides)]
    [InlineData(MegastationShelfMacroLayout.U)]
    public void ForcedBookcaseLayoutsAreCoherentProductionCompositions(
        MegastationShelfMacroLayout layout)
    {
        MegastationPrototypeCpuResult result = MegastationPrototypeGenerator.GenerateCpu(
            Nova, MegastationPrototypeSettings.Default with
            {
                MegaShelfDevelopment = new(
                    ForcedShelfCount: null,
                    ForceLandingSiteOnShelf: false,
                    ForcedMacroLayout: layout),
            });
        MegastationMegaShelfPlan plan = result.MegaShelfPlan;
        Assert.True(plan.Bookcases.Count == 1,
            $"{plan.Diagnostics.MacroSummary}; rejects="
            + $"height:{plan.Diagnostics.BookcaseInsufficientHeightRejectCount},"
            + $"arrival:{plan.Diagnostics.BookcaseArrivalRejectCount},"
            + $"wall:{plan.Diagnostics.BookcaseWallContinuityRejectCount},"
            + $"structure:{plan.Diagnostics.BookcaseStructuralRejectCount},"
            + $"symmetry:{plan.Diagnostics.BookcaseSymmetryRejectCount}; "
            + $"cavity={result.InteriorPlan.CavityEnvelope.Size}");
        MegastationBookcaseArrangement arrangement = plan.Bookcases[0];
        Assert.Equal(layout, arrangement.Layout);
        Assert.InRange(arrangement.LevelCount, 3, 5);
        Assert.Equal(arrangement.LevelCount * arrangement.HostWallIdentities.Count,
            arrangement.ShelfIdentities.Count);
        MegastationMegaShelf[] levels = plan.Shelves
            .Where(shelf => shelf.Family == MegastationMegaShelfFamily.Bookcase)
            .ToArray();
        Assert.Equal(arrangement.ShelfIdentities.Count, levels.Length);
        Assert.DoesNotContain(arrangement.HostWallIdentities,
            identity => identity.Contains("entrance", StringComparison.OrdinalIgnoreCase));
        Assert.All(levels, level => Assert.NotNull(level.LandingSurface));
        Assert.All(levels, level => Assert.Equal(3, level.Trusses.Count));
        Assert.Single(levels.SelectMany(level => level.Trusses)
            .Select(truss => truss.Spec.CrossSection).Distinct());
        foreach (IGrouping<string, MegastationMegaShelf> stack in levels.GroupBy(
                     level => level.HostWallIdentity, StringComparer.Ordinal))
        {
            Assert.Single(stack.Select(level => level.Body.Size).Distinct());
            float[] elevations = stack.Select(level => Vector3.Dot(
                    level.Body.Centre + level.Body.Up * level.Body.Size.Y * .5f,
                    result.InteriorPlan.PortalUp))
                .Order().ToArray();
            Assert.Equal(arrangement.LevelCount, elevations.Length);
            if (elevations.Length > 2)
                Assert.All(elevations.Zip(elevations.Skip(1), (a, b) => b - a),
                    spacing => Assert.Equal(elevations[1] - elevations[0], spacing, 2));
        }
        if (layout is MegastationShelfMacroLayout.SymmetricSides
            or MegastationShelfMacroLayout.U)
        {
            MegastationMegaShelf[] sides = levels.Where(level =>
                    level.HostWallIdentity.EndsWith("left", StringComparison.Ordinal)
                    || level.HostWallIdentity.EndsWith("right", StringComparison.Ordinal))
                .ToArray();
            Assert.Single(sides.Select(level => level.Body.Size.X).Distinct());
        }
        for (int first = 0; first < levels.Length; first++)
        for (int second = first + 1; second < levels.Length; second++)
            Assert.False(MegastationMegaShelfPlanner.StructuralVolumesIntersect(
                levels[first].Body, levels[second].Body, 0f),
                $"{levels[first].Identity} {levels[first].Body.Centre}/{levels[first].Body.Size}/"
                + $"{levels[first].Body.Right}/{levels[first].Body.Forward} overlaps "
                + $"{levels[second].Identity} {levels[second].Body.Centre}/"
                + $"{levels[second].Body.Size}/{levels[second].Body.Right}/"
                + $"{levels[second].Body.Forward}");
        output.WriteLine(arrangement.Summary);
    }

    [Theory]
    [InlineData("Oranae:Oranae I:Nova Anchorage")]
    [InlineData("Gaanis:Gaanis II:Omega Beacon")]
    [InlineData("Enloax:Enloax Vd:Deep Haven")]
    public void NaturalMultiShelfDistributionSampleIsValid(string identity)
    {
        MegastationPrototypeCpuResult result = MegastationPrototypeGenerator.GenerateCpu(
            identity, MegastationPrototypeSettings.Default with
            {
                MegaShelfDevelopment = MegastationMegaShelfDevelopmentOptions.Runtime,
            });
        MegastationMegaShelfPlan plan = result.MegaShelfPlan;
        MegastationInteriorPlan baseline = result.InteriorPlan with
        {
            AddedStructuralSolids = null,
            AdditionalLandingSurfaces = null,
            AddedStructuralMarkers = null,
            AddedStructuralTrusses = null,
        };
        MegastationLandingSiteStructuralClearance structuralClearance =
            MegastationLandingSiteStructuralClearance.Create(
                result.RegularisedOccupancy, baseline);
        Dictionary<string, MegastationBayWallSurface> hostWalls =
            MegastationBayHabitationPlanner.CreateWalls(baseline)
                .ToDictionary(wall => wall.Identity, StringComparer.Ordinal);
        Assert.Equal(plan.Shelves.Count, plan.Shelves.Select(shelf => shelf.Identity)
            .Distinct(StringComparer.Ordinal).Count());
        Assert.All(plan.Shelves, shelf =>
            Assert.False(result.InteriorPlan.ArrivalManeuverExclusion.IntersectsOrientedBox(
                shelf.Body.Centre, shelf.Body.Right, shelf.Body.Up, shelf.Body.Forward,
                shelf.Body.Size)));
        Assert.All(plan.Shelves, shelf =>
        {
            int expectedHostCount = shelf.Family is MegastationMegaShelfFamily.Cantilever
                or MegastationMegaShelfFamily.Bookcase ? 1 : 2;
            Assert.Equal(expectedHostCount, shelf.HostWallIdentities.Count);
            Assert.Equal(shelf.HostWallIdentities.Distinct(StringComparer.Ordinal).Count(),
                shelf.HostWallIdentities.Count);
            Assert.NotEmpty(shelf.SupportingFaces);
            Assert.True(structuralClearance.IsOrientedBoxClear(
                shelf.Body.Centre, shelf.Body.Right, shelf.Body.Up, shelf.Body.Forward,
                shelf.Body.Size, out string bodyBlocker), bodyBlocker);
            foreach (string hostIdentity in shelf.HostWallIdentities)
            {
                MegastationBayWallSurface host = hostWalls[hostIdentity];
                Assert.True(MegastationMegaShelfPlanner.TryFindRootSupport(
                    shelf.Body, host, result.RegularisedOccupancy.Grid,
                    result.BoundaryTopology, out BoundaryFaceKey[] exactRootFaces));
                Assert.NotEmpty(exactRootFaces);
                Assert.Contains(shelf.SupportingFaces, key =>
                    result.BoundaryTopology.Faces.Any(face => face.Key == key
                        && Vector3.Dot(BoundaryTopologyBuilder.Normal(face.Direction),
                            host.Normal) > .999f));
            }
            Assert.Equal(shelf.Body.Identity,
                shelf.LandingSurface?.StructuralOwnerIdentity ?? shelf.Body.Identity);
            if (shelf.Family == MegastationMegaShelfFamily.FullSpan)
            {
                (float minimum, float maximum) = SpanAlong(
                    baseline.CavityEnvelope, shelf.Body.Up);
                float top = Vector3.Dot(
                    shelf.Body.Centre + shelf.Body.Up * shelf.Body.Size.Y * .5f,
                    shelf.Body.Up);
                Assert.True(MegastationMegaShelfPlanner.FullSpanLeavesNavigablePassage(
                    minimum, maximum, top, shelf.Body.Size.Y));
            }
        });
        Assert.All(plan.Shelves, shelf =>
        {
            MegastationMegaShelfClearanceVolume operating =
                MegastationMegaShelfPlanner.CreateOperatingVolume(
                    shelf.Body,
                    shelf.HostWallIdentities.Select(identity => hostWalls[identity]).ToArray(),
                    shelf.LandingSurface is not null);
            Assert.True(structuralClearance.IsOrientedBoxClear(
                operating.Centre, operating.Right, operating.Up, operating.Forward,
                operating.Size, out string blocker), blocker);
        });
        for (int first = 0; first < plan.Shelves.Count; first++)
        for (int second = first + 1; second < plan.Shelves.Count; second++)
        {
            MegastationMegaShelf a = plan.Shelves[first];
            MegastationMegaShelf b = plan.Shelves[second];
            bool sameBookcase = a.Family == MegastationMegaShelfFamily.Bookcase
                && b.Family == MegastationMegaShelfFamily.Bookcase;
            bool intentionalUJoin = sameBookcase
                && plan.Bookcases.SingleOrDefault()?.Layout == MegastationShelfMacroLayout.U
                && a.HostWallIdentity != b.HostWallIdentity
                && MathF.Abs(Vector3.Dot(a.Body.Centre - b.Body.Centre,
                    result.InteriorPlan.PortalUp)) < 3f;
            if (!intentionalUJoin)
                Assert.False(MegastationMegaShelfPlanner.StructuralVolumesIntersect(
                    a.Body, b.Body, 18f));
            if (!sameBookcase)
                Assert.False(MegastationMegaShelfPlanner.ClearanceVolumesIntersect(
                    a.ClearanceVolume, b.ClearanceVolume));
        }
        output.WriteLine($"{identity}: shelves={plan.Shelves.Count}; " +
            $"landing={plan.LandingSurfaces.Count}; walls=" +
            $"{string.Join(',', plan.Shelves.GroupBy(shelf => shelf.HostWallIdentity)
                .Select(group => $"{group.Key}:{group.Count()}"))}; " +
            $"families={plan.Diagnostics.CantileverAcceptedCount}/" +
            $"{plan.Diagnostics.CornerAcceptedCount}/" +
            $"{plan.Diagnostics.FullSpanAcceptedCount}; " +
            $"familyCandidates={plan.Diagnostics.CantileverCandidateCount}/" +
            $"{plan.Diagnostics.CornerCandidateCount}/" +
            $"{plan.Diagnostics.FullSpanCandidateCount}; " +
            $"viable={plan.Diagnostics.CornerViableCandidateCount}/" +
            $"{plan.Diagnostics.FullSpanViableCandidateCount}; " +
            $"rejects={plan.Diagnostics.SupportRejectCount}support/" +
            $"{plan.Diagnostics.StructuralRejectCount}structure/" +
            $"{plan.Diagnostics.ArrivalExclusionRejectCount}arrival/" +
            $"{plan.Diagnostics.FullSpanFlightClearanceRejectCount}span-flight; " +
            $"{plan.Diagnostics.Summary}");

        static (float Minimum, float Maximum) SpanAlong(
            MegastationInteriorVolume volume, Vector3 axis)
        {
            float[] values =
            [
                Vector3.Dot(volume.Minimum, axis),
                Vector3.Dot(volume.Maximum, axis),
            ];
            return (values.Min(), values.Max());
        }
    }

    [Fact]
    public void ProductionShelfIsDeterministicSupportedAndStructurallyParticipating()
    {
        MegastationPrototypeCpuResult result = Result.Value;
        MegastationMegaShelfPlan plan = result.MegaShelfPlan;
        MegastationMegaShelfPlan replanned = MegastationMegaShelfPlanner.Plan(
            result.InteriorPlan with
            {
                AddedStructuralSolids = null,
                AdditionalLandingSurfaces = null,
                AddedStructuralMarkers = null,
                AddedStructuralTrusses = null,
            },
            result.RegularisedOccupancy,
            result.BoundaryTopology,
            new(ForcedShelfCount: 1, ForceLandingSiteOnShelf: true));
        MegastationMegaShelfPlan withAdditionalShelf = MegastationMegaShelfPlanner.Plan(
            result.InteriorPlan with
            {
                AddedStructuralSolids = null,
                AdditionalLandingSurfaces = null,
                AddedStructuralMarkers = null,
                AddedStructuralTrusses = null,
            },
            result.RegularisedOccupancy,
            result.BoundaryTopology,
            new(ForcedShelfCount: 2, ForceLandingSiteOnShelf: true));

        Assert.Single(plan.Shelves);
        Assert.Equal(plan.Diagnostics.Signature, replanned.Diagnostics.Signature);
        Assert.Equal(plan.Shelves[0].Trusses, withAdditionalShelf.Shelves[0].Trusses);
        MegastationMegaShelf shelf = plan.Shelves[0];
        MegastationLandingSurface shelfSurface = Assert.IsType<MegastationLandingSurface>(
            shelf.LandingSurface);
        Assert.NotEmpty(shelf.SupportingFaces);
        Assert.All(shelf.SupportingFaces, key => Assert.Contains(
            result.BoundaryTopology.Faces,
            face => face.Key == key
                && face.SpaceKind == MegastationBoundarySpaceKind.InteriorBoundary));
        Assert.True(shelf.Body.CastsStellarShadow);
        Assert.True(shelf.Body.CastsStaticArtificialShadow);
        Assert.Equal(12, plan.Diagnostics.VisibleTriangleCount);
        Assert.Equal(12, plan.Diagnostics.CasterTriangleCount);
        Assert.False(result.InteriorPlan.ArrivalManeuverExclusion.IntersectsOrientedBox(
            shelf.Body.Centre, shelf.Body.Right, shelf.Body.Up, shelf.Body.Forward,
            shelf.Body.Size));
        Assert.Equal(shelf.Body.Identity, shelfSurface.StructuralOwnerIdentity);
        Assert.Equal(Enum.GetValues<MegastationMegaShelfSurfaceRole>().Order(),
            shelf.SurfaceRoles.Order());
        Assert.InRange(shelf.Trusses.Count, 2, 4);
        Assert.NotEmpty(shelf.Markers);
        Assert.Equal(shelf.Trusses.Count, plan.Diagnostics.TrussCount);
        Assert.True(plan.Diagnostics.TrussVisibleTriangleCount > 0);
        Assert.Equal(plan.Diagnostics.TrussVisibleTriangleCount,
            plan.Diagnostics.TrussCasterTriangleCount);
        output.WriteLine(
            $"{plan.Diagnostics.Summary}; candidates={plan.Diagnostics.CandidateCount}; " +
            $"rejects={plan.Diagnostics.SupportRejectCount}support/" +
            $"{plan.Diagnostics.StructuralRejectCount}structural/" +
            $"{plan.Diagnostics.ArrivalExclusionRejectCount}arrival/" +
            $"{plan.Diagnostics.ShelfOverlapRejectCount}overlap/" +
            $"{plan.Diagnostics.VerticalSeparationRejectCount}separation/" +
            $"{plan.Diagnostics.CompositionRejectCount}composition; " +
            $"markers={plan.Diagnostics.MarkerCount}; " +
            $"trusses={plan.Diagnostics.TrussCount}" +
            $"({plan.Diagnostics.BoxTrussCount}box/" +
            $"{plan.Diagnostics.TriangularTrussCount}tri)," +
            $"triangles={plan.Diagnostics.TrussVisibleTriangleCount}");
    }

    [Fact]
    public void ExistingL2PlannerPlacesCompleteCardinalSiteInsideShelfSurface()
    {
        MegastationPrototypeCpuResult result = Result.Value;
        MegastationMegaShelf shelf = Assert.Single(result.MegaShelfPlan.Shelves);
        MegastationLandingSurface shelfSurface = Assert.IsType<MegastationLandingSurface>(
            shelf.LandingSurface);
        MegastationLandingSitePlan? site = result.LandingDistrictPlan.Sites.SingleOrDefault(
            candidate => candidate.LandingSurfaceIdentity == shelfSurface.Identity);
        Assert.True(site is not null,
            $"surface={shelfSurface.UsableSize}; " +
            $"shelf={result.MegaShelfPlan.Diagnostics.Summary}; " +
            $"surfaces={result.LandingDistrictPlan.Diagnostics.LandingSurfaceSummary}; " +
            $"sites={result.LandingDistrictPlan.Diagnostics.SiteSummary}; " +
            $"rejects={result.LandingDistrictPlan.Diagnostics.StructuralRejectionSummary}");

        Assert.Contains(site!.Orientation,
            Enum.GetValues<MegastationLandingSiteOrientation>());
        Assert.True(site.PadIds.Count >= 1,
            $"Forced visual shelf should prove a complete shelf site; " +
            $"{result.LandingDistrictPlan.Diagnostics.SiteSummary}");
        Assert.True(Vector3.Dot(site.FloorNormal, result.InteriorPlan.PortalUp) > .9999f);
        Assert.True(Vector3.Dot(site.ApronCentre, shelf.LandingSurface.Up)
            > Vector3.Dot(result.InteriorPlan.CavityEnvelope.Minimum,
                shelf.LandingSurface.Up));
        AssertEnvelopeInsideSurface(site, shelfSurface,
            result.InteriorPlan.PortalRight, result.InteriorPlan.OutwardNormal);
        Assert.All(result.LandingDistrictPlan.Pads.Where(pad =>
            site.PadIds.Contains(pad.PadId)), pad =>
            Assert.True(Vector3.Dot(pad.PadSurface.Normal,
                shelf.LandingSurface.Up) > .9999f));
        Assert.Equal(1, result.LandingDistrictPlan.Diagnostics.ShelfLandingSiteCount);
        Assert.Equal(2, result.LandingDistrictPlan.Diagnostics.LandingSurfaceCount);
    }

    [Fact]
    public void LargeBayCanAcceptMoreThanTwoBayWideShelvesWithoutOverlap()
    {
        MegastationPrototypeCpuResult result = Result.Value;
        MegastationInteriorPlan baseline = result.InteriorPlan with
        {
            AddedStructuralSolids = null,
            AdditionalLandingSurfaces = null,
            AddedStructuralMarkers = null,
            AddedStructuralTrusses = null,
        };
        MegastationMegaShelfPlan plan = MegastationMegaShelfPlanner.Plan(
            baseline, result.RegularisedOccupancy, result.BoundaryTopology,
            new(ForcedShelfCount: 6, ForceLandingSiteOnShelf: false));
        MegastationMegaShelfPlan natural = MegastationMegaShelfPlanner.Plan(
            baseline, result.RegularisedOccupancy, result.BoundaryTopology,
            MegastationMegaShelfDevelopmentOptions.Runtime);

        Assert.True(plan.Shelves.Count > 2, plan.Diagnostics.Summary);
        Assert.Equal(plan.Shelves.Count,
            plan.Shelves.Select(shelf => shelf.Identity).Distinct().Count());
        Assert.Equal(plan.LandingSurfaces.Count,
            plan.LandingSurfaces.Select(surface => surface.Identity).Distinct().Count());
        Assert.All(plan.Shelves, shelf =>
        {
            Assert.NotEmpty(shelf.SupportingFaces);
            Assert.False(baseline.ArrivalManeuverExclusion.IntersectsOrientedBox(
                shelf.Body.Centre, shelf.Body.Right, shelf.Body.Up, shelf.Body.Forward,
                shelf.Body.Size));
            Assert.All(shelf.Trusses, truss =>
                Assert.StartsWith(shelf.Identity, truss.Identity, StringComparison.Ordinal));
        });
        for (int first = 0; first < plan.Shelves.Count; first++)
        for (int second = first + 1; second < plan.Shelves.Count; second++)
        {
            Assert.False(MegastationMegaShelfPlanner.StructuralVolumesIntersect(
                plan.Shelves[first].Body, plan.Shelves[second].Body, 18f));
            Assert.False(MegastationMegaShelfPlanner.ClearanceVolumesIntersect(
                plan.Shelves[first].ClearanceVolume,
                plan.Shelves[second].ClearanceVolume));
        }
        Assert.True(plan.Shelves.Select(shelf => shelf.HostWallIdentity).Distinct().Count() > 1,
            plan.Diagnostics.Summary);
        Assert.Contains(plan.Shelves.GroupBy(shelf => shelf.HostWallIdentity), group =>
            group.Select(shelf => Vector3.Dot(shelf.Body.Centre, shelf.Body.Up))
                .Distinct().Count() > 1);

        MegastationInteriorPlan composedInterior = baseline with
        {
            AddedStructuralSolids = plan.StructuralSolids,
            AdditionalLandingSurfaces = plan.LandingSurfaces,
            AddedStructuralMarkers = plan.Markers,
            AddedStructuralTrusses = plan.Trusses,
        };
        MegastationLandingDistrictPlan landing = MegastationLandingDistrictPlanner.Plan(
            composedInterior, result.RegularisedOccupancy, plan,
            forceLandingSiteOnShelf: false);
        Assert.Contains(landing.Sites, site => site.LandingSurfaceIdentity == "main-floor");
        Dictionary<string, MegastationLandingSurface> shelfSurfaces = plan.LandingSurfaces
            .ToDictionary(surface => surface.Identity, StringComparer.Ordinal);
        foreach (MegastationLandingSitePlan site in landing.Sites.Where(site =>
                     site.LandingSurfaceIdentity != "main-floor"))
            AssertEnvelopeInsideSurface(site, shelfSurfaces[site.LandingSurfaceIdentity],
                baseline.PortalRight, baseline.OutwardNormal);
        output.WriteLine($"L4c forced composition: {plan.Diagnostics.Summary}; " +
            $"rejects={plan.Diagnostics.ShelfOverlapRejectCount}overlap/" +
            $"{plan.Diagnostics.VerticalSeparationRejectCount}separation/" +
            $"{plan.Diagnostics.ArrivalExclusionRejectCount}arrival; " +
            $"natural={natural.Shelves.Count}:{natural.Diagnostics.Summary}");
    }

    [Fact]
    public void ShelfBodyJoinsExistingCasterAndArtificialOcclusionWithoutNewResource()
    {
        MegastationPrototypeCpuResult result = Result.Value;
        MegastationMegaShelfPlan shelves = result.MegaShelfPlan;
        Assert.Contains(result.InteriorMesh.DecorClassRanges,
            range => range.decorClass == DecorClass.MegastationInteriorMajor);

        MegastationArtificialOcclusion withoutShelf = MegastationArtificialOcclusion.Build(
            result.RegularisedOccupancy,
            result.LandingDistrictPlan,
            result.InteriorPresentationPlan,
            result.BayFacilityPlan,
            []);
        MegastationArtificialOcclusion withShelf = MegastationArtificialOcclusion.Build(
            result.RegularisedOccupancy,
            result.LandingDistrictPlan,
            result.InteriorPresentationPlan,
            result.BayFacilityPlan,
            shelves.StructuralSolids);
        Assert.Equal(withoutShelf.Diagnostics(result.ArtificialLightingPlan.Lights.Count)
                .OccluderCount + shelves.StructuralSolids.Count,
            withShelf.Diagnostics(result.ArtificialLightingPlan.Lights.Count).OccluderCount);
    }

    [Fact]
    public void ShelfRootExcludesWallHabitation()
    {
        MegastationPrototypeCpuResult result = Result.Value;
        HashSet<BoundaryFaceKey> roots = result.MegaShelfPlan.Shelves
            .SelectMany(shelf => shelf.SupportingFaces).ToHashSet();
        Dictionary<string, MegastationBayWallSurface> walls =
            MegastationBayHabitationPlanner.CreateWalls(result.InteriorPlan)
                .ToDictionary(wall => wall.Identity, StringComparer.Ordinal);
        foreach (MegastationBayHabitationRegion region in result.BayHabitationPlan.Regions)
        {
            MegastationBayWallSurface wall = walls[region.WallIdentity];
            Assert.True(MegastationBayHabitationPlanner.TryFindSupportingFace(
                wall, region.Centre, region.Size, 1.5f,
                result.Grid, result.BoundaryTopology, out BoundaryFace? support));
            Assert.NotNull(support);
            Assert.DoesNotContain(support!.Key, roots);
        }
    }

    private static void AssertEnvelopeInsideSurface(
        MegastationLandingSitePlan site,
        MegastationLandingSurface surface,
        Vector3 canonicalRight,
        Vector3 canonicalForward)
    {
        float centreRight = Vector3.Dot(surface.Centre, canonicalRight);
        float centreForward = Vector3.Dot(surface.Centre, canonicalForward);
        Assert.InRange(site.InfrastructureEnvelope.RightMinimum,
            centreRight - surface.UsableSize.X * .5f,
            centreRight + surface.UsableSize.X * .5f);
        Assert.InRange(site.InfrastructureEnvelope.RightMaximum,
            centreRight - surface.UsableSize.X * .5f,
            centreRight + surface.UsableSize.X * .5f);
        Assert.InRange(site.InfrastructureEnvelope.ForwardMinimum,
            centreForward - surface.UsableSize.Y * .5f,
            centreForward + surface.UsableSize.Y * .5f);
        Assert.InRange(site.InfrastructureEnvelope.ForwardMaximum,
            centreForward - surface.UsableSize.Y * .5f,
            centreForward + surface.UsableSize.Y * .5f);
    }
}
