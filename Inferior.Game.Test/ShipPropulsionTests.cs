using Inferior.Core.Math;
using Inferior.Game;
using Inferior.Game.Ships;
using Inferior.Gameplay;
using Inferior.Gameplay.Engines;
using Inferior.Gameplay.Hull;
using Inferior.Gameplay.Ship;
using Microsoft.Xna.Framework;
using Xunit;

namespace Inferior.Game.Test;

public sealed class ShipPropulsionTests
{
    [Fact]
    public void EngineDefinitions_ExposeValidatedDistinctPhysicalValues()
    {
        EngineDefinition mule = MuleEngineDefinitionFactory.CreateDefinition();
        EngineDefinition needle = NeedleEngineDefinitionFactory.CreateDefinition();
        EngineDefinition atlas = AtlasEngineDefinitionFactory.CreateDefinition();

        Assert.All([mule, needle, atlas], definition =>
        {
            Assert.True(definition.DryMassKg > 0.0);
            Assert.True(definition.ForwardThrustN > 0.0);
            Assert.True(definition.ManeuveringThrustN >= 0.0);
            Assert.True(definition.RotationalTorqueNm >= 0.0);
        });
        Assert.Equal(3, new[] { mule.ForwardThrustN, needle.ForwardThrustN, atlas.ForwardThrustN }
            .Distinct()
            .Count());
    }

    [Fact]
    public void EngineDefinition_RejectsInvalidPhysicalValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Definition(massKg: -1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Definition(forwardThrustN: 0.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Definition(maneuveringThrustN: -1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Definition(rotationalTorqueNm: -1.0));
    }

    [Theory]
    [InlineData(AriesHullDefinitionFactory.HullId, 78_000.0, 4_800.0, 2, 312_000.0, 156_000.0, 500_000.0, 4.0, 2.0)]
    [InlineData(AsteriskHullDefinitionFactory.HullId, 15_600.0, 2_400.0, 1, 117_000.0, 58_500.0, 150_000.0, 7.5, 3.75)]
    [InlineData(BerenHullDefinitionFactory.HullId, 187_800.0, 6_600.0, 4, 751_200.0, 375_600.0, 1_200_000.0, 4.0, 2.0)]
    [InlineData(AntegaHullDefinitionFactory.HullId, 3_585_200.0, 384_000.0, 4, 14_340_800.0, 3_585_200.0, 40_000_000.0, 4.0, 1.0)]
    public void ConfiguredShips_AggregateMassAndPropulsion(
        string hullId,
        double expectedMass,
        double expectedEngineMass,
        int expectedEngineCount,
        double expectedForwardThrust,
        double expectedManeuveringThrust,
        double expectedTorque,
        double expectedForwardAcceleration,
        double expectedManeuveringAcceleration)
    {
        Ship ship = BuildConfiguredShip(hullId);
        ShipPropulsionCapability propulsion = ShipPropulsion.Resolve(ship);

        Assert.Equal(expectedMass, ship.Mass, 6);
        Assert.Equal(expectedEngineMass, propulsion.InstalledEngineMassKg, 6);
        Assert.Equal(expectedEngineCount, propulsion.InstalledEngineCount);
        Assert.Equal(expectedEngineCount, propulsion.OperationalEngineCount);
        Assert.Equal(expectedForwardThrust, propulsion.AvailableForwardForceShipLocalN.Length, 3);
        Assert.Equal(expectedManeuveringThrust, propulsion.AvailableManeuveringThrustN, 3);
        Assert.Equal(expectedTorque, propulsion.AvailableRotationalTorqueNm, 3);
        Assert.Equal(expectedForwardAcceleration, expectedForwardThrust / propulsion.CurrentMassKg, 6);
        Assert.Equal(expectedManeuveringAcceleration, expectedManeuveringThrust / propulsion.CurrentMassKg, 6);
    }

    [Fact]
    public void NoInstalledEngines_AddNoMassOrForce()
    {
        var ship = new Ship { HullMass = 1_000.0 };

        ShipPropulsionCapability propulsion = ShipPropulsion.Resolve(ship);

        Assert.Equal(1_000.0, propulsion.CurrentMassKg);
        Assert.Equal(0.0, propulsion.InstalledEngineMassKg);
        Assert.Equal(DVec3.Zero, propulsion.AvailableForwardForceShipLocalN);
    }

    [Fact]
    public void FullyDamagedEngine_RetainsMassButContributesNoAuthority()
    {
        (Ship ship, EngineInstance engine) = BuildSingleEngineShip(Quaternion.Identity);
        engine.SetDamageFraction(1.0);

        ShipPropulsionCapability propulsion = ShipPropulsion.Resolve(ship);

        Assert.Equal(1, propulsion.InstalledEngineCount);
        Assert.Equal(0, propulsion.OperationalEngineCount);
        Assert.Equal(engine.Variant.Engine.DryMassKg, propulsion.InstalledEngineMassKg);
        Assert.Equal(DVec3.Zero, propulsion.AvailableForwardForceShipLocalN);
        Assert.Equal(0.0, propulsion.AvailableManeuveringThrustN);
        Assert.Equal(0.0, propulsion.AvailableRotationalTorqueNm);
    }

    [Fact]
    public void InstalledOrientation_TransformsEngineLocalForwardForce()
    {
        Quaternion orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathHelper.PiOver2);
        (Ship ship, EngineInstance engine) = BuildSingleEngineShip(orientation);

        ShipPropulsionCapability propulsion = ShipPropulsion.Resolve(ship);
        Vector3 expectedFloat = Vector3.Transform(-Vector3.UnitZ, orientation);
        DVec3 expected = new(expectedFloat.X, expectedFloat.Y, expectedFloat.Z);

        Assert.True(
            (propulsion.AvailableForwardForceShipLocalN
             - expected * engine.Variant.Engine.ForwardThrustN).Length < 0.01);
    }

    [Fact]
    public void TranslationCommand_ClampsAxesAndDiagonalMagnitude()
    {
        DVec3 command = ShipPropulsion.ClampTranslationCommand(2.0, 1.0, -3.0);

        Assert.Equal(1.0, command.Length, 12);
        Assert.InRange(command.X, -1.0, 1.0);
        Assert.InRange(command.Y, -1.0, 1.0);
        Assert.InRange(command.Z, -1.0, 1.0);
    }

    [Fact]
    public void GearOneSnapshot_UsesInstalledForceAndCurrentMass()
    {
        Ship ship = BuildConfiguredShip(AntegaHullDefinitionFactory.HullId);
        var simulation = new SpaceSimulation();
        simulation.SetShip(ship);

        simulation.DebugTickPhysics(PlayerInput.Zero with { ThrustForward = 1.0 }, 0.25);

        ShipPropulsionSnapshot propulsion = Assert.IsType<ShipPropulsionSnapshot>(
            simulation.ShipState!.Propulsion);
        Assert.Equal(0, simulation.ShipState.NewtonianGear);
        Assert.Equal(3_585_200.0, propulsion.CurrentMassKg, 3);
        Assert.Equal(14_340_800.0, propulsion.AppliedForceShipLocalN.Length, 3);
        Assert.Equal(4.0, propulsion.ResultingAccelerationShipLocalMps2.Length, 6);
    }

    [Fact]
    public void HigherGear_ChangesCeilingButNotLowSpeedEngineForce()
    {
        Ship ship = BuildConfiguredShip(AntegaHullDefinitionFactory.HullId);
        var simulation = new SpaceSimulation();
        simulation.SetShip(ship);

        simulation.DebugTickPhysics(
            PlayerInput.Zero with { ThrustForward = 1.0, GearUp = true },
            0.25);

        ShipPropulsionSnapshot propulsion = Assert.IsType<ShipPropulsionSnapshot>(
            simulation.ShipState!.Propulsion);
        Assert.Equal(1, simulation.ShipState.NewtonianGear);
        Assert.Equal(14_340_800.0, propulsion.AppliedForceShipLocalN.Length, 3);
        Assert.Equal(4.0, propulsion.ResultingAccelerationShipLocalMps2.Length, 6);
    }

    [Fact]
    public void EqualForce_ProducesHalfAccelerationAtDoubleMass()
    {
        var light = new ShipPropulsionCapability(
            1_000.0, 1_000.0, 0.0, 0, 0, 0.0, DVec3.Zero, 0.0, 0.0);
        var heavy = light with { CurrentMassKg = 2_000.0 };
        var force = new DVec3(0.0, 0.0, -10_000.0);

        DVec3 lightAcceleration =
            ShipPropulsion.Apply(light, force).ResultingAccelerationShipLocalMps2;
        DVec3 heavyAcceleration =
            ShipPropulsion.Apply(heavy, force).ResultingAccelerationShipLocalMps2;

        Assert.Equal(lightAcceleration.Length / 2.0, heavyAcceleration.Length, 12);
    }

    [Fact]
    public void AsteriskEfficiency_IsHullOwnedAndDoesNotAffectAriesAfterEngineRemoval()
    {
        Ship asterisk = BuildConfiguredShip(AsteriskHullDefinitionFactory.HullId);
        Ship aries = BuildConfiguredShip(AriesHullDefinitionFactory.HullId);
        aries.EngineMounts[0].RemoveInstalledEngine();

        ShipPropulsionCapability asteriskPropulsion = ShipPropulsion.Resolve(asterisk);
        ShipPropulsionCapability ariesPropulsion = ShipPropulsion.Resolve(aries);
        EngineDefinition mule = MuleEngineDefinitionFactory.CreateDefinition();

        Assert.Equal(mule.ForwardThrustN * 0.75, asteriskPropulsion.AvailableForwardForceShipLocalN.Length, 6);
        Assert.Equal(mule.ManeuveringThrustN * 0.75, asteriskPropulsion.AvailableManeuveringThrustN, 6);
        Assert.Equal(mule.ForwardThrustN, ariesPropulsion.AvailableForwardForceShipLocalN.Length, 6);
        Assert.Equal(mule.ManeuveringThrustN, ariesPropulsion.AvailableManeuveringThrustN, 6);
    }

    private static Ship BuildConfiguredShip(string hullId)
        => ShipBuilder.NewShip(hullId)
            .WithDefaultStartingComponents()
            .Build();

    private static (Ship Ship, EngineInstance Engine) BuildSingleEngineShip(Quaternion orientation)
    {
        var ship = new Ship { HullMass = 1_000.0 };
        var mount = new EngineMount(
            "test.mount",
            "test.slot",
            EngineMountStandardIds.H2,
            EngineMountSide.Starboard,
            new EngineMountPose(DVec3.Zero, DVec3.UnitX, DVec3.UnitY));
        ship.AddEngineMount(mount);
        EngineInstance engine = EngineInstallationGenerator.Install(
            MuleEngineDefinitionFactory.CreateH2Variant(),
            mount,
            orientation);
        return (ship, engine);
    }

    private static EngineDefinition Definition(
        double massKg = 1.0,
        double forwardThrustN = 1.0,
        double maneuveringThrustN = 0.0,
        double rotationalTorqueNm = 0.0)
        => new(
            "test-engine",
            "Test Engine",
            new DVec3(1.0, 1.0, 1.0),
            massKg,
            forwardThrustN,
            maneuveringThrustN,
            rotationalTorqueNm);
}
