using Microsoft.Xna.Framework;

namespace Inferior.Galaxy;

public sealed record PlanetData
{
    // Classification
    public required PlanetType                Type                 { get; init; }

    // Physical
    public required double                    Radius               { get; init; }  // metres
    public required double                    Mass                 { get; init; }  // kg
    public required double                    AxialTilt            { get; init; }  // radians
    public required Vector3                   PoleDirection        { get; init; }  // unit vector, Y-up world space
    public required double                    RotationPeriod       { get; init; }  // seconds; negative = retrograde
    public required double                    RotationEpoch        { get; init; }  // radians; rotation angle at simTime = 0

    // Derived physical
    public double SurfaceGravity =>
        6.674e-11 * Mass / (Radius * Radius);

    public double EscapeVelocity =>
        Math.Sqrt(2.0 * 6.674e-11 * Mass / Radius);

    // Atmosphere
    public required bool                      HasAtmosphere        { get; init; }
    public required double                    AtmosphereHeight     { get; init; }  // metres above surface; 0 if none
    public required double                    SurfacePressureBars  { get; init; }  // bar; 0 if none
    public required double                    PressureScaleHeight  { get; init; }  // metres; 0 if none
    public required AtmosphereCompositionType Composition          { get; init; }
    public required Color                     AtmosphereColor      { get; init; }
    public required double                    MaxWindSpeed         { get; init; }  // m/s; 0 if none

    // Temperature
    public required double                    AverageTemperature   { get; init; }  // kelvin

    // Surface
    public required bool                      HasLiquidSurface     { get; init; }

    // Seed for future layer system, heightmap, etc.
    public required ulong                     PlanetSeed           { get; init; }
}
