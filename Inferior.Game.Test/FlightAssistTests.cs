using Inferior.Core.DataBus;
using Inferior.Core.Math;
using Inferior.Game;
using Inferior.Game.Ships;
using Inferior.Gameplay;
using Inferior.Gameplay.Hull;
using Inferior.Gameplay.Ship;
using Xunit;

namespace Inferior.Game.Test;

public sealed class FlightAssistTests
{
    private const double Dt = 1.0;

    [Fact]
    public void FlightAssistDefaultsOnAndPublishesActiveTopic()
    {
        var simulation = CreateSimulation(out _);
        double? active = null;
        void Handler(double value) => active = value;

        DataBus.Drain();
        DataBus.Instruments.Subscribe(Topics.Flight.FlightAssistActive, Handler);
        try
        {
            simulation.DebugTickPhysics(PlayerInput.Zero, Dt);
            simulation.DebugPublish();
            DataBus.Drain();
        }
        finally
        {
            DataBus.Instruments.Unsubscribe(Topics.Flight.FlightAssistActive, Handler);
        }

        Assert.True(simulation.ShipState!.FlightAssistOn);
        Assert.Equal(1.0, active);
    }

    [Fact]
    public void FlightAssistTogglePublishesPilotMessages()
    {
        var simulation = CreateSimulation(out _);
        var messages = new List<string>();
        void Handler(SystemMessage message) => messages.Add(message.Text);

        DataBus.Drain();
        DataBus.System.Subscribe(Topics.System.All, Handler);
        try
        {
            simulation.DebugTickPhysics(PlayerInput.Zero with { FlightAssistToggle = true }, Dt);
            simulation.DebugTickPhysics(PlayerInput.Zero, Dt);
            simulation.DebugTickPhysics(PlayerInput.Zero with { FlightAssistToggle = true }, Dt);
            DataBus.Drain();
        }
        finally
        {
            DataBus.System.Unsubscribe(Topics.System.All, Handler);
        }

        Assert.Contains("flight assist off", messages);
        Assert.Contains("flight assist on", messages);
        Assert.True(simulation.ShipState!.FlightAssistOn);
    }

    [Fact]
    public void FlightAssistDampsLateralAndVerticalVelocityWithoutChangingForwardVelocity()
    {
        var simulation = CreateSimulation(out Ship ship);
        ship.Velocity = ship.Forward * 100.0
            + ship.Right * 50.0
            + ship.Up * 50.0;
        double initialSpeed = ship.Velocity.Length;
        double initialForwardSpeed = DVec3.Dot(ship.Velocity, ship.Forward);
        double initialLateralSpeed = Math.Abs(DVec3.Dot(ship.Velocity, ship.Right));
        double initialVerticalSpeed = Math.Abs(DVec3.Dot(ship.Velocity, ship.Up));

        simulation.DebugTickPhysics(PlayerInput.Zero, Dt);

        double finalSpeed = ship.Velocity.Length;
        double finalForwardSpeed = DVec3.Dot(ship.Velocity, ship.Forward);
        double finalLateralSpeed = Math.Abs(DVec3.Dot(ship.Velocity, ship.Right));
        double finalVerticalSpeed = Math.Abs(DVec3.Dot(ship.Velocity, ship.Up));

        Assert.InRange(finalForwardSpeed - initialForwardSpeed, -1e-9, 1e-9);
        Assert.True(finalLateralSpeed < initialLateralSpeed);
        Assert.True(finalVerticalSpeed < initialVerticalSpeed);
        Assert.True(finalSpeed <= initialSpeed);
    }

    [Fact]
    public void FlightAssistUsesStrongerLiftAuthorityForDownwardVelocityCorrection()
    {
        var simulation = CreateSimulation(out Ship ship);
        ship.Velocity = ship.Right * 100.0 - ship.Up * 100.0;
        double initialLateralSpeed = Math.Abs(DVec3.Dot(ship.Velocity, ship.Right));
        double initialVerticalSpeed = Math.Abs(DVec3.Dot(ship.Velocity, ship.Up));

        simulation.DebugTickPhysics(PlayerInput.Zero, Dt);

        double lateralCorrection =
            initialLateralSpeed - Math.Abs(DVec3.Dot(ship.Velocity, ship.Right));
        double verticalCorrection =
            initialVerticalSpeed - Math.Abs(DVec3.Dot(ship.Velocity, ship.Up));

        Assert.True(verticalCorrection > lateralCorrection);
    }

    [Fact]
    public void FlightAssistPublishesAppliedForceAndAccelerationTelemetry()
    {
        var simulation = CreateSimulation(out Ship ship);
        ship.Velocity = ship.Right * 100.0;
        double? force = null;
        double? acceleration = null;
        void ForceHandler(double value) => force = value;
        void AccelerationHandler(double value) => acceleration = value;

        DataBus.Drain();
        DataBus.Instruments.Subscribe(Topics.Flight.FlightAssistForceN, ForceHandler);
        DataBus.Instruments.Subscribe(Topics.Flight.FlightAssistAccelerationMs2, AccelerationHandler);
        try
        {
            simulation.DebugTickPhysics(PlayerInput.Zero, Dt);
            simulation.DebugPublish();
            DataBus.Drain();
        }
        finally
        {
            DataBus.Instruments.Unsubscribe(Topics.Flight.FlightAssistForceN, ForceHandler);
            DataBus.Instruments.Unsubscribe(Topics.Flight.FlightAssistAccelerationMs2, AccelerationHandler);
        }

        Assert.True(force.HasValue && force.Value > 0.0);
        Assert.True(acceleration.HasValue && acceleration.Value > 0.0);
    }

    private static SpaceSimulation CreateSimulation(out Ship ship)
    {
        ship = ShipBuilder.NewShip(CosmoHullDefinitionFactory.HullId).Build();
        var simulation = new SpaceSimulation();
        simulation.SetShip(ship);
        simulation.DebugSetFlightModeImmediately(FlightMode.SystemNewtonian);
        return simulation;
    }
}
