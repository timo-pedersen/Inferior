using Inferior.Core.Math;
using Inferior.Game;
using Inferior.Game.Ships;
using Inferior.Gameplay;
using Inferior.Gameplay.Hull;
using Inferior.Gameplay.Ship;
using Microsoft.Xna.Framework;
using Xunit;
using Xunit.Abstractions;

namespace Inferior.Game.Test;

public sealed class ShipRotationTests(ITestOutputHelper output)
{
    [Fact]
    public void Cube_HasEqualAxisInertia()
    {
        DVec3 inertia = ShipRotation.CalculateBoxInertiaKgM2(
            12.0,
            new DVec3(2.0, 2.0, 2.0));

        Assert.Equal(inertia.X, inertia.Y, 12);
        Assert.Equal(inertia.Y, inertia.Z, 12);
    }

    [Fact]
    public void LongThinBox_HasExpectedAxisOrdering()
    {
        DVec3 inertia = ShipRotation.CalculateBoxInertiaKgM2(
            12.0,
            new DVec3(1.0, 2.0, 3.0));

        Assert.True(inertia.Z < inertia.Y);
        Assert.True(inertia.Y < inertia.X);
        Assert.Equal(new DVec3(13.0, 10.0, 5.0), inertia);
    }

    [Fact]
    public void WideBox_HasExpectedFormulaValues()
    {
        DVec3 inertia = ShipRotation.CalculateBoxInertiaKgM2(
            12.0,
            new DVec3(4.0, 2.0, 3.0));

        Assert.Equal(new DVec3(13.0, 25.0, 20.0), inertia);
    }

    [Fact]
    public void Inertia_ScalesLinearlyWithMassAndQuadraticallyWithDimensions()
    {
        DVec3 baseline = ShipRotation.CalculateBoxInertiaKgM2(
            100.0,
            new DVec3(2.0, 3.0, 4.0));
        DVec3 doubleMass = ShipRotation.CalculateBoxInertiaKgM2(
            200.0,
            new DVec3(2.0, 3.0, 4.0));
        DVec3 doubleDimensions = ShipRotation.CalculateBoxInertiaKgM2(
            100.0,
            new DVec3(4.0, 6.0, 8.0));

        Assert.Equal(baseline * 2.0, doubleMass);
        Assert.Equal(baseline * 4.0, doubleDimensions);
    }

    [Fact]
    public void Inertia_RejectsInvalidMassAndDimensions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ShipRotation.CalculateBoxInertiaKgM2(0.0, new DVec3(1.0, 1.0, 1.0)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ShipRotation.CalculateBoxInertiaKgM2(1.0, new DVec3(0.0, 1.0, 1.0)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ShipRotation.CalculateBoxInertiaKgM2(1.0, new DVec3(1.0, -1.0, 1.0)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ShipRotation.CalculateBoxInertiaKgM2(1.0, new DVec3(1.0, 1.0, double.NaN)));
    }

    [Fact]
    public void AngularAcceleration_IsTorqueDividedByEachAxisInertia()
    {
        var bounds = new ShipPresentationBounds(DVec3.Zero, new DVec3(1.0, 2.0, 3.0));

        ShipRotationCapability rotation = ShipRotation.Resolve(
            12.0,
            bounds,
            availableRotationalTorqueNm: 130.0);

        Assert.Equal(10.0, rotation.AvailableAngularAccelerationRadPerSec2.X, 12);
        Assert.Equal(13.0, rotation.AvailableAngularAccelerationRadPerSec2.Y, 12);
        Assert.Equal(26.0, rotation.AvailableAngularAccelerationRadPerSec2.Z, 12);
    }

    [Fact]
    public void AssistedTarget_UsesPartialInputAndPitchAsymmetry()
    {
        var ship = new Ship();

        DVec3 positive = ShipRotation.ResolveTargetAngularVelocity(
            ship,
            RotationCommand.Clamp(1.0, 0.5, -0.25));
        DVec3 negativePitch = ShipRotation.ResolveTargetAngularVelocity(
            ship,
            RotationCommand.Clamp(-1.0, 0.0, 0.0));

        Assert.Equal(new DVec3(1.4, 0.5, 0.375), positive);
        Assert.Equal(-1.0, negativePitch.X, 12);
    }

    [Fact]
    public void AssistedVelocity_AcceleratesBrakesReversesAndDoesNotOvershoot()
    {
        var acceleration = new DVec3(2.0, 2.0, 2.0);

        DVec3 accelerated = ShipRotation.MoveTowardsTarget(
            DVec3.Zero,
            new DVec3(1.0, 0.0, 0.0),
            acceleration,
            0.25);
        DVec3 braking = ShipRotation.MoveTowardsTarget(
            accelerated,
            DVec3.Zero,
            acceleration,
            0.1);
        DVec3 reversing = ShipRotation.MoveTowardsTarget(
            braking,
            new DVec3(-1.0, 0.0, 0.0),
            acceleration,
            0.1);
        DVec3 stopped = ShipRotation.MoveTowardsTarget(
            new DVec3(0.1, 0.0, 0.0),
            DVec3.Zero,
            acceleration,
            1.0);

        Assert.Equal(0.5, accelerated.X, 12);
        Assert.Equal(0.3, braking.X, 12);
        Assert.Equal(0.1, reversing.X, 12);
        Assert.Equal(0.0, stopped.X, 12);
        Assert.Equal(
            new DVec3(1.0, 0.0, 0.0),
            ShipRotation.MoveTowardsTarget(
                new DVec3(1.0, 0.0, 0.0),
                new DVec3(1.0, 0.0, 0.0),
                acceleration,
                1.0));
    }

    [Fact]
    public void OrientationIntegration_HandlesZeroSingleAndCombinedAxes()
    {
        var zero = new Ship();
        zero.IntegrateAngularVelocity(1.0);
        Assert.Equal(Quaternion.Identity, zero.Orientation);

        var pitch = new Ship();
        pitch.SetAngularVelocityLocal(new DVec3(1.0, 0.0, 0.0));
        pitch.IntegrateAngularVelocity(0.5);
        Quaternion expected = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.5f);
        AssertQuaternionEquivalent(expected, pitch.Orientation, 1e-6);

        var combined = new Ship();
        combined.SetAngularVelocityLocal(new DVec3(0.4, -0.3, 0.2));
        for (int i = 0; i < 1_000; i++)
            combined.IntegrateAngularVelocity(1.0 / 120.0);
        AssertFiniteNormalized(combined.Orientation);
    }

    [Fact]
    public void OrientationIntegration_IsStableAcrossReasonableTickSizes()
    {
        Quaternion sixtyHz = IntegrateForOneSecond(1.0 / 60.0);
        Quaternion oneTwentyHz = IntegrateForOneSecond(1.0 / 120.0);

        AssertQuaternionEquivalent(sixtyHz, oneTwentyHz, 2e-5);
    }

    [Fact]
    public void ConfiguredBoundsCache_InvalidatesWhenEnginesChange()
    {
        Ship ship = BuildConfiguredShip(AntegaHullDefinitionFactory.HullId);
        ShipPresentationBounds installed =
            ShipPresentationBoundsCalculator.GetConfiguredBounds(ship);

        foreach (var mount in ship.EngineMounts)
            mount.RemoveInstalledEngine();
        ShipPresentationBounds removed =
            ShipPresentationBoundsCalculator.GetConfiguredBounds(ship);

        Assert.NotEqual(installed, removed);
        Assert.True(installed.Size.Length > removed.Size.Length);
    }

    [Fact]
    public void SimulationInput_RampsAndBrakesAngularVelocityWithoutInstantStop()
    {
        Ship ship = BuildConfiguredShip(AriesHullDefinitionFactory.HullId);
        var simulation = new SpaceSimulation();
        simulation.SetShip(ship);

        for (int i = 0; i < 3; i++)
            simulation.DebugTickPhysics(PlayerInput.Zero with { PitchInput = 1.0 }, 0.1);
        double heldVelocity = ship.AngularVelocityLocalRadPerSec.X;
        simulation.DebugTickPhysics(PlayerInput.Zero, 0.1);

        Assert.InRange(heldVelocity, 0.0, ship.TurnRatePitchUp);
        Assert.InRange(ship.AngularVelocityLocalRadPerSec.X, 0.0, heldVelocity);
        Assert.NotEqual(0.0, ship.AngularVelocityLocalRadPerSec.X);
        Assert.Equal(
            ship.AngularVelocityLocalRadPerSec,
            simulation.ShipState!.Rotation!.AngularVelocityLocalRadPerSec);
    }

    [Fact]
    public void TeleportResetsAngularVelocity()
    {
        Ship ship = BuildConfiguredShip(AriesHullDefinitionFactory.HullId);
        ship.SetAngularVelocityLocal(new DVec3(0.5, -0.25, 0.75));
        var simulation = new SpaceSimulation();
        simulation.SetShip(ship);

        simulation.TeleportShip(new DVec3(10.0, 20.0, 30.0), Quaternion.Identity);
        simulation.DebugTickPhysics(PlayerInput.Zero, 0.0);

        Assert.Equal(DVec3.Zero, ship.AngularVelocityLocalRadPerSec);
        Assert.Equal(DVec3.Zero, simulation.ShipState!.Rotation!.AngularVelocityLocalRadPerSec);
    }

    [Fact]
    public void ShipCyclePreservesAngularVelocityAtTransition()
    {
        Ship ship = BuildConfiguredShip(AriesHullDefinitionFactory.HullId);
        DVec3 angularVelocity = new(0.25, -0.5, 0.75);
        ship.SetAngularVelocityLocal(angularVelocity);
        var simulation = new SpaceSimulation();
        simulation.SetShip(ship);

        simulation.RequestCycleShipHull();
        simulation.DebugTickPhysics(PlayerInput.Zero, 0.0);

        Assert.Equal(
            AsteriskHullDefinitionFactory.HullId,
            simulation.ShipState!.HullTypeId);
        Assert.Equal(
            angularVelocity,
            simulation.ShipState.Rotation!.AngularVelocityLocalRadPerSec);
    }

    [Theory]
    [InlineData(AriesHullDefinitionFactory.HullId)]
    [InlineData(AsteriskHullDefinitionFactory.HullId)]
    [InlineData(BerenHullDefinitionFactory.HullId)]
    [InlineData(AntegaHullDefinitionFactory.HullId)]
    public void ConfiguredShips_ExposeInspectableRotationCapability(string hullId)
    {
        Ship ship = BuildConfiguredShip(hullId);
        ShipPresentationBounds bounds =
            ShipPresentationBoundsCalculator.GetConfiguredBounds(ship);
        ShipPropulsionCapability propulsion = ShipPropulsion.Resolve(ship);
        ShipRotationCapability rotation = ShipRotation.Resolve(
            ship.Mass,
            bounds,
            propulsion.AvailableRotationalTorqueNm);

        Assert.True(rotation.ConfiguredDimensionsMeters.X > 0.0);
        Assert.True(rotation.ConfiguredDimensionsMeters.Y > 0.0);
        Assert.True(rotation.ConfiguredDimensionsMeters.Z > 0.0);
        Assert.True(rotation.AxisInertiaKgM2.X > 0.0);
        Assert.True(rotation.AvailableAngularAccelerationRadPerSec2.X > 0.0);

        output.WriteLine(
            $"{hullId}: mass={ship.Mass:F0}; bounds={rotation.ConfiguredDimensionsMeters}; " +
            $"engines={propulsion.InstalledEngineCount}; torque={rotation.AvailableRotationalTorqueNm:E4}; " +
            $"inertia={rotation.AxisInertiaKgM2}; accel={rotation.AvailableAngularAccelerationRadPerSec2}; " +
            $"time=({ship.TurnRatePitchUp / rotation.AvailableAngularAccelerationRadPerSec2.X:F2}, " +
            $"{ship.TurnRateYaw / rotation.AvailableAngularAccelerationRadPerSec2.Y:F2}, " +
            $"{ship.TurnRateRoll / rotation.AvailableAngularAccelerationRadPerSec2.Z:F2})");
    }

    private static Ship BuildConfiguredShip(string hullId)
        => ShipBuilder.NewShip(hullId)
            .WithDefaultStartingComponents()
            .Build();

    private static Quaternion IntegrateForOneSecond(double dt)
    {
        var ship = new Ship();
        ship.SetAngularVelocityLocal(new DVec3(0.4, -0.3, 0.2));
        int ticks = (int)Math.Round(1.0 / dt);
        for (int i = 0; i < ticks; i++)
            ship.IntegrateAngularVelocity(dt);
        return ship.Orientation;
    }

    private static void AssertQuaternionEquivalent(
        Quaternion expected,
        Quaternion actual,
        double tolerance)
    {
        double dot = Math.Abs(Quaternion.Dot(expected, actual));
        Assert.InRange(dot, 1.0 - tolerance, 1.0 + tolerance);
    }

    private static void AssertFiniteNormalized(Quaternion value)
    {
        Assert.True(float.IsFinite(value.X));
        Assert.True(float.IsFinite(value.Y));
        Assert.True(float.IsFinite(value.Z));
        Assert.True(float.IsFinite(value.W));
        Assert.InRange(Math.Abs(value.Length() - 1.0f), 0.0f, 1e-5f);
    }
}
