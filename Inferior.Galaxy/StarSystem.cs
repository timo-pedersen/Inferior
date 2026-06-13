using Inferior.Core.Math;

namespace Inferior.Galaxy;

/// <summary>
/// A complete generated star system.
/// Generated deterministically from the star's seed.
/// All bodies orbit on rails — only their phase offsets distinguish them.
///
/// Generation is lazy: a system is only built when the player
/// enters or approaches it. The galaxy only stores Star stubs.
/// </summary>
public sealed class StarSystem
{
    public Star                    Star    { get; }
    public IReadOnlyList<OrbitalBody> Planets => _planets;
    public IReadOnlyList<OrbitalBody> AsteroidBelt => _asteroidBelt;

    // ── Ecliptic tilt (3D flight view only — system map is unaffected) ────────
    // Angle (radians) and azimuth of the axis around which the orbital plane is tilted
    // relative to the galaxy plane. Stored as primitives; the rendering layer converts
    // to a Matrix so Galaxy doesn't need a MonoGame dependency.
    public float EclipticTiltRadians        { get; private set; }
    public float EclipticTiltAzimuthRadians { get; private set; }

    private readonly List<OrbitalBody> _planets      = new();
    private readonly List<OrbitalBody> _asteroidBelt = new();

    // Used to collect all body positions in one pass for physics / rendering
    private readonly List<(OrbitalBody body, DVec3 pos)> _positionCache = new();

    public StarSystem(Star star) => Star = star;

    // ── Positions ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns positions of all bodies (star at origin, then all planets/moons)
    /// at the given game time. Result is a flat list suitable for gravity sampling.
    /// Re-uses an internal cache — do not store references across frames.
    /// </summary>
    public IReadOnlyList<(OrbitalBody body, DVec3 pos)> GetAllPositions(double gameTime)
    {
        _positionCache.Clear();

        // Star is always at system origin
        // Planets and their children collect recursively
        foreach (var planet in _planets)
            planet.CollectPositions(gameTime, DVec3.Zero, _positionCache);

        foreach (var asteroid in _asteroidBelt)
            asteroid.CollectPositions(gameTime, DVec3.Zero, _positionCache);

        return _positionCache;
    }

    // ── Factory ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Generate a complete star system from a seeded random.
    /// rng should be derived specifically for this star.
    /// </summary>
    public static StarSystem Generate(Star star, Core.Random.SeededRandom rng)
    {
        var system = new StarSystem(star);

        int planetCount = RollPlanetCount(star, rng);
        var orbits      = GenerateOrbits(star, planetCount, rng);

        for (int i = 0; i < orbits.Count; i++)
        {
            double orbitalRadius = orbits[i];
            string name   = $"{star.Name} {ToRoman(i + 1)}";
            var    planet = OrbitalBody.Generate(i, name, orbitalRadius, star.MassKg, rng.Derive(i));

            planet.ParentMassKg = star.MassKg;

            // Generate moons
            int moonCount = RollMoonCount(planet, rng.Derive(i, 99));
            for (int m = 0; m < moonCount; m++)
            {
                double moonRadius = rng.Derive(i, m).NextDouble(
                    planet.RadiusMeters * 2.5,
                    planet.HillSphereRadius * 0.4);

                string moonName = $"{name}{(char)('a' + m)}";
                var moon = OrbitalBody.Generate(
                    m, moonName, moonRadius, planet.MassKg, rng.Derive(i, m), isMoon: true);

                moon.ParentMassKg = planet.MassKg;
                planet.AddChild(moon);
            }

            system._planets.Add(planet);
        }

        // Asteroid belt — roughly between inner and outer planets if enough planets
        if (planetCount >= 3)
            GenerateAsteroidBelt(system, star, rng);

        // Random ecliptic tilt — each system's orbital plane is tilted independently
        var eclipticRng = rng.Derive(9876);
        system.EclipticTiltRadians        = (float)(eclipticRng.NextDouble(0.0, System.Math.PI / 5.0));
        system.EclipticTiltAzimuthRadians = (float)(eclipticRng.NextAngle());

        return system;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static int RollPlanetCount(Star star, Core.Random.SeededRandom rng)
    {
        return star.SpectralClass switch
        {
            SpectralClass.O or SpectralClass.B => rng.NextInt(0, 3),
            SpectralClass.NeutronStar          => rng.NextInt(0, 2),
            SpectralClass.BlackHole            => 0,
            SpectralClass.WhiteDwarf           => rng.NextInt(0, 2),
            _                                  => rng.NextInt(1, 9),
        };
    }

    private static List<double> GenerateOrbits(Star star, int count, Core.Random.SeededRandom rng)
    {
        // Titius-Bode inspired spacing: each orbit ~1.5-2.5x the previous
        var orbits = new List<double>(count);
        if (count == 0) return orbits;

        double current = Units.AU * rng.NextDouble(0.2, 0.6); // first planet distance

        for (int i = 0; i < count; i++)
        {
            orbits.Add(current);
            current *= rng.NextDouble(1.4, 2.8);
        }

        return orbits;
    }

    private static int RollMoonCount(OrbitalBody planet, Core.Random.SeededRandom rng)
    {
        return planet.BodyType switch
        {
            BodyType.GasGiant    => rng.NextInt(2, 8),
            BodyType.IceGiant    => rng.NextInt(1, 5),
            BodyType.EarthLike   => rng.NextInt(0, 2),
            BodyType.RockyPlanet => rng.NextBool(0.3) ? 1 : 0,
            BodyType.Desert      => rng.NextBool(0.2) ? 1 : 0,
            _                    => 0,
        };
    }

    private static void GenerateAsteroidBelt(StarSystem system, Star star, Core.Random.SeededRandom rng)
    {
        // Place belt between roughly the 2nd and 3rd planet
        int beltPlanetIndex = System.Math.Min(1, system._planets.Count - 1);
        double innerRadius  = system._planets[beltPlanetIndex].OrbitalRadius * 1.2;
        double outerRadius  = innerRadius * 1.8;

        int asteroidCount = rng.NextInt(30, 80);

        for (int i = 0; i < asteroidCount; i++)
        {
            double radius = rng.Derive(1000 + i).NextDouble(innerRadius, outerRadius);
            var asteroid  = OrbitalBody.Generate(
                i, $"{star.Name} belt-{i}", radius, star.MassKg,
                rng.Derive(2000 + i));

            // Override type to asteroid
            // (OrbitalBody is sealed/immutable so we need to construct directly)
            system._asteroidBelt.Add(new OrbitalBody
            {
                BodyIndex      = i,
                Name           = $"{star.Name} belt-{i}",
                BodyType       = BodyType.Asteroid,
                OrbitalRadius  = radius,
                Period         = Units.OrbitalPeriod(radius, star.MassKg),
                PhaseOffset    = rng.Derive(3000 + i).NextAngle(),
                MassKg         = rng.Derive(4000 + i).NextDouble(1e12, 1e20),
                RadiusMeters   = rng.Derive(5000 + i).NextDouble(500, 50_000),
                ParentMassKg   = star.MassKg,
            });
        }
    }

    private static string ToRoman(int n) => n switch
    {
        1 => "I", 2 => "II", 3 => "III", 4 => "IV", 5 => "V",
        6 => "VI", 7 => "VII", 8 => "VIII", 9 => "IX", 10 => "X",
        _ => n.ToString()
    };
}
