using Inferior.Core.Math;
using Inferior.Gameplay.Engines;

namespace Inferior.Gameplay.Ship;

public readonly record struct EngineTranslationCommand(
    double Longitudinal,
    double Lateral,
    double Vertical,
    bool UseLiftChannel)
{
    public static EngineTranslationCommand Clamp(
        double longitudinal,
        double lateral,
        double vertical,
        bool useLiftChannel)
        => new(
            Math.Clamp(longitudinal, -1.0, 1.0),
            Math.Clamp(lateral, -1.0, 1.0),
            Math.Clamp(vertical, -1.0, 1.0),
            useLiftChannel);
}

public readonly record struct EngineTranslationAllocation(
    EngineTranslationCommand Command,
    double Usage,
    DVec3 AllocatedAxes)
{
    public double Longitudinal => AllocatedAxes.Z;
    public double Lateral => AllocatedAxes.X;
    public double Vertical => AllocatedAxes.Y;
}

public sealed record ResolvedEnginePropulsion(
    string InstanceId,
    string FamilyId,
    EngineGeometryTransform GeometryTransform,
    EngineHarmonyOutput Harmony,
    double OperationalFactor,
    double ForwardEfficiency,
    double ManeuveringEfficiency);

public readonly record struct ShipPropulsionCapability(
    double CurrentMassKg,
    double HullMassKg,
    double ComponentMassKg,
    int InstalledEngineCount,
    int OperationalEngineCount,
    double InstalledEngineMassKg,
    DVec3 AvailableForwardForceShipLocalN,
    double AvailableReverseThrustN,
    double AvailableLateralThrustN,
    double AvailableLiftThrustN,
    double AvailableRotationalTorqueNm,
    double SpeedCeilingMps,
    IReadOnlyList<ResolvedEnginePropulsion> Engines);

public readonly record struct ShipPropulsionApplication(
    DVec3 AppliedForceShipLocalN,
    DVec3 ResultingAccelerationShipLocalMps2,
    EngineTranslationAllocation TranslationAllocation);

public sealed record EngineHarmonySnapshot(
    string InstanceId,
    string FamilyId,
    int SelectedHarmony,
    int HarmonyCount,
    double NormalizedPosition,
    double Curve,
    double ThrustMultiplier,
    double SpeedCeilingMps,
    double MaximumForwardThrustN,
    double MaximumReverseThrustN,
    double MaximumLateralThrustN,
    double MaximumLiftThrustN,
    double MaximumRotationalTorqueNm);

public sealed record ShipPropulsionSnapshot(
    double CurrentMassKg,
    double HullMassKg,
    double ComponentMassKg,
    int InstalledEngineCount,
    int OperationalEngineCount,
    double InstalledEngineMassKg,
    DVec3 AvailableForwardForceShipLocalN,
    double AvailableReverseThrustN,
    double AvailableLateralThrustN,
    double AvailableLiftThrustN,
    double AvailableRotationalTorqueNm,
    double SpeedCeilingMps,
    IReadOnlyList<EngineHarmonySnapshot> Engines,
    EngineTranslationAllocation TranslationAllocation,
    DVec3 AppliedForceShipLocalN,
    DVec3 ResultingAccelerationShipLocalMps2,
    double MaximumLiftAccelerationMps2,
    double MaximumHoverGravityG,
    double SafeLandingGravityG);

public static class ShipPropulsion
{
    public const double StandardGravityMps2 = 9.80665;
    public const double LandingReserveFactor = 1.25;

    private static readonly DVec3 EngineLocalForward = -DVec3.UnitZ;

    public static ShipPropulsionCapability Resolve(Ship ship)
    {
        ArgumentNullException.ThrowIfNull(ship);

        int installedCount = 0;
        int operationalCount = 0;
        double engineMassKg = 0.0;
        DVec3 forwardForce = DVec3.Zero;
        double reverseThrustN = 0.0;
        double lateralThrustN = 0.0;
        double liftThrustN = 0.0;
        double rotationalTorqueNm = 0.0;
        double speedCeilingMps = double.PositiveInfinity;
        var resolvedEngines = new List<ResolvedEnginePropulsion>();

        int configuredEngineCount = ship.EngineMounts.Count(mount => mount.InstalledEngine is not null);
        double forwardEfficiency = configuredEngineCount == 1 && ship.SingleEngineEfficiency is { } efficiency
            ? efficiency.Forward
            : 1.0;
        double maneuveringEfficiency = configuredEngineCount == 1 && ship.SingleEngineEfficiency is { } maneuverEfficiency
            ? maneuverEfficiency.Maneuvering
            : 1.0;
        double rotationEfficiency = configuredEngineCount == 1 && ship.SingleEngineEfficiency is { } rotationLayout
            ? rotationLayout.Rotation
            : 1.0;

        foreach (EngineMount mount in ship.EngineMounts)
        {
            EngineInstance? engine = mount.InstalledEngine;
            if (engine is null)
                continue;

            installedCount++;
            EngineDefinition definition = engine.Variant.Engine;
            engineMassKg += definition.DryMassKg;

            double operationalFactor = 1.0 - engine.DamageFraction;
            if (operationalFactor <= 0.0 || engine.GeometryTransform is null)
                continue;

            operationalCount++;
            EngineHarmonyOutput harmony = definition.ResolveHarmony(engine.SelectedHarmony);
            var resolved = new ResolvedEnginePropulsion(
                engine.InstanceId,
                definition.FamilyId,
                engine.GeometryTransform,
                harmony,
                operationalFactor,
                forwardEfficiency,
                maneuveringEfficiency);
            resolvedEngines.Add(resolved);

            forwardForce += engine.GeometryTransform.TransformDirection(EngineLocalForward)
                * (harmony.MaximumForwardThrustN * operationalFactor * forwardEfficiency);
            reverseThrustN += harmony.MaximumReverseThrustN * operationalFactor * forwardEfficiency;
            lateralThrustN += harmony.MaximumLateralThrustN * operationalFactor * maneuveringEfficiency;
            liftThrustN += harmony.MaximumLiftThrustN * operationalFactor * maneuveringEfficiency;
            rotationalTorqueNm += harmony.MaximumRotationalTorqueNm
                * operationalFactor
                * rotationEfficiency;
            speedCeilingMps = Math.Min(speedCeilingMps, harmony.SpeedCeilingMps);
        }

        return new ShipPropulsionCapability(
            ship.Mass,
            ship.HullMass,
            ship.ComponentMass,
            installedCount,
            operationalCount,
            engineMassKg,
            forwardForce,
            reverseThrustN,
            lateralThrustN,
            liftThrustN,
            rotationalTorqueNm,
            double.IsPositiveInfinity(speedCeilingMps) ? 0.0 : speedCeilingMps,
            Array.AsReadOnly(resolvedEngines.ToArray()));
    }

    public static EngineTranslationAllocation AllocateTranslation(
        EngineTranslationCommand command)
    {
        EngineTranslationCommand clamped = EngineTranslationCommand.Clamp(
            command.Longitudinal,
            command.Lateral,
            command.Vertical,
            command.UseLiftChannel);
        var axes = new DVec3(clamped.Lateral, clamped.Vertical, clamped.Longitudinal);
        double usage = axes.Length;
        DVec3 allocated = usage > 1.0 ? axes / usage : axes;
        return new EngineTranslationAllocation(clamped, usage, allocated);
    }

    public static DVec3 ResolveAppliedForce(
        ShipPropulsionCapability capability,
        EngineTranslationAllocation allocation,
        double longitudinalScale = 1.0)
    {
        if (!double.IsFinite(longitudinalScale) || longitudinalScale < 0.0)
            throw new ArgumentOutOfRangeException(nameof(longitudinalScale));

        DVec3 forceShipLocal = DVec3.Zero;
        foreach (ResolvedEnginePropulsion engine in capability.Engines)
        {
            EngineHarmonyOutput harmony = engine.Harmony;
            double longitudinalMaximum = allocation.Longitudinal >= 0.0
                ? harmony.MaximumForwardThrustN
                : harmony.MaximumReverseThrustN;
            double verticalMaximum = allocation.Command.UseLiftChannel && allocation.Vertical > 0.0
                ? harmony.MaximumLiftThrustN
                : harmony.MaximumLateralThrustN;

            var forceEngineLocal = new DVec3(
                allocation.Lateral * harmony.MaximumLateralThrustN * engine.ManeuveringEfficiency,
                allocation.Vertical * verticalMaximum * engine.ManeuveringEfficiency,
                -allocation.Longitudinal * longitudinalMaximum * engine.ForwardEfficiency * longitudinalScale);
            forceEngineLocal *= engine.OperationalFactor;
            forceShipLocal += engine.GeometryTransform.TransformDirection(forceEngineLocal);
        }
        return forceShipLocal;
    }

    public static ShipPropulsionApplication Apply(
        ShipPropulsionCapability capability,
        DVec3 appliedForceShipLocalN,
        EngineTranslationAllocation translationAllocation = default)
    {
        DVec3 acceleration = capability.CurrentMassKg > 0.0
            ? appliedForceShipLocalN / capability.CurrentMassKg
            : DVec3.Zero;
        return new ShipPropulsionApplication(
            appliedForceShipLocalN,
            acceleration,
            translationAllocation);
    }

    public static double MaximumHoverGravityG(ShipPropulsionCapability capability)
        => capability.CurrentMassKg > 0.0
            ? capability.AvailableLiftThrustN / capability.CurrentMassKg / StandardGravityMps2
            : 0.0;
}
