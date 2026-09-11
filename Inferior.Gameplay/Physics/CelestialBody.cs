using Inferior.Core.Math;
using Inferior.Galaxy;

namespace Inferior.Gameplay.Physics;

/// <summary>
/// Runtime representation of a star, planet, or moon in the live simulation world.
/// Distinct from Galaxy.Star / Galaxy.OrbitalBody (generation-time data);
/// CelestialBody is the live sim-time object with current position and physics state.
/// </summary>
public class CelestialBody
{
    public static CelestialBody FromStar(Star star, DVec3 position)
    {
        ArgumentNullException.ThrowIfNull(star);
        return new CelestialBody
        {
            Position = position,
            Radius = star.RadiusMeters,
            Mass = star.MassKg,
            Class = star.SpectralClass,
            SurfaceTemperatureKelvin = star.Temperature,
        };
    }

    /// <summary>Current universe position in metres.</summary>
    public DVec3  Position       { get; set; }

    /// <summary>Physical radius in metres.</summary>
    public double Radius         { get; set; }

    /// <summary>Mass in kg.</summary>
    public double Mass           { get; set; }

    /// <summary>Spectral classification — used by StarPhysics for pressure/temperature calculations.</summary>
    public SpectralClass Class   { get; set; }

    /// <summary>Actual generated stellar surface temperature in kelvin; zero for non-stars.</summary>
    public double SurfaceTemperatureKelvin { get; set; }

    /// <summary>Axial tilt in radians — used by periodic noise sources.</summary>
    public double AxialTilt      { get; set; }

    /// <summary>Sidereal rotation period in seconds. Never zero — avoids divide-by-zero in Noise.Periodic.</summary>
    public double RotationPeriod { get; set; } = 1.0;
}
