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
    public void EngineDefinitions_ExposeValidatedDistinctHarmonyAndDirectionalValues()
    {
        EngineDefinition mule = MuleEngineDefinitionFactory.CreateDefinition();
        EngineDefinition needle = NeedleEngineDefinitionFactory.CreateDefinition();
        EngineDefinition atlas = AtlasEngineDefinitionFactory.CreateDefinition();

        Assert.All([mule, needle, atlas], definition =>
        {
            Assert.True(definition.DryMassKg > 0.0);
            Assert.True(definition.MaximumForwardThrustN > 0.0);
            Assert.InRange(definition.ReverseThrustFraction, 0.0, 1.0);
            Assert.InRange(definition.LateralThrustFraction, 0.0, 1.0);
            Assert.InRange(definition.LiftThrustFraction, 0.0, 1.0);
            Assert.True(definition.HarmonyCount >= 2);
            Assert.InRange(definition.MinimumThrustFraction, double.Epsilon, 1.0);
        });
        Assert.Equal(0.50, atlas.LiftThrustFraction);
        Assert.Equal(3, new[]
        {
            mule.MaximumForwardThrustN,
            needle.MaximumForwardThrustN,
            atlas.MaximumForwardThrustN,
        }.Distinct().Count());
    }

    [Fact]
    public void EngineDefinition_RejectsInvalidPhysicalHarmonyAndDirectionalValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Definition(massKg: -1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Definition(maximumForwardThrustN: 0.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Definition(reverseFraction: -0.1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Definition(lateralFraction: 1.1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Definition(liftFraction: double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => Definition(rotationalTorqueNm: -1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Definition(harmonyCount: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Definition(minimumThrustFraction: 0.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Definition(minimumThrustFraction: 1.1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Definition(minimumSpeedCeilingMps: 0.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Definition(
            minimumSpeedCeilingMps: 100.0,
            maximumSpeedCeilingMps: 99.0));
    }

    [Theory]
    [InlineData(1, 0.0, 0.0, 0.1, 50.0)]
    [InlineData(2, 1.0 / 3.0, 1.0 / 9.0, 0.2, 155.55555555555554)]
    [InlineData(3, 2.0 / 3.0, 4.0 / 9.0, 0.5, 472.22222222222217)]
    [InlineData(4, 1.0, 1.0, 1.0, 1_000.0)]
    public void HarmonyCurve_UsesQuadraticPositionForThrustAndSpeed(
        int harmony,
        double expectedPosition,
        double expectedCurve,
        double expectedMultiplier,
        double expectedSpeed)
    {
        EngineHarmonyOutput output = Definition(harmonyCount: 4).ResolveHarmony(harmony);

        Assert.Equal(expectedPosition, output.NormalizedPosition, 12);
        Assert.Equal(expectedCurve, output.Curve, 12);
        Assert.Equal(expectedMultiplier, output.ThrustMultiplier, 12);
        Assert.Equal(expectedSpeed, output.SpeedCeilingMps, 9);
    }

    [Fact]
    public void HarmonyCount_ChangesResolutionButNotEndpoints()
    {
        EngineDefinition coarse = Definition(harmonyCount: 4);
        EngineDefinition fine = Definition(harmonyCount: 16);

        Assert.Equal(
            coarse.ResolveHarmony(1).ThrustMultiplier,
            fine.ResolveHarmony(1).ThrustMultiplier);
        Assert.Equal(
            coarse.ResolveHarmony(4).ThrustMultiplier,
            fine.ResolveHarmony(16).ThrustMultiplier);
        Assert.True(
            fine.ResolveHarmony(2).ThrustMultiplier
            < coarse.ResolveHarmony(2).ThrustMultiplier);
    }

    [Fact]
    public void HarmonyIndex_FailsClearlyOutsideDefinitionRange()
    {
        EngineDefinition definition = Definition(harmonyCount: 4);

        Assert.Throws<ArgumentOutOfRangeException>(() => definition.ResolveHarmony(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => definition.ResolveHarmony(5));

        (Ship _, EngineInstance engine) = BuildSingleEngineShip(definition);
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.SetSelectedHarmony(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.SetSelectedHarmony(5));
    }

    [Fact]
    public void HarmonyOutput_DerivesEveryChannelAndTorqueFromForwardMultiplier()
    {
        EngineDefinition definition = Definition(
            maximumForwardThrustN: 1_000.0,
            reverseFraction: 0.4,
            lateralFraction: 0.3,
            liftFraction: 0.8,
            rotationalTorqueNm: 2_000.0,
            harmonyCount: 4);

        EngineHarmonyOutput low = definition.ResolveHarmony(1);
        EngineHarmonyOutput high = definition.ResolveHarmony(4);

        Assert.Equal(100.0, low.MaximumForwardThrustN, 12);
        Assert.Equal(40.0, low.MaximumReverseThrustN, 12);
        Assert.Equal(30.0, low.MaximumLateralThrustN, 12);
        Assert.Equal(80.0, low.MaximumLiftThrustN, 12);
        Assert.Equal(200.0, low.MaximumRotationalTorqueNm, 12);
        Assert.Equal(1_000.0, high.MaximumForwardThrustN, 12);
        Assert.Equal(400.0, high.MaximumReverseThrustN, 12);
        Assert.Equal(300.0, high.MaximumLateralThrustN, 12);
        Assert.Equal(800.0, high.MaximumLiftThrustN, 12);
        Assert.Equal(2_000.0, high.MaximumRotationalTorqueNm, 12);
    }

    [Theory]
    [InlineData(1.0, 0.0, 0.0, 1.0, 1.0, 0.0, 0.0)]
    [InlineData(1.0, 1.0, 0.0, 1.4142135623730951, 0.7071067811865475, 0.7071067811865475, 0.0)]
    [InlineData(1.0, 1.0, 1.0, 1.7320508075688772, 0.5773502691896258, 0.5773502691896258, 0.5773502691896258)]
    [InlineData(0.2, 0.3, 0.4, 0.5385164807134505, 0.2, 0.3, 0.4)]
    [InlineData(-1.0, -1.0, 0.0, 1.4142135623730951, -0.7071067811865475, -0.7071067811865475, 0.0)]
    public void SharedEnvelope_NormalizesCombinedAxesWithoutChangingSubUnitCommands(
        double longitudinal,
        double lateral,
        double vertical,
        double expectedUsage,
        double expectedLongitudinal,
        double expectedLateral,
        double expectedVertical)
    {
        EngineTranslationAllocation allocation = ShipPropulsion.AllocateTranslation(
            new EngineTranslationCommand(longitudinal, lateral, vertical, UseLiftChannel: true));

        Assert.Equal(expectedUsage, allocation.Usage, 12);
        Assert.Equal(expectedLongitudinal, allocation.Longitudinal, 12);
        Assert.Equal(expectedLateral, allocation.Lateral, 12);
        Assert.Equal(expectedVertical, allocation.Vertical, 12);
        Assert.InRange(allocation.AllocatedAxes.Length, 0.0, 1.0 + 1e-12);
    }

    [Fact]
    public void ZeroTranslationCommand_ProducesNoForce()
    {
        Ship ship = BuildSingleEngineShip(Definition()).Ship;
        ShipPropulsionCapability capability = ShipPropulsion.Resolve(ship);
        EngineTranslationAllocation allocation = ShipPropulsion.AllocateTranslation(
            new EngineTranslationCommand(0.0, 0.0, 0.0, UseLiftChannel: true));

        Assert.Equal(DVec3.Zero, ShipPropulsion.ResolveAppliedForce(capability, allocation));
    }

    [Fact]
    public void VerticalTranslation_UsesLateralOrLiftChannelWithoutStacking()
    {
        (Ship ship, EngineInstance engine) = BuildSingleEngineShip(Definition(
            maximumForwardThrustN: 1_000.0,
            lateralFraction: 0.25,
            liftFraction: 0.75));
        engine.SetSelectedHarmony(engine.Variant.Engine.HarmonyCount);
        ShipPropulsionCapability capability = ShipPropulsion.Resolve(ship);

        double ordinaryUp = ResolveAxisForce(capability, 0.0, 0.0, 1.0, useLift: false).Y;
        double liftUp = ResolveAxisForce(capability, 0.0, 0.0, 1.0, useLift: true).Y;
        double down = ResolveAxisForce(capability, 0.0, 0.0, -1.0, useLift: true).Y;

        Assert.Equal(250.0, ordinaryUp, 9);
        Assert.Equal(750.0, liftUp, 9);
        Assert.Equal(-250.0, down, 9);
    }

    [Fact]
    public void UnavailableEngine_ProducesNoLiftOrOtherForce()
    {
        (Ship ship, EngineInstance engine) = BuildSingleEngineShip(Definition());
        engine.SetDamageFraction(1.0);
        ShipPropulsionCapability capability = ShipPropulsion.Resolve(ship);

        Assert.Equal(0.0, capability.AvailableLiftThrustN);
        Assert.Equal(DVec3.Zero, ResolveAxisForce(capability, 0.0, 0.0, 1.0, useLift: true));
    }

    [Fact]
    public void MixedEngines_UseIndependentHarmonyAndDirectionalFractions()
    {
        var ship = new Ship { HullMass = 1_000.0 };
        EngineInstance low = InstallEngine(ship, Definition(
            familyId: "low",
            maximumForwardThrustN: 1_000.0,
            lateralFraction: 0.2), 0);
        EngineInstance high = InstallEngine(ship, Definition(
            familyId: "high",
            maximumForwardThrustN: 2_000.0,
            lateralFraction: 0.5), 1);
        high.SetSelectedHarmony(high.Variant.Engine.HarmonyCount);

        ShipPropulsionCapability capability = ShipPropulsion.Resolve(ship);
        DVec3 forward = ResolveAxisForce(capability, 1.0, 0.0, 0.0, useLift: false);
        DVec3 lateral = ResolveAxisForce(capability, 0.0, 1.0, 0.0, useLift: false);

        Assert.Equal(2_100.0, forward.Length, 9);
        Assert.Equal(1_020.0, lateral.X, 9);
        Assert.Equal(2, capability.Engines.Count);
        Assert.Equal(50.0, capability.SpeedCeilingMps, 9);
        Assert.Equal(0.1, low.Variant.Engine.ResolveHarmony(low.SelectedHarmony).ThrustMultiplier);
    }

    [Fact]
    public void LongitudinalTaper_ScalesResolvedForce()
    {
        (Ship ship, EngineInstance engine) = BuildSingleEngineShip(Definition(
            maximumForwardThrustN: 1_000.0));
        engine.SetSelectedHarmony(engine.Variant.Engine.HarmonyCount);
        ShipPropulsionCapability capability = ShipPropulsion.Resolve(ship);
        EngineTranslationAllocation allocation = ShipPropulsion.AllocateTranslation(
            new EngineTranslationCommand(1.0, 0.0, 0.0, UseLiftChannel: false));

        DVec3 full = ShipPropulsion.ResolveAppliedForce(capability, allocation);
        DVec3 tapered = ShipPropulsion.ResolveAppliedForce(
            capability,
            allocation,
            longitudinalScale: 0.25);

        Assert.Equal(full * 0.25, tapered);
    }

    [Fact]
    public void EngineInstallationOrientation_TransformsCompleteForceContribution()
    {
        Quaternion orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathHelper.PiOver2);
        (Ship ship, EngineInstance engine) = BuildSingleEngineShip(Definition(), orientation);
        engine.SetSelectedHarmony(engine.Variant.Engine.HarmonyCount);
        ShipPropulsionCapability capability = ShipPropulsion.Resolve(ship);

        DVec3 applied = ResolveAxisForce(capability, 1.0, 0.0, 0.0, useLift: false);
        Vector3 expectedFloat = Vector3.Transform(-Vector3.UnitZ, orientation);
        DVec3 expected = new(expectedFloat.X, expectedFloat.Y, expectedFloat.Z);

        Assert.True((applied - expected * engine.Variant.Engine.MaximumForwardThrustN).Length < 0.01);
    }

    [Theory]
    [InlineData(AriesHullDefinitionFactory.HullId, 78_000.0, 4_800.0, 2, 1_560_000.0, 780_000.0, 1_170_000.0, 1_200_000.0)]
    [InlineData(AsteriskHullDefinitionFactory.HullId, 15_600.0, 2_400.0, 1, 585_000.0, 292_500.0, 438_750.0, 360_000.0)]
    [InlineData(BerenHullDefinitionFactory.HullId, 187_800.0, 6_600.0, 4, 3_756_000.0, 1_878_000.0, 2_817_000.0, 4_200_000.0)]
    [InlineData(AntegaHullDefinitionFactory.HullId, 3_585_200.0, 384_000.0, 4, 71_704_000.0, 17_926_000.0, 35_852_000.0, 360_000_000.0)]
    public void ConfiguredShips_AggregateMaximumHarmonyMassAndPropulsion(
        string hullId,
        double expectedMass,
        double expectedEngineMass,
        int expectedEngineCount,
        double expectedForward,
        double expectedLateral,
        double expectedLift,
        double expectedTorque)
    {
        Ship ship = BuildConfiguredShip(hullId);
        SetMaximumHarmony(ship);
        ShipPropulsionCapability propulsion = ShipPropulsion.Resolve(ship);

        Assert.Equal(expectedMass, ship.Mass, 6);
        Assert.Equal(expectedEngineMass, propulsion.InstalledEngineMassKg, 6);
        Assert.Equal(expectedEngineCount, propulsion.InstalledEngineCount);
        Assert.Equal(expectedForward, propulsion.AvailableForwardForceShipLocalN.Length, 3);
        Assert.Equal(expectedForward, propulsion.AvailableReverseThrustN, 3);
        Assert.Equal(expectedLateral, propulsion.AvailableLateralThrustN, 3);
        Assert.Equal(expectedLift, propulsion.AvailableLiftThrustN, 3);
        Assert.Equal(expectedTorque, propulsion.AvailableRotationalTorqueNm, 3);
    }

    [Fact]
    public void DefaultHarmony_ContributesMinimumRatherThanMaximumOutput()
    {
        Ship ship = BuildConfiguredShip(AriesHullDefinitionFactory.HullId);
        ShipPropulsionCapability propulsion = ShipPropulsion.Resolve(ship);

        Assert.Equal(156_000.0, propulsion.AvailableForwardForceShipLocalN.Length, 6);
        Assert.Equal(120_000.0, propulsion.AvailableRotationalTorqueNm, 6);
        Assert.Equal(50.0, propulsion.SpeedCeilingMps, 6);
    }

    [Fact]
    public void AsteriskEfficiencies_RemainHullOwnedAndDoNotFollowSingleEngineDamage()
    {
        Ship asterisk = BuildConfiguredShip(AsteriskHullDefinitionFactory.HullId);
        Ship aries = BuildConfiguredShip(AriesHullDefinitionFactory.HullId);
        SetMaximumHarmony(asterisk);
        SetMaximumHarmony(aries);
        aries.EngineMounts[0].RemoveInstalledEngine();

        ShipPropulsionCapability asteriskPropulsion = ShipPropulsion.Resolve(asterisk);
        ShipPropulsionCapability ariesPropulsion = ShipPropulsion.Resolve(aries);
        EngineDefinition mule = MuleEngineDefinitionFactory.CreateDefinition();

        Assert.Equal(mule.MaximumForwardThrustN * 0.75, asteriskPropulsion.AvailableForwardForceShipLocalN.Length, 6);
        Assert.Equal(mule.MaximumForwardThrustN * mule.LateralThrustFraction * 0.75, asteriskPropulsion.AvailableLateralThrustN, 6);
        Assert.Equal(mule.MaximumForwardThrustN, ariesPropulsion.AvailableForwardForceShipLocalN.Length, 6);
        Assert.Equal(mule.MaximumForwardThrustN * mule.LateralThrustFraction, ariesPropulsion.AvailableLateralThrustN, 6);
    }

    [Fact]
    public void LandingDiagnostics_TrackMassHarmonyAndLiftFractionWithoutLandabilityFlag()
    {
        (Ship ship, EngineInstance engine) = BuildSingleEngineShip(Definition(
            maximumForwardThrustN: 10_000.0,
            liftFraction: 0.5,
            massKg: 100.0), hullMass: 900.0);
        ShipPropulsionCapability low = ShipPropulsion.Resolve(ship);
        engine.SetSelectedHarmony(engine.Variant.Engine.HarmonyCount);
        ShipPropulsionCapability high = ShipPropulsion.Resolve(ship);

        double lowHover = ShipPropulsion.MaximumHoverGravityG(low);
        double highHover = ShipPropulsion.MaximumHoverGravityG(high);
        double expectedHigh = 5_000.0 / 1_000.0 / ShipPropulsion.StandardGravityMps2;

        Assert.Equal(expectedHigh, highHover, 12);
        Assert.Equal(highHover * 0.1, lowHover, 12);
        Assert.DoesNotContain("CanLand", File.ReadAllText(Path.Combine(
            RepoRoot(), "Inferior.Gameplay", "Ship", "ShipPropulsion.cs")));
    }

    [Fact]
    public void AddedMassLowersHoverGravityAndHigherLiftFractionRaisesIt()
    {
        (Ship lightShip, EngineInstance lightEngine) = BuildSingleEngineShip(
            Definition(maximumForwardThrustN: 10_000.0, liftFraction: 0.5, massKg: 100.0),
            hullMass: 900.0);
        (Ship heavyShip, EngineInstance heavyEngine) = BuildSingleEngineShip(
            Definition(maximumForwardThrustN: 10_000.0, liftFraction: 0.5, massKg: 100.0),
            hullMass: 1_900.0);
        (Ship highLiftShip, EngineInstance highLiftEngine) = BuildSingleEngineShip(
            Definition(maximumForwardThrustN: 10_000.0, liftFraction: 0.8, massKg: 100.0),
            hullMass: 900.0);
        lightEngine.SetSelectedHarmony(4);
        heavyEngine.SetSelectedHarmony(4);
        highLiftEngine.SetSelectedHarmony(4);

        double light = ShipPropulsion.MaximumHoverGravityG(ShipPropulsion.Resolve(lightShip));
        double heavy = ShipPropulsion.MaximumHoverGravityG(ShipPropulsion.Resolve(heavyShip));
        double highLift = ShipPropulsion.MaximumHoverGravityG(ShipPropulsion.Resolve(highLiftShip));

        Assert.True(heavy < light);
        Assert.True(highLift > light);
    }

    [Fact]
    public void AntegaMaximumHarmony_HasExpectedEmptyLiftRating()
    {
        Ship ship = BuildConfiguredShip(AntegaHullDefinitionFactory.HullId);
        SetMaximumHarmony(ship);
        ShipPropulsionCapability capability = ShipPropulsion.Resolve(ship);

        double liftAcceleration = capability.AvailableLiftThrustN / capability.CurrentMassKg;
        Assert.Equal(10.0, liftAcceleration, 6);
        Assert.Equal(10.0 / ShipPropulsion.StandardGravityMps2,
            ShipPropulsion.MaximumHoverGravityG(capability), 6);
    }

    [Fact]
    public void SimulationHarmonyShift_IncreasesForceCeilingAndRotationalTorque()
    {
        Ship ship = BuildConfiguredShip(AriesHullDefinitionFactory.HullId);
        var simulation = new SpaceSimulation();
        simulation.SetShip(ship);
        simulation.DebugTickPhysics(
            PlayerInput.Zero with { ThrustForward = 1.0, PitchInput = 1.0 },
            0.01);
        ShipPropulsionSnapshot low = simulation.ShipState!.Propulsion!;
        double lowAngularVelocity = ship.AngularVelocityLocalRadPerSec.X;
        ship.SetAngularVelocityLocal(DVec3.Zero);

        simulation.DebugTickPhysics(
            PlayerInput.Zero with
            {
                ThrustForward = 1.0,
                PitchInput = 1.0,
                GearUp = true,
                GearChangeSequence = 1,
                GearChangeSteps = 1,
            },
            0.01);
        ShipPropulsionSnapshot next = simulation.ShipState!.Propulsion!;

        Assert.Equal(0, low.Engines[0].SelectedHarmony - 1);
        Assert.Equal(2, next.Engines[0].SelectedHarmony);
        Assert.True(next.AvailableForwardForceShipLocalN.Length > low.AvailableForwardForceShipLocalN.Length);
        Assert.True(next.SpeedCeilingMps > low.SpeedCeilingMps);
        Assert.True(next.AvailableRotationalTorqueNm > low.AvailableRotationalTorqueNm);
        Assert.True(ship.AngularVelocityLocalRadPerSec.X > lowAngularVelocity);
    }

    [Fact]
    public void FullyDamagedEngine_RetainsMassButContributesNoAuthority()
    {
        (Ship ship, EngineInstance engine) = BuildSingleEngineShip(Definition());
        engine.SetDamageFraction(1.0);

        ShipPropulsionCapability propulsion = ShipPropulsion.Resolve(ship);

        Assert.Equal(1, propulsion.InstalledEngineCount);
        Assert.Equal(0, propulsion.OperationalEngineCount);
        Assert.Equal(engine.Variant.Engine.DryMassKg, propulsion.InstalledEngineMassKg);
        Assert.Equal(DVec3.Zero, propulsion.AvailableForwardForceShipLocalN);
        Assert.Equal(0.0, propulsion.AvailableLateralThrustN);
        Assert.Equal(0.0, propulsion.AvailableLiftThrustN);
        Assert.Equal(0.0, propulsion.AvailableRotationalTorqueNm);
        Assert.Equal(0.0, propulsion.SpeedCeilingMps);
    }

    private static DVec3 ResolveAxisForce(
        ShipPropulsionCapability capability,
        double longitudinal,
        double lateral,
        double vertical,
        bool useLift)
    {
        EngineTranslationAllocation allocation = ShipPropulsion.AllocateTranslation(
            new EngineTranslationCommand(longitudinal, lateral, vertical, useLift));
        return ShipPropulsion.ResolveAppliedForce(capability, allocation);
    }

    private static Ship BuildConfiguredShip(string hullId)
        => ShipBuilder.NewShip(hullId)
            .WithDefaultStartingComponents()
            .Build();

    private static void SetMaximumHarmony(Ship ship)
    {
        foreach (EngineInstance engine in ship.EngineMounts
            .Select(mount => mount.InstalledEngine)
            .OfType<EngineInstance>())
        {
            engine.SetSelectedHarmony(engine.Variant.Engine.HarmonyCount);
        }
    }

    private static (Ship Ship, EngineInstance Engine) BuildSingleEngineShip(
        EngineDefinition definition,
        Quaternion? orientation = null,
        double hullMass = 1_000.0)
    {
        var ship = new Ship { HullMass = hullMass };
        EngineInstance engine = InstallEngine(ship, definition, 0, orientation);
        return (ship, engine);
    }

    private static EngineInstance InstallEngine(
        Ship ship,
        EngineDefinition definition,
        int index,
        Quaternion? orientation = null)
    {
        var mount = new EngineMount(
            $"test.mount.{index}",
            $"test.slot.{index}",
            EngineMountStandardIds.H2,
            EngineMountSide.Starboard,
            new EngineMountPose(DVec3.Zero, DVec3.UnitX, DVec3.UnitY));
        ship.AddEngineMount(mount);
        var variant = new EngineVariantDefinition(
            $"{definition.FamilyId}.h2.{index}",
            definition,
            EngineMountStandardIds.H2);
        return EngineInstallationGenerator.Install(variant, mount, orientation);
    }

    private static EngineDefinition Definition(
        string familyId = "test-engine",
        double massKg = 1.0,
        double maximumForwardThrustN = 1_000.0,
        double reverseFraction = 0.4,
        double lateralFraction = 0.3,
        double liftFraction = 0.8,
        double rotationalTorqueNm = 2_000.0,
        int harmonyCount = 4,
        double minimumThrustFraction = 0.1,
        double minimumSpeedCeilingMps = 50.0,
        double maximumSpeedCeilingMps = 1_000.0)
        => new(
            familyId,
            "Test Engine",
            new DVec3(1.0, 1.0, 1.0),
            massKg,
            maximumForwardThrustN,
            reverseFraction,
            lateralFraction,
            liftFraction,
            rotationalTorqueNm,
            harmonyCount,
            minimumThrustFraction,
            minimumSpeedCeilingMps,
            maximumSpeedCeilingMps);

    private static string RepoRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null && !File.Exists(Path.Combine(directory, "Inferior.slnx")))
            directory = Directory.GetParent(directory)?.FullName;
        return directory ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
