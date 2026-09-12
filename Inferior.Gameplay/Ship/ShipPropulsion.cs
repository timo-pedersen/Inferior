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

public readonly record struct ResolvedEnginePropulsion(
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

        double engineMassKg = 0.0;
        DVec3 forwardForce = DVec3.Zero;
        double reverseThrustN = 0.0;
        double lateralThrustN = 0.0;
        double liftThrustN = 0.0;
        double rotationalTorqueNm = 0.0;
        double speedCeilingMps = double.PositiveInfinity;

        // Indexed access rather than foreach/LINQ: EngineMounts is exposed as IReadOnlyList<T>,
        // and iterating an interface-typed sequence (via foreach or Enumerable.Count) boxes its
        // enumerator on every call. Indexing through the interface has no such cost.
        //
        // This pre-pass counts both configured (installed, regardless of damage/geometry — the
        // "single engine ship" designation below deliberately keys on this) and resolved (will
        // actually reach resolvedEngines below) engines, so resolvedEngines can be allocated once
        // at exactly the size it needs rather than an upper bound, an over-sized List, or a
        // separate ToArray() copy.
        IReadOnlyList<EngineMount> mounts = ship.EngineMounts;
        int configuredEngineCount = 0;
        int resolvedEngineCount = 0;
        for (int i = 0; i < mounts.Count; i++)
        {
            EngineInstance? mountedEngine = mounts[i].InstalledEngine;
            if (mountedEngine is null)
                continue;

            configuredEngineCount++;
            if (1.0 - mountedEngine.DamageFraction > 0.0 && mountedEngine.GeometryTransform is not null)
                resolvedEngineCount++;
        }

        ResolvedEnginePropulsion[] resolvedEngines = resolvedEngineCount == 0
            ? []
            : new ResolvedEnginePropulsion[resolvedEngineCount];
        int resolvedIndex = 0;
        double forwardEfficiency = configuredEngineCount == 1 && ship.SingleEngineEfficiency is { } efficiency
            ? efficiency.Forward
            : 1.0;
        double maneuveringEfficiency = configuredEngineCount == 1 && ship.SingleEngineEfficiency is { } maneuverEfficiency
            ? maneuverEfficiency.Maneuvering
            : 1.0;
        double rotationEfficiency = configuredEngineCount == 1 && ship.SingleEngineEfficiency is { } rotationLayout
            ? rotationLayout.Rotation
            : 1.0;

        for (int mountIndex = 0; mountIndex < mounts.Count; mountIndex++)
        {
            EngineMount mount = mounts[mountIndex];
            EngineInstance? engine = mount.InstalledEngine;
            if (engine is null)
                continue;

            EngineDefinition definition = engine.Variant.Engine;
            engineMassKg += definition.DryMassKg;

            double operationalFactor = 1.0 - engine.DamageFraction;
            if (operationalFactor <= 0.0 || engine.GeometryTransform is null)
                continue;

            EngineHarmonyOutput harmony = definition.ResolveHarmony(engine.SelectedHarmony);
            var resolved = new ResolvedEnginePropulsion(
                engine.InstanceId,
                definition.FamilyId,
                engine.GeometryTransform,
                harmony,
                operationalFactor,
                forwardEfficiency,
                maneuveringEfficiency);
            resolvedEngines[resolvedIndex++] = resolved;

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
            configuredEngineCount,
            resolvedEngineCount,
            engineMassKg,
            forwardForce,
            reverseThrustN,
            lateralThrustN,
            liftThrustN,
            rotationalTorqueNm,
            double.IsPositiveInfinity(speedCeilingMps) ? 0.0 : speedCeilingMps,
            // resolvedEngines is a T[], which already implements IReadOnlyList<T> — returned
            // directly rather than via Array.AsReadOnly (an extra wrapper allocation for no
            // extra safety here: it's a fresh array from this call, nothing else holds a
            // reference to it that a caller could reach and mutate).
            resolvedEngines);
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
        IReadOnlyList<ResolvedEnginePropulsion> engines = capability.Engines;
        for (int i = 0; i < engines.Count; i++)
        {
            ResolvedEnginePropulsion engine = engines[i];
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
