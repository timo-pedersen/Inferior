namespace Inferior.Core.Math;

/// <summary>
/// Double precision 3D vector for universe-scale coordinates.
/// All positions, velocities and forces in simulation space use this type.
/// Cast to Microsoft.Xna.Framework.Vector3 only at render time, after
/// subtracting the camera/ship origin so the value is near zero.
/// </summary>
public readonly struct DVec3
{
    public readonly double X;
    public readonly double Y;
    public readonly double Z;

    public static readonly DVec3 Zero    = new(0, 0, 0);
    public static readonly DVec3 UnitX   = new(1, 0, 0);
    public static readonly DVec3 UnitY   = new(0, 1, 0);
    public static readonly DVec3 UnitZ   = new(0, 0, 1);

    public DVec3(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    // ── Arithmetic ────────────────────────────────────────────────────────────

    public static DVec3 operator +(DVec3 a, DVec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static DVec3 operator -(DVec3 a, DVec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static DVec3 operator -(DVec3 a)           => new(-a.X, -a.Y, -a.Z);
    public static DVec3 operator *(DVec3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    public static DVec3 operator *(double s, DVec3 a) => new(a.X * s, a.Y * s, a.Z * s);
    public static DVec3 operator /(DVec3 a, double s) => new(a.X / s, a.Y / s, a.Z / s);

    // ── Length & normalisation ────────────────────────────────────────────────

    public double LengthSquared => X * X + Y * Y + Z * Z;
    public double Length        => System.Math.Sqrt(LengthSquared);

    /// <summary>Returns a unit vector. Returns Zero if length is zero.</summary>
    public DVec3 Normalized()
    {
        double len = Length;
        return len > 0 ? this / len : Zero;
    }

    /// <summary>Static form — same as v.Normalized(). Matches XNA/Unity naming convention.</summary>
    public static DVec3 Normalize(DVec3 v) => v.Normalized();

    // ── Products ──────────────────────────────────────────────────────────────

    public static double Dot(DVec3 a, DVec3 b)
        => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    public static DVec3 Cross(DVec3 a, DVec3 b) => new(
        a.Y * b.Z - a.Z * b.Y,
        a.Z * b.X - a.X * b.Z,
        a.X * b.Y - a.Y * b.X);

    // ── Distance ──────────────────────────────────────────────────────────────

    public static double Distance(DVec3 a, DVec3 b)        => (a - b).Length;
    public static double DistanceSquared(DVec3 a, DVec3 b) => (a - b).LengthSquared;

    // ── Interpolation ─────────────────────────────────────────────────────────

    public static DVec3 Lerp(DVec3 a, DVec3 b, double t)
        => new(a.X + (b.X - a.X) * t,
               a.Y + (b.Y - a.Y) * t,
               a.Z + (b.Z - a.Z) * t);

    // ── Clamping ──────────────────────────────────────────────────────────────

    /// <summary>Clamps the vector's magnitude to maxLength.</summary>
    public DVec3 ClampMagnitude(double maxLength)
    {
        double lenSq = LengthSquared;
        if (lenSq <= maxLength * maxLength) return this;
        return Normalized() * maxLength;
    }

    // ── Conversion ────────────────────────────────────────────────────────────

    /// <summary>
    /// Convert to XNA/MonoGame Vector3 for rendering.
    /// ONLY call this after subtracting the camera universe origin,
    /// so the resulting floats are near zero and precision is safe.
    /// </summary>
    public Microsoft.Xna.Framework.Vector3 ToVector3()
        => new((float)X, (float)Y, (float)Z);

    /// <summary>
    /// Returns the render-space Vector3 relative to a given universe origin.
    /// This is the safe way to convert for drawing.
    /// </summary>
    public Microsoft.Xna.Framework.Vector3 ToRenderSpace(DVec3 cameraUniversePos)
        => (this - cameraUniversePos).ToVector3();

    // ── 2D helpers (galactic plane) ───────────────────────────────────────────

    /// <summary>DVec3 from a 2D galactic-plane point (Y = galactic up).</summary>
    public static DVec3 FromXZ(double x, double z) => new(x, 0, z);

    /// <summary>Horizontal distance ignoring Y (galactic plane distance).</summary>
    public double PlanarDistance(DVec3 other)
    {
        double dx = X - other.X;
        double dz = Z - other.Z;
        return System.Math.Sqrt(dx * dx + dz * dz);
    }

    // ── Equality ──────────────────────────────────────────────────────────────

    public bool Equals(DVec3 other)
        => X == other.X && Y == other.Y && Z == other.Z;

    public override bool Equals(object? obj)
        => obj is DVec3 other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(X, Y, Z);

    public static bool operator ==(DVec3 a, DVec3 b) => a.Equals(b);
    public static bool operator !=(DVec3 a, DVec3 b) => !a.Equals(b);

    // ── Formatting ────────────────────────────────────────────────────────────

    public override string ToString() => $"({X:F2}, {Y:F2}, {Z:F2})";

    /// <summary>Format with explicit precision, e.g. for debug display.</summary>
    public string ToString(int decimals)
    {
        string fmt = $"F{decimals}";
        return $"({X.ToString(fmt)}, {Y.ToString(fmt)}, {Z.ToString(fmt)})";
    }
}
