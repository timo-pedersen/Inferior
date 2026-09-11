using Inferior.Game.StationGen.Megastations;
using Microsoft.Xna.Framework;
using Xunit;

namespace Inferior.Game.Test;

public sealed class MegastationShelfLandingIntegrationFastTests
{
    [Fact]
    public void SetLevelOccupancyIsDeterministicAndKeepsAClearMajority()
    {
        for (int shelfCount = 2; shelfCount <= 12; shelfCount++)
        for (int seed = 0; seed < 64; seed++)
        {
            int first = MegastationLandingDistrictPlanner
                .DesiredOccupiedShelfCount(seed, shelfCount);
            int second = MegastationLandingDistrictPlanner
                .DesiredOccupiedShelfCount(seed, shelfCount);
            Assert.Equal(first, second);
            Assert.InRange(first, shelfCount / 2 + 1, shelfCount);
        }

        int[] singleShelf = Enumerable.Range(0, 512)
            .Select(seed => MegastationLandingDistrictPlanner
                .DesiredOccupiedShelfCount(seed, 1)).Distinct().Order().ToArray();
        Assert.Equal([0, 1], singleShelf);
    }

    [Fact]
    public void IntegratedLayoutFitsARealShallowShelfWithoutShrinkingPadMargins()
    {
        var shallowUsable = new Vector2(116f, 132f);

        Assert.True(MegastationLandingDistrictPlanner.CanHostMinimumSite(
            shallowUsable, MegastationLandingSiteLayout.WallIntegrated));
        Assert.False(MegastationLandingDistrictPlanner.CanHostMinimumSite(
            shallowUsable, MegastationLandingSiteLayout.Freestanding));

        Vector2 integrated = MegastationLandingDistrictPlanner.MinimumShelfBodySize(
            MegastationLandingSiteLayout.WallIntegrated);
        Vector2 freestanding = MegastationLandingDistrictPlanner.MinimumShelfBodySize(
            MegastationLandingSiteLayout.Freestanding);
        Assert.Equal(integrated.X, freestanding.X);
        Assert.True(integrated.Y < freestanding.Y);
    }

    [Fact]
    public void WallToEdgeReservationRejectsIntrusionButLeavesAdjacentSpaceUsable()
    {
        var reservation = new MegastationLandingSiteReservation(
            "reservation", "site", "bay-wall:left",
            new Vector3(0f, 22f, 60f), new Vector3(80f, 44f, 120f),
            Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ,
            new Vector2(0f, 22f), new Vector2(80f, 44f));

        Assert.True(reservation.IntersectsOrientedBox(
            new Vector3(0f, 10f, 100f), Vector3.UnitX, Vector3.UnitY,
            Vector3.UnitZ, new Vector3(20f, 20f, 20f)));
        Assert.False(reservation.IntersectsOrientedBox(
            new Vector3(55f, 10f, 100f), Vector3.UnitX, Vector3.UnitY,
            Vector3.UnitZ, new Vector3(20f, 20f, 20f)));

        Assert.True(MegastationBayHabitationPlanner.ReservationOverlapsRegion(
            "bay-wall:left", Vector2.Zero, new Vector2(20f, 10f), reservation));
        Assert.False(MegastationBayHabitationPlanner.ReservationOverlapsRegion(
            "bay-wall:right", Vector2.Zero, new Vector2(20f, 10f), reservation));
        Assert.False(MegastationBayHabitationPlanner.ReservationOverlapsRegion(
            "bay-wall:left", new Vector2(60f, 0f), new Vector2(10f), reservation));
    }
}
