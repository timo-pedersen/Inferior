using Inferior.Core.Math;
using Xunit;

namespace Inferior.Game.Test;

public class EclipticTransformTests
{
    [Fact]
    public void EclipticToGalaxy_ZeroTilt_ReturnsInput()
    {
        var position = new DVec3(-12_345.25, 678.5, 9_876.75);

        var transformed = CoordinateTransforms.EclipticToGalaxy(
            position, azimuth: 1.23456789, tilt: 0.0);

        AssertClose(position, transformed, 0.0);
    }

    [Fact]
    public void EclipticToGalaxy_MatchesDoublePrecisionMatrixReference()
    {
        var position = new DVec3(-87_654_321.125, 12_345.5, 45_678_901.875);
        double azimuth = 0.713257191516459;
        double tilt = -0.428540289402008;

        var expected = MatrixReferenceEclipticToGalaxy(position, azimuth, tilt);
        var actual = CoordinateTransforms.EclipticToGalaxy(position, azimuth, tilt);

        AssertClose(expected, actual, 1e-8);
    }

    [Fact]
    public void EclipticToGalaxy_LargeCoordinates_AvoidsFloatAxisKilometreError()
    {
        var position = new DVec3(137_107_992_313.84682, -4_250.75, 126_134_719_066.21112);
        double azimuth = 0.013257191516459;
        double tilt = 0.428540289402008;

        var expected = MatrixReferenceEclipticToGalaxy(position, azimuth, tilt);
        var actual = CoordinateTransforms.EclipticToGalaxy(position, azimuth, tilt);
        var floatAxis = FloatAxisEclipticToGalaxy(position, azimuth, tilt);

        AssertClose(expected, actual, 0.001);
        Assert.True((floatAxis - expected).Length > 1_000.0,
            $"Float-axis transform should visibly diverge at universe scale; delta={(floatAxis - expected).Length:F6} m");
    }

    [Fact]
    public void GalaxyToEcliptic_InvertsEclipticToGalaxy()
    {
        var position = new DVec3(-91_000_000_000.25, 125_000.5, 77_000_000_000.75);
        double azimuth = 5.0271134;
        double tilt = 0.311702;

        var galaxy = CoordinateTransforms.EclipticToGalaxy(position, azimuth, tilt);
        var roundTrip = CoordinateTransforms.GalaxyToEcliptic(galaxy, azimuth, tilt);

        AssertClose(position, roundTrip, 0.001);
    }

    private static DVec3 MatrixReferenceEclipticToGalaxy(DVec3 position, double azimuth, double tilt)
    {
        double ux  = System.Math.Cos(azimuth);
        double uz  = System.Math.Sin(azimuth);
        double cos = System.Math.Cos(tilt);
        double sin = System.Math.Sin(tilt);
        double ic  = 1.0 - cos;

        double r00 = cos + ux * ux * ic;
        double r01 = -uz * sin;
        double r02 = ux * uz * ic;
        double r10 = uz * sin;
        double r11 = cos;
        double r12 = -ux * sin;
        double r20 = ux * uz * ic;
        double r21 = ux * sin;
        double r22 = cos + uz * uz * ic;

        return new DVec3(
            r00 * position.X + r01 * position.Y + r02 * position.Z,
            r10 * position.X + r11 * position.Y + r12 * position.Z,
            r20 * position.X + r21 * position.Y + r22 * position.Z);
    }

    private static DVec3 FloatAxisEclipticToGalaxy(DVec3 position, double azimuth, double tilt)
    {
        double ux  = MathF.Cos((float)azimuth);
        double uz  = MathF.Sin((float)azimuth);
        double cos = System.Math.Cos(tilt);
        double sin = System.Math.Sin(tilt);
        double ic  = 1.0 - cos;

        double r00 = cos + ux * ux * ic;
        double r01 = -uz * sin;
        double r02 = ux * uz * ic;
        double r10 = uz * sin;
        double r11 = cos;
        double r12 = -ux * sin;
        double r20 = ux * uz * ic;
        double r21 = ux * sin;
        double r22 = cos + uz * uz * ic;

        return new DVec3(
            r00 * position.X + r01 * position.Y + r02 * position.Z,
            r10 * position.X + r11 * position.Y + r12 * position.Z,
            r20 * position.X + r21 * position.Y + r22 * position.Z);
    }

    private static void AssertClose(DVec3 expected, DVec3 actual, double tolerance)
    {
        Assert.InRange(System.Math.Abs(actual.X - expected.X), 0.0, tolerance);
        Assert.InRange(System.Math.Abs(actual.Y - expected.Y), 0.0, tolerance);
        Assert.InRange(System.Math.Abs(actual.Z - expected.Z), 0.0, tolerance);
    }
}
