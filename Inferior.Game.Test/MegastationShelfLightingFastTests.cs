using Inferior.Game.StationGen;
using Inferior.Game.StationGen.Megastations;
using Inferior.Game.States;
using Microsoft.Xna.Framework;
using Xunit;

namespace Inferior.Game.Test;

public sealed class MegastationShelfLightingFastTests
{
    [Fact]
    public void EveryShelfFamilyHasExplicitSafeLightingCoverage()
    {
        MegastationMegaShelf[] shelves = Enum.GetValues<MegastationMegaShelfFamily>()
            .Select((family, index) => Shelf(family, index)).ToArray();
        MegastationShelfLightingPlan plan = MegastationShelfLightingPlanner.Plan(
            481516, ShelfPlan(shelves));

        Assert.All(shelves, shelf => Assert.Contains(plan.FloodFixtures,
            fixture => fixture.ShelfIdentity == shelf.Identity));
        string fullSpanIdentity = shelves.Single(shelf =>
            shelf.Family == MegastationMegaShelfFamily.FullSpan).Identity;
        Assert.DoesNotContain(plan.Beacons,
            beacon => beacon.ShelfIdentity == fullSpanIdentity);
        Assert.Equal(plan.FloodFixtures.Count, plan.ArtificialLights.Count);
        Assert.DoesNotContain(plan.ArtificialLights,
            light => light.Identity.Contains("beacon", StringComparison.Ordinal));
        StationLightInfo[] glowLights = MegastationPrototypeGenerator
            .CreateShelfObstacleLights(plan).ToArray();
        Assert.Equal(plan.Beacons.Count, glowLights.Length);
        Assert.All(glowLights, glow =>
        {
            Assert.Equal(GlowType.WarningStrobe, glow.Type);
            Assert.Equal(LightPattern.Strobe, glow.Pattern);
            Assert.Null(glow.SurfaceNormal);
            Assert.Null(glow.PresentationSizePixels);
            Assert.Equal(4f, glow.PresentationSizeScale);
            Assert.Equal(5f, glow.PresentationMinimumSizePixels);
            Assert.Equal(280f, glow.PresentationMaximumSizePixels);
            Assert.Equal(.5f, glow.Rate);
            MegastationShelfObstacleBeacon beacon = plan.Beacons.Single(item =>
                Vector3.Distance(item.Position + item.Up * .40f, glow.WorldPosition) < .001f);
            Assert.True(Vector3.Dot(glow.WorldPosition - beacon.Position, beacon.Up) > .39f);
        });
        foreach (MegastationMegaShelf shelf in shelves.Where(shelf =>
                     shelf.Family != MegastationMegaShelfFamily.FullSpan))
        {
            MegastationShelfObstacleBeacon[] shelfBeacons = plan.Beacons
                .Where(beacon => beacon.ShelfIdentity == shelf.Identity).ToArray();
            Assert.Equal(shelfBeacons.Count(beacon =>
                    beacon.Mount == MegastationShelfBeaconMount.Top),
                shelfBeacons.Count(beacon =>
                    beacon.Mount == MegastationShelfBeaconMount.Underside));
            Assert.All(shelfBeacons.Where(beacon =>
                beacon.Mount == MegastationShelfBeaconMount.Underside), beacon =>
                Assert.True(Vector3.Dot(beacon.Up, shelf.Body.Up) < -.999f));
        }
        StationLightInfo[] floodGlows = MegastationPrototypeGenerator
            .CreateShelfFloodGlowLights(plan).ToArray();
        Assert.Equal(plan.FloodFixtures.Count, floodGlows.Length);
        Assert.All(floodGlows, glow =>
        {
            Assert.Equal(GlowType.ShelfWorkFlood, glow.Type);
            Assert.Equal(LightPattern.Continuous, glow.Pattern);
            Assert.Equal(Color.White, glow.Colour);
            Assert.Equal(0f, glow.Rate);
            Assert.Equal(4f, glow.PresentationSizeScale);
            Assert.Equal(24f, glow.PresentationMinimumSizePixels);
            Assert.Equal(640f, glow.PresentationMaximumSizePixels);
            MegastationShelfFloodFixture fixture = plan.FloodFixtures.Single(item =>
                Vector3.Distance(item.Centre + item.Direction * .68f,
                    glow.WorldPosition) < .001f);
            Assert.True(Vector3.Dot(glow.WorldPosition - fixture.Centre,
                fixture.Direction) > .67f);
        });
        Assert.All(plan.FloodFixtures, fixture =>
        {
            MegastationMegaShelf shelf = shelves.Single(s => s.Identity == fixture.ShelfIdentity);
            Assert.True(Vector3.Dot(fixture.Direction, -shelf.Body.Up) >= .939f);
            Assert.True(fixture.TrussIdentity is not null || shelf.Trusses.Count == 0);
            Assert.True(float.IsFinite(fixture.Range) && fixture.Range > 0f);
            Assert.True(float.IsFinite(fixture.Intensity) && fixture.Intensity > 0f);
            Assert.Equal(MegastationShelfLightingPlanner.WorkLightIntensity,
                fixture.Intensity);
            int minimum = Math.Min(fixture.Colour.R,
                Math.Min(fixture.Colour.G, fixture.Colour.B));
            int maximum = Math.Max(fixture.Colour.R,
                Math.Max(fixture.Colour.G, fixture.Colour.B));
            Assert.True(minimum >= 242);
            Assert.True(maximum - minimum <= 10);
        });
    }

    [Fact]
    public void ClippedCornerUsesActualExposedBoundaryEndpoints()
    {
        MegastationMegaShelf shelf = Shelf(MegastationMegaShelfFamily.Corner, 2);
        Vector3[] corners = MegastationShelfLightingPlanner.ExposedCorners(shelf);

        Assert.Equal(2, corners.Length);
        Assert.Contains(corners, point => Near(point, new Vector3(120f, 142f, 80f)));
        Assert.Contains(corners, point => Near(point, new Vector3(150f, 142f, 50f)));
    }

    [Fact]
    public void BookcaseLevelsShareBlinkPhaseAndPlanIsDeterministic()
    {
        MegastationMegaShelf first = Shelf(MegastationMegaShelfFamily.Bookcase, 0);
        MegastationMegaShelf second = Shelf(MegastationMegaShelfFamily.Bookcase, 1);
        var bookcase = new MegastationBookcaseArrangement(
            "bookcase-A", MegastationShelfMacroLayout.Back, 2, [20f, 80f],
            ["wall"], [first.Identity, second.Identity],
            StructuralTrussCrossSection.Box, "test");
        MegastationMegaShelfPlan shelfPlan = ShelfPlan([first, second], [bookcase]);

        MegastationShelfLightingPlan a = MegastationShelfLightingPlanner.Plan(12345, shelfPlan);
        MegastationShelfLightingPlan b = MegastationShelfLightingPlanner.Plan(12345, shelfPlan);

        Assert.Equal(a.Signature, b.Signature);
        Assert.Single(a.Beacons.Select(beacon => beacon.Phase).Distinct());
        Assert.Single(a.Beacons.Select(beacon => beacon.Rate).Distinct());
    }

    [Fact]
    public void ShelfBeaconsUseProvenShortOnLongOffExteriorStrobe()
    {
        Assert.Equal(0f, SystemSpaceState.EvaluateGlowPattern(
            LightPattern.Strobe, .50f));
        Assert.Equal(1f, SystemSpaceState.EvaluateGlowPattern(
            LightPattern.Strobe, .10f));
    }

    private static MegastationMegaShelf Shelf(MegastationMegaShelfFamily family, int index)
    {
        string identity = $"shelf-{family}" + (index == 0 ? "" : $"-{index}");
        var body = new MegastationInteriorStructuralSolid(
            identity + "/body", MegastationInteriorStructuralRole.MegaShelf,
            new Vector3(0f, index * 70f, 0f), new Vector3(300f, 4f, 160f),
            Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, true, true,
            family == MegastationMegaShelfFamily.Corner ? 30f : 0f,
            family == MegastationMegaShelfFamily.Corner ? 1 : 0);
        IReadOnlyList<MegastationMegaShelfTruss> trusses =
            MegastationMegaShelfPlanner.BuildTrusses(identity, body, 9000 + index,
                force: true, family: family);
        return new(identity, family, "wall", ["wall"], [], body, null,
            MegastationMegaShelfUsage.Structural, MegastationMegaShelfVerticalBand.Mid,
            new(body.Centre, body.Size, body.Right, body.Up, body.Forward, false),
            [], [], trusses, 9000 + index);
    }

    private static MegastationMegaShelfPlan ShelfPlan(
        IReadOnlyList<MegastationMegaShelf> shelves,
        IReadOnlyList<MegastationBookcaseArrangement>? bookcases = null)
        => new(7, 12, shelves, shelves.Select(shelf => shelf.Body).ToArray(), [], [],
            shelves.SelectMany(shelf => shelf.Trusses).ToArray(), bookcases ?? [],
            new(
                AlgorithmVersion: MegastationMegaShelfPlanner.AlgorithmVersion,
                CandidateCount: 0, AcceptedCount: shelves.Count,
                SupportRejectCount: 0, StructuralRejectCount: 0,
                ArrivalExclusionRejectCount: 0, ShelfOverlapRejectCount: 0,
                VerticalSeparationRejectCount: 0, OperatingClearanceRejectCount: 0,
                LandingDowngradeCount: 0, FullSpanFlightClearanceRejectCount: 0,
                CompositionRejectCount: 0, CantileverCandidateCount: 0,
                CornerCandidateCount: 0, FullSpanCandidateCount: 0,
                CornerViableCandidateCount: 0, FullSpanViableCandidateCount: 0,
                CantileverAcceptedCount: 0, CornerAcceptedCount: 0,
                FullSpanAcceptedCount: 0, LowCandidateCount: 0, MidCandidateCount: 0,
                HighCandidateCount: 0, LowAcceptedCount: 0, MidAcceptedCount: 0,
                HighAcceptedCount: 0, MinimumWidth: 0f, MaximumWidth: 0f,
                MinimumProjection: 0f, MaximumProjection: 0f,
                MinimumThickness: 0f, MaximumThickness: 0f,
                VisibleTriangleCount: 0, CasterTriangleCount: 0, MarkerCount: 0,
                CornerMarkerCount: 0, LowerEdgeMarkerCount: 0, TrussCount: 0,
                BoxTrussCount: 0, TriangularTrussCount: 0,
                TrussVisibleTriangleCount: 0, TrussCasterTriangleCount: 0,
                Summary: "", Signature: ""));

    private static bool Near(Vector3 a, Vector3 b) => Vector3.Distance(a, b) < .001f;
}
