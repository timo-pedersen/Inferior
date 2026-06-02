using Inferior.Core.Math;

namespace Inferior.Core.World;

/// <summary>
/// Runtime representation of a star, planet, or moon in the simulation world.
/// Stub — properties will be populated by Inferior.Gameplay when that project exists.
/// Distinct from Galaxy.Star / Galaxy.OrbitalBody (generation-time data);
/// CelestialBody is the live sim-time object with current position and physics state.
/// </summary>
public class CelestialBody
{
    /// <summary>Current universe position in metres.</summary>
    public DVec3  Position        { get; set; }

    /// <summary>Physical radius in metres.</summary>
    public double Radius          { get; set; }

    /// <summary>Mass in kg.</summary>
    public double Mass            { get; set; }

    /// <summary>Spectral class index (maps to SpectralClass enum once Gameplay layer exists).</summary>
    public int    Class           { get; set; }

    /// <summary>Axial tilt in radians — drives periodic noise sources.</summary>
    public double AxialTilt       { get; set; }

    /// <summary>Sidereal rotation period in seconds.</summary>
    public double RotationPeriod  { get; set; } = 1.0; // avoid divide-by-zero in Noise.Periodic
}
