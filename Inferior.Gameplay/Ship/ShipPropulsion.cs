using Inferior.Core.Math;
using Inferior.Gameplay.Engines;

namespace Inferior.Gameplay.Ship;

public readonly record struct ShipPropulsionCapability(
    double CurrentMassKg,
    double HullMassKg,
    double ComponentMassKg,
    int InstalledEngineCount,
    int OperationalEngineCount,
    double InstalledEngineMassKg,
    DVec3 AvailableForwardForceShipLocalN,
    double AvailableManeuveringThrustN,
    double AvailableRotationalTorqueNm);

public readonly record struct ShipPropulsionApplication(
    DVec3 AppliedForceShipLocalN,
    DVec3 ResultingAccelerationShipLocalMps2);

public sealed record ShipPropulsionSnapshot(
    double CurrentMassKg,
    double HullMassKg,
    double ComponentMassKg,
    int InstalledEngineCount,
    int OperationalEngineCount,
    double InstalledEngineMassKg,
    DVec3 AvailableForwardForceShipLocalN,
    double AvailableManeuveringThrustN,
    double AvailableRotationalTorqueNm,
    DVec3 AppliedForceShipLocalN,
    DVec3 ResultingAccelerationShipLocalMps2);

public static class ShipPropulsion
{
    private static readonly DVec3 EngineLocalForward = -DVec3.UnitZ;

    public static ShipPropulsionCapability Resolve(Ship ship)
    {
        ArgumentNullException.ThrowIfNull(ship);

        int installedCount = 0;
        int operationalCount = 0;
        double engineMassKg = 0.0;
        DVec3 forwardForce = DVec3.Zero;
        double maneuveringThrustN = 0.0;
        double rotationalTorqueNm = 0.0;

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
            forwardForce += engine.GeometryTransform.TransformDirection(EngineLocalForward)
                * (definition.ForwardThrustN * operationalFactor);
            maneuveringThrustN += definition.ManeuveringThrustN * operationalFactor;
            rotationalTorqueNm += definition.RotationalTorqueNm * operationalFactor;
        }

        if (ship.SingleEngineEfficiency is { } efficiency && installedCount == 1)
        {
            forwardForce *= efficiency.Forward;
            maneuveringThrustN *= efficiency.Maneuvering;
            rotationalTorqueNm *= efficiency.Rotation;
        }

        return new ShipPropulsionCapability(
            ship.Mass,
            ship.HullMass,
            ship.ComponentMass,
            installedCount,
            operationalCount,
            engineMassKg,
            forwardForce,
            maneuveringThrustN,
            rotationalTorqueNm);
    }

    public static DVec3 ClampTranslationCommand(
        double forward,
        double lateral,
        double vertical)
    {
        var command = new DVec3(
            Math.Clamp(lateral, -1.0, 1.0),
            Math.Clamp(vertical, -1.0, 1.0),
            Math.Clamp(forward, -1.0, 1.0));
        double length = command.Length;
        return length > 1.0 ? command / length : command;
    }

    public static DVec3 ResolveAppliedForce(
        ShipPropulsionCapability capability,
        DVec3 command,
        double forwardScale = 1.0)
    {
        return capability.AvailableForwardForceShipLocalN * (command.Z * forwardScale)
            + DVec3.UnitX * (command.X * capability.AvailableManeuveringThrustN)
            + DVec3.UnitY * (command.Y * capability.AvailableManeuveringThrustN);
    }

    public static ShipPropulsionApplication Apply(
        ShipPropulsionCapability capability,
        DVec3 appliedForceShipLocalN)
    {
        DVec3 acceleration = capability.CurrentMassKg > 0.0
            ? appliedForceShipLocalN / capability.CurrentMassKg
            : DVec3.Zero;
        return new ShipPropulsionApplication(appliedForceShipLocalN, acceleration);
    }
}
