namespace Inferior.Core.Math;

/// <summary>
/// Pure double-precision coordinate transforms for universe-scale positions.
/// </summary>
public static class CoordinateTransforms
{
    public static DVec3 EclipticToGalaxy(DVec3 position, double azimuth, double tilt)
    {
        double kx   = System.Math.Cos(azimuth);
        double kz   = System.Math.Sin(azimuth);
        double cosT = System.Math.Cos(tilt);
        double sinT = System.Math.Sin(tilt);
        double dot  = kx * position.X + kz * position.Z;

        return new DVec3(
            position.X * cosT - kz * position.Y * sinT + kx * dot * (1.0 - cosT),
            position.Y * cosT + (kz * position.X - kx * position.Z) * sinT,
            position.Z * cosT + kx * position.Y * sinT + kz * dot * (1.0 - cosT));
    }

    public static DVec3 GalaxyToEcliptic(DVec3 position, double azimuth, double tilt)
        => EclipticToGalaxy(position, azimuth, -tilt);
}
