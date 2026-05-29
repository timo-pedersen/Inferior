using Inferior.Core.Math;

namespace Inferior.Galaxy;

// ── Body classification ───────────────────────────────────────────────────────

public enum BodyType
{
    RockyPlanet,
    IcePlanet,
    OceanPlanet,
    EarthLike,
    Desert,
    Volcanic,
    GasGiant,
    IceGiant,
    Moon,
    Asteroid,
    Station,
    Satellite,
}

public enum AtmosphereType
{
    None,
    Thin,
    Breathable,
    Thick,
    Toxic,
    Corrosive,
}

// ── Orbital body ──────────────────────────────────────────────────────────────

/// <summary>
/// A body that orbits another body on a fixed circular rail.
/// Position is fully determined by game time — no physics integration needed.
/// Only the ship and dynamic objects use Newtonian integration.
/// </summary>
public sealed class OrbitalBody
{
    // Identity
    public string   Name       { get; init; } = "";
    public BodyType BodyType   { get; init; }
    public int      BodyIndex  { get; init; } // index within parent system

    // Orbit definition — all in meters / seconds
    public double OrbitalRadius { get; init; }  // m from parent centre
    public double Period        { get; init; }  // seconds for one full orbit
    public double PhaseOffset   { get; init; }  // radians, randomised at generation

    // Axial tilt (visual only for now)
    public double AxialTilt     { get; init; }  // radians

    // Physical properties
    public double MassKg        { get; init; }
    public double RadiusMeters  { get; init; }
    public double SurfaceGravity => Units.SurfaceGravity(MassKg, RadiusMeters);

    // Atmosphere
    public AtmosphereType AtmosphereType { get; init; }
    public double         AtmosphereHeight { get; init; } // meters above surface
    public Microsoft.Xna.Framework.Color AtmosphereColor { get; init; }

    // Children (moons orbit planets, satellites orbit planets/moons)
    public IReadOnlyList<OrbitalBody> Children => _children;
    private readonly List<OrbitalBody> _children = new();

    public void AddChild(OrbitalBody child) => _children.Add(child);

    // Derived zones
    public double HillSphereRadius => OrbitalRadius > 0
        ? Units.HillSphereRadius(OrbitalRadius, MassKg, ParentMassKg)
        : double.MaxValue;

    /// <summary>Set by parent during system generation.</summary>
    public double ParentMassKg { get; set; }

    // ── Position on rails ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns this body's position in system space (metres from star)
    /// at a given game time. Purely deterministic — no state.
    /// </summary>
    public DVec3 GetPosition(double gameTimeSeconds, DVec3 parentPosition = default)
    {
        double angle = DMath.OrbitalAngle(gameTimeSeconds, Period, PhaseOffset);
        return DMath.CircularOrbitPosition(parentPosition, OrbitalRadius, angle);
    }

    /// <summary>
    /// Returns all children's positions recursively.
    /// Useful for gravity sampling — collect all bodies' positions in one pass.
    /// </summary>
    public void CollectPositions(
        double gameTime,
        DVec3  parentPos,
        List<(OrbitalBody body, DVec3 pos)> results)
    {
        DVec3 myPos = GetPosition(gameTime, parentPos);
        results.Add((this, myPos));

        foreach (var child in _children)
            child.CollectPositions(gameTime, myPos, results);
    }

    // ── Factory ───────────────────────────────────────────────────────────────

    public static OrbitalBody Generate(
        int index,
        string name,
        double orbitalRadius,
        double parentMassKg,
        Core.Random.SeededRandom rng,
        bool isMoon = false)
    {
        var bodyType   = RollBodyType(orbitalRadius, parentMassKg, rng, isMoon);
        var (mass, radius) = GetPhysicals(bodyType, rng);
        var atmosphere = GetAtmosphere(bodyType, rng);
        var period     = Units.OrbitalPeriod(orbitalRadius, parentMassKg);

        return new OrbitalBody
        {
            BodyIndex       = index,
            Name            = name,
            BodyType        = bodyType,
            OrbitalRadius   = orbitalRadius,
            Period          = period,
            PhaseOffset     = rng.NextAngle(),
            AxialTilt       = rng.NextGaussian(0, 0.3),
            MassKg          = mass,
            RadiusMeters    = radius,
            ParentMassKg    = parentMassKg,
            AtmosphereType  = atmosphere.type,
            AtmosphereHeight= atmosphere.height,
            AtmosphereColor = atmosphere.color,
        };
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static BodyType RollBodyType(
        double orbitalRadius, double parentMassKg, Core.Random.SeededRandom rng, bool isMoon)
    {
        if (isMoon) return rng.NextBool(0.7) ? BodyType.Moon : BodyType.RockyPlanet;

        // Rough zone classification from orbital radius
        // This is simplified — real generation would use star luminosity properly
        double au = Units.MetersToAU(orbitalRadius);

        if (au < 0.5) return rng.Pick(new[] { BodyType.Volcanic, BodyType.RockyPlanet, BodyType.Desert });
        if (au < 1.5) return rng.Pick(new[] { BodyType.EarthLike, BodyType.Desert, BodyType.RockyPlanet, BodyType.OceanPlanet });
        if (au < 4.0) return rng.Pick(new[] { BodyType.RockyPlanet, BodyType.IcePlanet, BodyType.Desert });
        if (au < 10.0) return rng.Pick(new[] { BodyType.GasGiant, BodyType.GasGiant, BodyType.IceGiant });
        return rng.Pick(new[] { BodyType.IceGiant, BodyType.IcePlanet, BodyType.GasGiant });
    }

    private static (double mass, double radius) GetPhysicals(BodyType type, Core.Random.SeededRandom rng)
    {
        return type switch
        {
            BodyType.GasGiant    => (rng.NextDouble(50, 500)  * Units.EarthMass, rng.NextDouble(8,  12) * Units.EarthRadius),
            BodyType.IceGiant    => (rng.NextDouble(10, 50)   * Units.EarthMass, rng.NextDouble(3,  6)  * Units.EarthRadius),
            BodyType.EarthLike   => (rng.NextDouble(0.5, 2.0) * Units.EarthMass, rng.NextDouble(0.8, 1.3) * Units.EarthRadius),
            BodyType.OceanPlanet => (rng.NextDouble(0.5, 3.0) * Units.EarthMass, rng.NextDouble(0.9, 1.5) * Units.EarthRadius),
            BodyType.Desert      => (rng.NextDouble(0.1, 1.5) * Units.EarthMass, rng.NextDouble(0.5, 1.2) * Units.EarthRadius),
            BodyType.Volcanic    => (rng.NextDouble(0.3, 2.0) * Units.EarthMass, rng.NextDouble(0.6, 1.1) * Units.EarthRadius),
            BodyType.RockyPlanet => (rng.NextDouble(0.05, 1.0)* Units.EarthMass, rng.NextDouble(0.3, 1.0) * Units.EarthRadius),
            BodyType.IcePlanet   => (rng.NextDouble(0.01, 0.5)* Units.EarthMass, rng.NextDouble(0.2, 0.8) * Units.EarthRadius),
            BodyType.Moon        => (rng.NextDouble(0.001, 0.1)*Units.EarthMass, rng.NextDouble(0.1, 0.5) * Units.EarthRadius),
            BodyType.Asteroid    => (rng.NextDouble(1e12, 1e20),                  rng.NextDouble(1e3, 5e5)),
            _                    => (Units.EarthMass, Units.EarthRadius),
        };
    }

    private static (AtmosphereType type, double height, Microsoft.Xna.Framework.Color color)
        GetAtmosphere(BodyType bodyType, Core.Random.SeededRandom rng)
    {
        return bodyType switch
        {
            BodyType.EarthLike => (
                AtmosphereType.Breathable,
                rng.NextDouble(60e3, 120e3),
                new Microsoft.Xna.Framework.Color(100, 150, 255)),

            BodyType.OceanPlanet => (
                AtmosphereType.Breathable,
                rng.NextDouble(80e3, 150e3),
                new Microsoft.Xna.Framework.Color(80, 140, 255)),

            BodyType.GasGiant => (
                AtmosphereType.Thick,
                rng.NextDouble(200e3, 500e3),
                new Microsoft.Xna.Framework.Color(
                    rng.NextInt(150, 255),
                    rng.NextInt(120, 200),
                    rng.NextInt(80, 180))),

            BodyType.IceGiant => (
                AtmosphereType.Thick,
                rng.NextDouble(100e3, 300e3),
                new Microsoft.Xna.Framework.Color(
                    rng.NextInt(80, 140),
                    rng.NextInt(160, 220),
                    rng.NextInt(200, 255))),

            BodyType.Volcanic => (
                AtmosphereType.Toxic,
                rng.NextDouble(30e3, 80e3),
                new Microsoft.Xna.Framework.Color(180, 80, 20)),

            BodyType.Desert => (
                rng.NextBool(0.5) ? AtmosphereType.Thin : AtmosphereType.None,
                rng.NextDouble(10e3, 40e3),
                new Microsoft.Xna.Framework.Color(210, 150, 100)),

            BodyType.IcePlanet => (
                rng.NextBool(0.3) ? AtmosphereType.Thin : AtmosphereType.None,
                rng.NextDouble(5e3, 20e3),
                new Microsoft.Xna.Framework.Color(200, 230, 255)),

            _ => (AtmosphereType.None, 0, Microsoft.Xna.Framework.Color.Transparent),
        };
    }
}
