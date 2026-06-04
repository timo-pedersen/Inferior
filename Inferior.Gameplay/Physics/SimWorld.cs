using Inferior.Core.Math;

namespace Inferior.Gameplay.Physics;

/// <summary>
/// All live game objects in the current star system.
/// Populated by the simulation each tick. Queried by Environment and sensors.
///
/// Gravity calculation lives in SensorData.GravityCalculations — SimWorld only
/// holds the data; it does not compute derived physics values itself.
/// </summary>
public class SimWorld
{
    /// <summary>All bodies large enough to exert meaningful gravity (stars, planets, moons).</summary>
    public List<CelestialBody> MassiveBodies { get; } = [];

    // ── Stubs — implementation follows from the physics simulation ────────────

    public CelestialBody NearestStar(DVec3 position)
    {
        // Stars are always far more massive than any planet — heaviest body is the star.
        CelestialBody? best = null;
        double bestMass = 0;
        foreach (var b in MassiveBodies)
        {
            if (b.Mass > bestMass) { bestMass = b.Mass; best = b; }
        }
        return best ?? _stubStar;
    }

    public CelestialBody NearestMassiveBody(DVec3 position)
    {
        CelestialBody? best = null;
        double bestDistSq = double.MaxValue;
        foreach (var b in MassiveBodies)
        {
            double dSq = (b.Position - position).LengthSquared;
            if (dSq < bestDistSq) { bestDistSq = dSq; best = b; }
        }
        return best ?? _stubStar;
    }

    public DVec3  MagneticFieldAt(DVec3 position) => DVec3.Zero; // TODO: stellar magnetic model
    public double RadiationAt(DVec3 position)     => 0.0;        // TODO: inverse-square stellar flux

    // ── Stub sentinel — prevents null refs before real data is wired ──────────
    private static readonly CelestialBody _stubStar = new()
    {
        Position       = DVec3.Zero,
        Radius         = 6.957e8,   // solar radius, m
        Mass           = 1.989e30,  // solar mass, kg
        RotationPeriod = 2.192e6,   // ~25.4 days (sun)
        AxialTilt      = 0.1309,    // ~7.5°
    };
}
