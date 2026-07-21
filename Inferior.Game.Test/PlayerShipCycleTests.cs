using Inferior.Core.Math;
using Inferior.Game;
using Inferior.Game.Ships;
using Inferior.Gameplay;
using Inferior.Gameplay.Hull;
using Microsoft.Xna.Framework;
using Xunit;

namespace Inferior.Game.Test;

public sealed class PlayerShipCycleTests
{
    [Fact]
    public void Catalog_CyclesAriesAsteriskBerenAntegaAndWraps()
    {
        Assert.Equal(
            [
                AriesHullDefinitionFactory.HullId,
                AsteriskHullDefinitionFactory.HullId,
                BerenHullDefinitionFactory.HullId,
                AntegaHullDefinitionFactory.HullId,
            ],
            PlayerShipCycleCatalog.HullTypeIds);
        Assert.Equal(
            AsteriskHullDefinitionFactory.HullId,
            PlayerShipCycleCatalog.GetNext(AriesHullDefinitionFactory.HullId));
        Assert.Equal(
            BerenHullDefinitionFactory.HullId,
            PlayerShipCycleCatalog.GetNext(AsteriskHullDefinitionFactory.HullId));
        Assert.Equal(
            AntegaHullDefinitionFactory.HullId,
            PlayerShipCycleCatalog.GetNext(BerenHullDefinitionFactory.HullId));
        Assert.Equal(
            AriesHullDefinitionFactory.HullId,
            PlayerShipCycleCatalog.GetNext(AntegaHullDefinitionFactory.HullId));
    }

    [Fact]
    public void SimulationCycle_PreservesWorldKinematicsAndPublishesReplacement()
    {
        DVec3 position = new(1200.0, -44.0, 9850.0);
        DVec3 velocity = new(18.0, -2.0, 31.0);
        Quaternion orientation =
            Quaternion.CreateFromYawPitchRoll(0.35f, -0.27f, 0.14f);
        var ship = ShipBuilder.NewShip(AsteriskHullDefinitionFactory.HullId)
            .WithPosition(position)
            .WithOrientation(orientation)
            .Build();
        ship.Velocity = velocity;
        var simulation = new SpaceSimulation();
        simulation.SetShip(ship);
        simulation.DebugSetFlightModeImmediately(FlightMode.Docked);

        simulation.RequestCycleShipHull();
        simulation.TickForTests(PlayerInput.Zero, 0.0);

        SpaceSimulation.ShipSnapshot snapshot = simulation.ShipState!;
        Assert.Equal(BerenHullDefinitionFactory.HullId, snapshot.HullTypeId);
        Assert.Equal(position, snapshot.Position);
        Assert.Equal(velocity, snapshot.Velocity);
        AssertQuaternion(orientation, snapshot.Orientation);
        Assert.Equal(4, snapshot.EngineMounts!.Count);
        Assert.Equal(
            BerenHullDefinitionFactory.HullId,
            PlayerShipCycleCatalog.HullTypeIds.Single(id => id == snapshot.HullTypeId));
    }

    [Fact]
    public void SimulationCycle_ProcessesRepeatedRequestsInOrder()
    {
        var simulation = new SpaceSimulation();
        simulation.SetShip(
            ShipBuilder.NewShip(AriesHullDefinitionFactory.HullId).Build());
        simulation.DebugSetFlightModeImmediately(FlightMode.Docked);

        simulation.RequestCycleShipHull();
        simulation.RequestCycleShipHull();
        simulation.TickForTests(PlayerInput.Zero, 0.0);

        Assert.Equal(
            BerenHullDefinitionFactory.HullId,
            simulation.ShipState!.HullTypeId);
    }

    private static void AssertQuaternion(Quaternion expected, Quaternion actual)
    {
        float dot = Math.Abs(Quaternion.Dot(expected, actual));
        Assert.InRange(dot, 0.99999f, 1.00001f);
    }
}
