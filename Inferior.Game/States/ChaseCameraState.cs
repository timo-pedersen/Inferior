using Inferior.Core.Math;
using Microsoft.Xna.Framework;

namespace Inferior.Game.States;

internal sealed class ChaseCameraState
{
    private static readonly DVec3 DefaultOffset = new(0.0, 30.0, 80.0);

    public const double MinimumRadiusMeters = 20.0;
    public const double MaximumRadiusMeters = 750.0;
    public const double OrbitAngularSpeedRadiansPerSecond = 1.2;
    public const double RadialSpeedMetersPerSecond = 120.0;
    public const double RollSpeedRadiansPerSecond = 1.2;

    private DVec3 _easedHullLocalOffset;
    private bool _easedOffsetValid;

    public DVec3 HullLocalDirection { get; private set; } = DefaultOffset.Normalized();
    public double Radius { get; private set; } = DefaultOffset.Length;
    public double RollRadians { get; private set; }

    public DVec3 DesiredHullLocalOffset => HullLocalDirection * Radius;

    public void ResetSmoothing() => _easedOffsetValid = false;

    public void ApplyEdit(ChaseCameraEditInput input, double dt)
    {
        if (input.Reset)
        {
            ResetPose();
            return;
        }

        ApplyOrbit(input.Horizontal, input.Vertical, dt);
        Radius = Math.Clamp(
            Radius + input.Radial * RadialSpeedMetersPerSecond * dt,
            MinimumRadiusMeters,
            MaximumRadiusMeters);
        RollRadians = WrapAngle(
            RollRadians + input.Roll * RollSpeedRadiansPerSecond * dt);
    }

    public void ResetPose()
    {
        HullLocalDirection = DefaultOffset.Normalized();
        Radius = DefaultOffset.Length;
        RollRadians = 0.0;
        ResetSmoothing();
    }

    public DVec3 ResolveWorldOffset(Quaternion shipOrientation)
    {
        DVec3 desiredOffset = DesiredHullLocalOffset;
        _easedHullLocalOffset = _easedOffsetValid
            ? DVec3.Lerp(_easedHullLocalOffset, desiredOffset, 0.08)
            : desiredOffset;
        _easedOffsetValid = true;
        return Transform(_easedHullLocalOffset, shipOrientation);
    }

    public Quaternion ResolveCameraOrientation(
        DVec3 worldOffset,
        DVec3 shipUp)
    {
        DVec3 forward = (-worldOffset).Normalized();
        Quaternion unrolled = SystemSpaceState.QuatLookAtWithUp(forward, shipUp);
        var forwardAxis = new Vector3(
            (float)forward.X,
            (float)forward.Y,
            (float)forward.Z);
        Quaternion roll = Quaternion.CreateFromAxisAngle(
            forwardAxis,
            (float)RollRadians);
        return Quaternion.Normalize(roll * unrolled);
    }

    internal static DVec3 Transform(DVec3 value, Quaternion orientation)
    {
        Vector3 transformed = Vector3.Transform(
            new Vector3((float)value.X, (float)value.Y, (float)value.Z),
            orientation);
        return new DVec3(transformed.X, transformed.Y, transformed.Z);
    }

    private void ApplyOrbit(double horizontal, double vertical, double dt)
    {
        if (dt <= 0.0 || (horizontal == 0.0 && vertical == 0.0))
            return;

        Quaternion orientation = ResolveCameraOrientation(
            HullLocalDirection,
            DVec3.UnitY);
        DVec3 screenRight = Transform(DVec3.UnitX, orientation);
        DVec3 screenUp = Transform(DVec3.UnitY, orientation);
        DVec3 tangent = screenRight * horizontal + screenUp * vertical;
        double tangentLength = tangent.Length;
        if (tangentLength < 1e-9)
            return;

        tangent /= tangentLength;
        double angle = OrbitAngularSpeedRadiansPerSecond * dt;
        HullLocalDirection = (HullLocalDirection * Math.Cos(angle)
                            + tangent * Math.Sin(angle)).Normalized();
    }

    private static double WrapAngle(double radians)
    {
        double wrapped = radians % (Math.PI * 2.0);
        if (wrapped > Math.PI)
            wrapped -= Math.PI * 2.0;
        else if (wrapped < -Math.PI)
            wrapped += Math.PI * 2.0;
        return wrapped;
    }
}

internal readonly record struct ChaseCameraEditInput(
    double Horizontal,
    double Vertical,
    double Roll,
    double Radial,
    bool Reset);
