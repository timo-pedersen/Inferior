using Inferior.Core.Math;

namespace Inferior.Gameplay.Ship;

public readonly record struct RotationCommand(
    double Pitch,
    double Yaw,
    double Roll)
{
    public static RotationCommand Clamp(double pitch, double yaw, double roll)
        => new(
            Math.Clamp(pitch, -1.0, 1.0),
            Math.Clamp(yaw, -1.0, 1.0),
            Math.Clamp(roll, -1.0, 1.0));
}

public readonly record struct ShipRotationCapability(
    DVec3 ConfiguredDimensionsMeters,
    DVec3 AxisInertiaKgM2,
    double AvailableRotationalTorqueNm,
    DVec3 AvailableAngularAccelerationRadPerSec2);

public sealed record ShipRotationSnapshot(
    DVec3 ConfiguredDimensionsMeters,
    DVec3 AxisInertiaKgM2,
    double AvailableRotationalTorqueNm,
    DVec3 AvailableAngularAccelerationRadPerSec2,
    DVec3 AngularVelocityLocalRadPerSec,
    DVec3 TargetAngularVelocityLocalRadPerSec,
    bool FlightAssistOn);

public static class ShipRotation
{
    public static DVec3 CalculateBoxInertiaKgM2(double massKg, DVec3 dimensionsMeters)
    {
        ValidatePositiveFinite(massKg, nameof(massKg));
        ValidatePositiveFinite(dimensionsMeters.X, nameof(dimensionsMeters));
        ValidatePositiveFinite(dimensionsMeters.Y, nameof(dimensionsMeters));
        ValidatePositiveFinite(dimensionsMeters.Z, nameof(dimensionsMeters));

        double width = dimensionsMeters.X;
        double height = dimensionsMeters.Y;
        double length = dimensionsMeters.Z;
        var inertia = new DVec3(
            massKg * (height * height + length * length) / 12.0,
            massKg * (width * width + length * length) / 12.0,
            massKg * (width * width + height * height) / 12.0);
        ValidatePositiveFinite(inertia.X, nameof(inertia));
        ValidatePositiveFinite(inertia.Y, nameof(inertia));
        ValidatePositiveFinite(inertia.Z, nameof(inertia));
        return inertia;
    }

    public static ShipRotationCapability Resolve(
        double currentMassKg,
        ShipPresentationBounds configuredBounds,
        double availableRotationalTorqueNm)
    {
        if (!double.IsFinite(availableRotationalTorqueNm) || availableRotationalTorqueNm < 0.0)
            throw new ArgumentOutOfRangeException(nameof(availableRotationalTorqueNm));

        DVec3 dimensions = configuredBounds.Size;
        DVec3 inertia = CalculateBoxInertiaKgM2(currentMassKg, dimensions);
        DVec3 angularAcceleration = new(
            availableRotationalTorqueNm / inertia.X,
            availableRotationalTorqueNm / inertia.Y,
            availableRotationalTorqueNm / inertia.Z);
        return new ShipRotationCapability(
            dimensions,
            inertia,
            availableRotationalTorqueNm,
            angularAcceleration);
    }

    public static DVec3 ResolveTargetAngularVelocity(
        Ship ship,
        RotationCommand command)
    {
        ArgumentNullException.ThrowIfNull(ship);
        double pitchRate = command.Pitch >= 0.0
            ? ship.TurnRatePitchUp
            : ship.TurnRatePitchDown;
        return new DVec3(
            command.Pitch * pitchRate,
            command.Yaw * ship.TurnRateYaw,
            -command.Roll * ship.TurnRateRoll);
    }

    public static DVec3 MoveTowardsTarget(
        DVec3 current,
        DVec3 target,
        DVec3 angularAccelerationRadPerSec2,
        double dt)
    {
        if (!double.IsFinite(dt) || dt < 0.0)
            throw new ArgumentOutOfRangeException(nameof(dt));
        return new DVec3(
            MoveTowards(current.X, target.X, angularAccelerationRadPerSec2.X * dt),
            MoveTowards(current.Y, target.Y, angularAccelerationRadPerSec2.Y * dt),
            MoveTowards(current.Z, target.Z, angularAccelerationRadPerSec2.Z * dt));
    }

    public static double NormalizeLegacyMouseInput(double radiansPerTick, double maximumRate)
    {
        ValidatePositiveFinite(maximumRate, nameof(maximumRate));
        if (!double.IsFinite(radiansPerTick))
            throw new ArgumentOutOfRangeException(nameof(radiansPerTick));
        return Math.Clamp(
            radiansPerTick * FlightConstants.RotationInputReferenceHz / maximumRate,
            -1.0,
            1.0);
    }

    private static double MoveTowards(double current, double target, double maxDelta)
    {
        if (!double.IsFinite(current)
            || !double.IsFinite(target)
            || !double.IsFinite(maxDelta)
            || maxDelta < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDelta));
        }

        double delta = target - current;
        if (Math.Abs(delta) <= maxDelta)
            return target;
        return current + Math.CopySign(maxDelta, delta);
    }

    private static void ValidatePositiveFinite(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0.0)
            throw new ArgumentOutOfRangeException(name, "Value must be finite and positive.");
    }
}
