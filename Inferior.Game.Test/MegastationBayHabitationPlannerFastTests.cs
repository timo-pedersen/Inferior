using Inferior.Game.StationGen.Megastations;
using Microsoft.Xna.Framework;
using Xunit;

namespace Inferior.Game.Test;

public sealed class MegastationBayHabitationPlannerFastTests
{
    [Fact]
    public void MinimumBudgetProvidesCredibleFloorAndScalesWithUsableArea()
    {
        Assert.Equal(0, MegastationBayHabitationPlanner.MinimumRegionBudget(0f));
        Assert.Equal(6, MegastationBayHabitationPlanner.MinimumRegionBudget(90_000f));
        Assert.Equal(8, MegastationBayHabitationPlanner.MinimumRegionBudget(240_000f));
        Assert.Equal(12, MegastationBayHabitationPlanner.MinimumRegionBudget(900_000f));
    }

    [Fact]
    public void CompleteReservationsRejectOverlapWithoutCouplingSeparateWalls()
    {
        MegastationBayHabitationReservation cutout = Reservation(
            "cutout", "left", new(0f, 0f), new(40f, 18f));
        MegastationBayHabitationReservation crossingWindows = Reservation(
            "windows", "left", new(18f, 0f), new(12f, 8f));
        MegastationBayHabitationReservation clearWindows = Reservation(
            "clear", "left", new(30f, 20f), new(12f, 8f));
        MegastationBayHabitationReservation otherWall = Reservation(
            "other", "right", new(0f, 0f), new(40f, 18f));

        Assert.True(MegastationBayFacilityPlanner.ReservationsOverlap(
            cutout, crossingWindows));
        Assert.False(MegastationBayFacilityPlanner.ReservationsOverlap(
            cutout, clearWindows));
        Assert.False(MegastationBayFacilityPlanner.ReservationsOverlap(
            cutout, otherWall));
    }

    [Fact]
    public void SupportingFaceRequiresTheCompleteOpeningAndMargin()
    {
        var grid = new SliceGrid(
            [10f, 10f, 10f], [10f, 10f, 10f], [10f, 10f, 10f],
            1..2, 1..2, 1..2);
        var occupancy = new StructuralOccupancy(grid);
        occupancy.MarkStructural(1, 1, 1);
        occupancy.ProtectEmpty(2, 1, 1, MegacellVoidKind.InteriorFlightVolume);
        ExteriorSpace.ClassifyExternallyAccessibleEmpty(occupancy);
        BoundaryTopology topology = BoundaryTopologyBuilder.Build(
            occupancy, MegastationPrototypeSettings.Default);
        var wall = new MegastationBayWallSurface(
            "wall", MegastationBayWallKind.Left,
            new Vector3(5f, 0f, 0f), Vector3.UnitX, -Vector3.UnitZ,
            Vector3.UnitY, 10f, 10f, true);

        Assert.True(MegastationBayHabitationPlanner.TryFindSupportingFace(
            wall, Vector2.Zero, new Vector2(6f), 1f, grid, topology,
            out BoundaryFace? supporting));
        Assert.NotNull(supporting);
        Assert.False(MegastationBayHabitationPlanner.TryFindSupportingFace(
            wall, Vector2.Zero, new Vector2(9f, 6f), 1f, grid, topology,
            out _));
        Assert.False(MegastationBayHabitationPlanner.TryFindSupportingFace(
            wall, new Vector2(3f, 0f), new Vector2(6f), 1f, grid, topology,
            out _));
    }

    private static MegastationBayHabitationReservation Reservation(
        string identity,
        string wall,
        Vector2 centre,
        Vector2 size)
        => new(identity, wall, identity,
            MegastationBayHabitationReservationKind.PlainWindowBand,
            centre, size, 0f, 1f);
}
