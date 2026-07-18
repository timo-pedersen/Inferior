using Inferior.Core.Math;
using Inferior.Game;
using Inferior.Game.Input;
using Inferior.Game.Ships;
using Inferior.Gameplay;
using Inferior.Gameplay.Engines;
using Inferior.Gameplay.Ship;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Xunit;

namespace Inferior.Game.Test;

public sealed class EngineDebugCyclingTests
{
    [Fact]
    public void Cycle_TransitionsMuleToNeedleToEmptyToNewMulePair()
    {
        var simulation = new SpaceSimulation();
        Ship ship = ShipBuilder.NewShip("type-1").Build();
        simulation.SetShip(ship);
        simulation.DebugSetFlightModeImmediately(FlightMode.Docked);
        simulation.DebugTickPhysics(PlayerInput.Zero, 1.0 / 60.0);

        EngineInstance[] originalMules = AssertPair(
            ship,
            MuleEngineDefinitionFactory.H2VariantId);

        Cycle(simulation);
        EngineInstance[] needles = AssertPair(
            ship,
            NeedleEngineDefinitionFactory.H2VariantId);
        Assert.NotSame(needles[0], needles[1]);
        Assert.DoesNotContain(needles, engine =>
            originalMules.Any(original => ReferenceEquals(original, engine)));
        Assert.All(originalMules, engine => Assert.False(engine.IsInstalled));

        Cycle(simulation);
        Assert.All(ship.EngineMounts, mount => Assert.Null(mount.InstalledEngine));
        Assert.All(
            simulation.ShipState!.EngineMounts!,
            mount => Assert.Null(mount.InstalledEngine));
        Assert.All(needles, engine => Assert.False(engine.IsInstalled));

        Cycle(simulation);
        EngineInstance[] replacementMules = AssertPair(
            ship,
            MuleEngineDefinitionFactory.H2VariantId);
        Assert.DoesNotContain(replacementMules, engine =>
            originalMules.Any(original => ReferenceEquals(original, engine)));
        Assert.NotEqual(replacementMules[0].InstanceId, replacementMules[1].InstanceId);
    }

    [Fact]
    public void Cycle_PreservesHullMountsAndShipPhysicsState()
    {
        var simulation = new SpaceSimulation();
        Ship ship = ShipBuilder.NewShip("type-1")
            .WithPosition(new DVec3(125.0, -42.0, 880.0))
            .WithOrientation(Quaternion.CreateFromYawPitchRoll(0.4f, -0.2f, 0.1f))
            .Build();
        ship.Velocity = new DVec3(13.0, -7.0, 29.0);
        simulation.SetShip(ship);
        simulation.DebugSetFlightModeImmediately(FlightMode.Docked);

        string hullTypeId = ship.HullTypeId;
        DVec3 position = ship.Position;
        DVec3 velocity = ship.Velocity;
        Quaternion orientation = ship.Orientation;
        EngineMount[] mounts = ship.EngineMounts.ToArray();
        string[] mountIds = mounts.Select(mount => mount.MountId).ToArray();
        EngineMountPose[] mountPoses = mounts.Select(mount => mount.Pose).ToArray();

        Cycle(simulation, 0.0);

        Assert.Equal(hullTypeId, ship.HullTypeId);
        Assert.Equal(position, ship.Position);
        Assert.Equal(velocity, ship.Velocity);
        Assert.Equal(orientation, ship.Orientation);
        Assert.Equal(mountIds, ship.EngineMounts.Select(mount => mount.MountId));
        Assert.Equal(mountPoses, ship.EngineMounts.Select(mount => mount.Pose));
        Assert.Same(mounts[0], ship.EngineMounts[0]);
        Assert.Same(mounts[1], ship.EngineMounts[1]);
    }

    [Fact]
    public void Cycle_FromPartialOrUnknownConfigurationReturnsToMulePair()
    {
        var simulation = new SpaceSimulation();
        Ship ship = ShipBuilder.NewShip("type-1")
            .WithEngineVariant(NeedleEngineDefinitionFactory.H2VariantId)
            .Build();
        simulation.SetShip(ship);
        simulation.DebugSetFlightModeImmediately(FlightMode.Docked);
        ship.EngineMounts.Single(mount =>
            mount.Side == EngineMountSide.Port).RemoveInstalledEngine();

        Cycle(simulation);

        AssertPair(ship, MuleEngineDefinitionFactory.H2VariantId);
    }

    [Theory]
    [InlineData(Keys.LeftControl)]
    [InlineData(Keys.RightControl)]
    public void PlatformInput_RecognizesControlF2RisingEdge(Keys control)
    {
        var pressed = new KeyboardState(control, Keys.F2);

        Assert.True(EngineDebugCyclePlatformInput.IsCycleJustPressed(
            pressed,
            new KeyboardState()));
        Assert.False(EngineDebugCyclePlatformInput.IsCycleJustPressed(pressed, pressed));
        Assert.False(EngineDebugCyclePlatformInput.IsCycleJustPressed(
            new KeyboardState(Keys.F2),
            new KeyboardState()));
        Assert.False(EngineDebugCyclePlatformInput.IsCycleJustPressed(
            new KeyboardState(Keys.LeftShift, Keys.F2),
            new KeyboardState()));
    }

    private static void Cycle(
        SpaceSimulation simulation,
        double dt = 1.0 / 60.0)
    {
        simulation.RequestDebugCycleEngineConfiguration();
        simulation.DebugTickPhysics(PlayerInput.Zero, dt);
    }

    private static EngineInstance[] AssertPair(Ship ship, string expectedVariantId)
    {
        EngineInstance[] engines = ship.EngineMounts
            .Select(mount => mount.InstalledEngine)
            .OfType<EngineInstance>()
            .ToArray();
        Assert.Equal(2, engines.Length);
        Assert.All(engines, engine =>
            Assert.Equal(expectedVariantId, engine.Variant.VariantId));
        return engines;
    }
}
