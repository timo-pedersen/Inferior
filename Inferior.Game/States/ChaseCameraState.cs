using Inferior.Core.Math;
using Microsoft.Xna.Framework;

namespace Inferior.Game.States;

internal sealed class ChaseCameraState
{
    private static readonly DVec3 DefaultOffset = new(0.0, 30.0, 80.0);

    private DVec3 _easedHullLocalOffset;
    private bool _easedOffsetValid;

    public DVec3 HullLocalDirection { get; private set; } = DefaultOffset.Normalized();
    public double Radius { get; private set; } = DefaultOffset.Length;
    public double RollRadians { get; private set; }

    public DVec3 DesiredHullLocalOffset => HullLocalDirection * Radius;

    public void ResetSmoothing() => _easedOffsetValid = false;

    public DVec3 ResolveWorldOffset(Quaternion shipOrientation)
    {
        DVec3 desiredOffset = DesiredHullLocalOffset;
        _easedHullLocalOffset = _easedOffsetValid
            ? DVec3.Lerp(_easedHullLocalOffset, desiredOffset, 0.08)
            : desiredOffset;
        _easedOffsetValid = true;
        return Transform(_easedHullLocalOffset, shipOrientation);
    }

    internal static DVec3 Transform(DVec3 value, Quaternion orientation)
    {
        Vector3 transformed = Vector3.Transform(
            new Vector3((float)value.X, (float)value.Y, (float)value.Z),
            orientation);
        return new DVec3(transformed.X, transformed.Y, transformed.Z);
    }
}
