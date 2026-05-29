namespace Inferior.Core.Math;

/// <summary>
/// Double precision math utilities to complement DVec3.
/// Mirrors commonly used float helpers from MathHelper but in double.
/// </summary>
public static class DMath
{
    public const double Pi      = System.Math.PI;
    public const double TwoPi   = System.Math.PI * 2.0;
    public const double HalfPi  = System.Math.PI * 0.5;
    public const double Epsilon  = 1e-10;

    // ── Basic ─────────────────────────────────────────────────────────────────

    public static double Clamp(double value, double min, double max)
        => value < min ? min : value > max ? max : value;

    public static double Clamp01(double value) => Clamp(value, 0.0, 1.0);

    public static double Lerp(double a, double b, double t) => a + (b - a) * t;

    public static double InverseLerp(double a, double b, double value)
        => System.Math.Abs(b - a) < Epsilon ? 0.0 : (value - a) / (b - a);

    /// <summary>Remap value from [inMin,inMax] to [outMin,outMax].</summary>
    public static double Remap(double value,
        double inMin, double inMax, double outMin, double outMax)
        => Lerp(outMin, outMax, InverseLerp(inMin, inMax, value));

    public static double Abs(double value)   => System.Math.Abs(value);
    public static double Sign(double value)  => System.Math.Sign(value);
    public static double Sqrt(double value)  => System.Math.Sqrt(value);
    public static double Pow(double b, double e) => System.Math.Pow(b, e);

    // ── Angles ────────────────────────────────────────────────────────────────

    public static double ToRadians(double degrees) => degrees * Pi / 180.0;
    public static double ToDegrees(double radians) => radians * 180.0 / Pi;

    /// <summary>Wrap angle to [0, 2π).</summary>
    public static double WrapAngle(double radians)
    {
        radians %= TwoPi;
        return radians < 0 ? radians + TwoPi : radians;
    }

    /// <summary>Shortest angular difference from a to b, signed, in radians.</summary>
    public static double AngleDelta(double from, double to)
    {
        double delta = WrapAngle(to - from);
        return delta > Pi ? delta - TwoPi : delta;
    }

    // ── Smooth curves ─────────────────────────────────────────────────────────

    public static double SmoothStep(double edge0, double edge1, double x)
    {
        double t = Clamp01((x - edge0) / (edge1 - edge0));
        return t * t * (3.0 - 2.0 * t);
    }

    public static double SmootherStep(double edge0, double edge1, double x)
    {
        double t = Clamp01((x - edge0) / (edge1 - edge0));
        return t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
    }

    /// <summary>Exponential ease — good for drag/approach curves.</summary>
    public static double ExpDecay(double current, double target, double decay, double dt)
        => target + (current - target) * System.Math.Exp(-decay * dt);

    // ── Orbital helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Position on a circular orbit given angle.
    /// Orbit is in the XZ plane (Y is galactic up).
    /// </summary>
    public static DVec3 CircularOrbitPosition(DVec3 center, double radius, double angle)
        => center + new DVec3(
            System.Math.Cos(angle) * radius,
            0,
            System.Math.Sin(angle) * radius);

    /// <summary>
    /// Orbital angle at a given game time.
    /// </summary>
    public static double OrbitalAngle(double gameTimeSeconds, double periodSeconds, double phaseOffset)
        => WrapAngle((gameTimeSeconds / periodSeconds) * TwoPi + phaseOffset);

    // ── Gravity ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Gravitational acceleration vector toward a massive body.
    /// Returns m/s² direction × magnitude.
    /// </summary>
    public static DVec3 GravityAcceleration(DVec3 shipPos, DVec3 bodyPos, double bodyMassKg)
    {
        DVec3  delta   = bodyPos - shipPos;
        double distSq  = delta.LengthSquared;

        if (distSq < 1.0) return DVec3.Zero; // inside body, avoid singularity

        double accel   = Units.G * bodyMassKg / distSq;
        double dist    = System.Math.Sqrt(distSq);
        return (delta / dist) * accel;
    }

    // ── Coordinate helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Convert galaxy-space (light-years) position to render-space
    /// for the galaxy map. Returns a normalised [-1,1] position
    /// suitable for scaling to screen.
    /// </summary>
    public static (float x, float y) GalaxyToScreen(
        DVec3 starPos, DVec3 cameraPos, double metersPerPixel, float screenCX, float screenCY)
    {
        DVec3 rel = starPos - cameraPos;
        return (
            screenCX + (float)(rel.X / metersPerPixel),
            screenCY + (float)(rel.Z / metersPerPixel)   // Z is the horizontal axis on map
        );
    }
}
