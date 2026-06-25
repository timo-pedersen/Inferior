using Microsoft.Xna.Framework;
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
/// A body that orbits another body. Planets use full Keplerian mechanics;
/// moons, asteroids, and stations use the legacy circular rail path.
/// Position is fully determined by game time — no physics integration needed.
/// </summary>
public sealed class OrbitalBody
{
    // Identity
    public string   Name       { get; init; } = "";
    public BodyType BodyType   { get; init; }
    public int      BodyIndex  { get; init; }

    // ── Keplerian orbital elements (set by PlanetFactory; SemiMajorAxis == 0 → circular rail) ──
    public double SemiMajorAxis     { get; init; }  // metres
    public double Eccentricity      { get; init; }  // 0 = circular, must be < 1
    public double Inclination       { get; init; }  // radians
    public double AscendingNode     { get; init; }  // radians, longitude of ascending node (Ω)
    public double PeriapsisArgument { get; init; }  // radians, argument of periapsis (ω)
    public double MeanAnomalyEpoch  { get; init; }  // radians, mean anomaly at simTime = 0

    // ── Planet data (set by PlanetFactory; null for moons, asteroids, stations) ──
    public PlanetData? Planet { get; init; }

    // ── Orientation (mutable — updated each sim tick for planet rotation; read by Brief B lat/lon) ──
    public Quaternion Orientation { get; set; } = Quaternion.Identity;

    // ── Legacy circular orbit fields (used by moons, asteroids, stations; set by OrbitalBody.Generate) ──
    public double OrbitalRadius { get; init; }
    public double Period        { get; init; }
    public double PhaseOffset   { get; init; }

    // Axial tilt (visual only)
    public double AxialTilt     { get; init; }

    // Physical properties
    public double MassKg        { get; init; }
    public double RadiusMeters  { get; init; }
    public double SurfaceGravity => Units.SurfaceGravity(MassKg, RadiusMeters);

    // Rotation
    public double RotationPeriod { get; init; }
    public DVec3  RotationAxis   { get; init; } = new(0, 1, 0);

    // Atmosphere (legacy fields — still set by PlanetFactory for backward compat)
    public AtmosphereType AtmosphereType   { get; init; }
    public double         AtmosphereHeight { get; init; }
    public Color          AtmosphereColor  { get; init; }

    /// <summary>Relative atmospheric density at surface (1.0 = Earth sea level). 0 for airless bodies.</summary>
    public double AtmosphereSurfaceDensity => AtmosphereType switch
    {
        AtmosphereType.Thin       => 0.1,
        AtmosphereType.Breathable => 1.0,
        AtmosphereType.Thick      => 4.0,
        AtmosphereType.Toxic      => 2.0,
        AtmosphereType.Corrosive  => 8.0,
        _                         => 0.0,
    };

    /// <summary>Altitude at which density falls to 1/e of surface value (metres).</summary>
    public double AtmosphereScaleHeight => AtmosphereHeight > 0 ? AtmosphereHeight / 8.0 : 0.0;

    /// <summary>Threshold altitude for entering/exiting atmosphere state.</summary>
    public double AtmosphereCeilingAltitude => Planet != null
        ? (Planet.HasAtmosphere ? Planet.AtmosphereHeight : 50_000.0)
        : (AtmosphereType != AtmosphereType.None ? AtmosphereHeight : 50_000.0);

    /// <summary>
    /// Relative atmospheric density at the given altitude above the surface.
    /// For PlanetData bodies, uses physical SurfacePressureBars and PressureScaleHeight.
    /// For legacy bodies, uses AtmosphereSurfaceDensity and AtmosphereScaleHeight.
    /// </summary>
    public double DensityAtAltitude(double altitudeAboveSurface)
    {
        if (Planet is { HasAtmosphere: true })
        {
            double surf = Planet.SurfacePressureBars;
            if (surf <= 0) return 0.0;
            if (altitudeAboveSurface <= 0) return surf;
            double scaleH = Planet.PressureScaleHeight;
            if (scaleH <= 0) return 0.0;
            return surf * System.Math.Exp(-altitudeAboveSurface / scaleH);
        }
        double surfDens = AtmosphereSurfaceDensity;
        if (surfDens <= 0) return 0.0;
        if (altitudeAboveSurface <= 0) return surfDens;
        double h = AtmosphereScaleHeight;
        if (h <= 0) return 0.0;
        return surfDens * System.Math.Exp(-altitudeAboveSurface / h);
    }

    // Children (moons orbit planets, satellites orbit planets/moons)
    public IReadOnlyList<OrbitalBody> Children => _children;
    private readonly List<OrbitalBody> _children = new();

    public void AddChild(OrbitalBody child) => _children.Add(child);

    // Derived zones
    public double HillSphereRadius => OrbitalRadius > 0
        ? Units.HillSphereRadius(OrbitalRadius, MassKg, ParentMassKg)
        : double.MaxValue;

    public double ParentMassKg { get; set; }

    // ── Position on rails ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns this body's position in system space at a given game time.
    /// For Keplerian bodies (SemiMajorAxis > 0), uses full orbital mechanics.
    /// For legacy circular bodies, uses the old rail calculation.
    /// </summary>
    public DVec3 GetPosition(double gameTimeSeconds, DVec3 parentPosition = default)
    {
        if (SemiMajorAxis > 0.0 && ParentMassKg > 0.0)
            return ComputePosition(gameTimeSeconds, Units.G * ParentMassKg, parentPosition);
        double angle = DMath.OrbitalAngle(gameTimeSeconds, Period, PhaseOffset);
        return DMath.CircularOrbitPosition(parentPosition, OrbitalRadius, angle);
    }

    /// <summary>
    /// Compute body position from Keplerian elements.
    /// starGM = G × starMassKg; starPosition = position of the parent star in world space.
    /// </summary>
    public DVec3 ComputePosition(double simTime, double starGM, DVec3 starPosition)
    {
        double a = SemiMajorAxis;
        double e = Eccentricity;

        double n = System.Math.Sqrt(starGM / (a * a * a));
        double M = ((MeanAnomalyEpoch + n * simTime) % (2.0 * System.Math.PI) + 2.0 * System.Math.PI) % (2.0 * System.Math.PI);

        // Newton's method — Kepler's equation M = E - e·sin(E)
        double E = M;
        for (int k = 0; k < 8; k++)
            E = E + (M - E + e * System.Math.Sin(E)) / (1.0 - e * System.Math.Cos(E));

        double cosE = System.Math.Cos(E);
        double sinV = System.Math.Sqrt(1.0 - e * e) * System.Math.Sin(E) / (1.0 - e * cosE);
        double cosV = (cosE - e) / (1.0 - e * cosE);
        double nu   = System.Math.Atan2(sinV, cosV);

        double r = a * (1.0 - e * cosE);

        // Orbital frame in Y-up coordinates (ecliptic = XZ plane)
        var ascNode     = new Vector3((float)System.Math.Cos(AscendingNode), 0f, (float)System.Math.Sin(AscendingNode));
        var orbNormal   = Vector3.Transform(Vector3.UnitY,
                              Quaternion.CreateFromAxisAngle(ascNode, (float)Inclination));
        var periapsisDir = Vector3.Transform(ascNode,
                              Quaternion.CreateFromAxisAngle(orbNormal, (float)PeriapsisArgument));
        var lateralDir  = Vector3.Cross(orbNormal, periapsisDir);

        double px = r * System.Math.Cos(nu);
        double py = r * System.Math.Sin(nu);

        return starPosition
            + new DVec3(periapsisDir.X, periapsisDir.Y, periapsisDir.Z) * px
            + new DVec3(lateralDir.X,  lateralDir.Y,  lateralDir.Z)  * py;
    }

    /// <summary>
    /// Compute body velocity from Keplerian elements (vis-viva decomposition).
    /// starGM = G × starMassKg.
    /// </summary>
    public DVec3 ComputeVelocity(double simTime, double starGM, DVec3 starPosition)
    {
        double a = SemiMajorAxis;
        double e = Eccentricity;

        double n = System.Math.Sqrt(starGM / (a * a * a));
        double M = ((MeanAnomalyEpoch + n * simTime) % (2.0 * System.Math.PI) + 2.0 * System.Math.PI) % (2.0 * System.Math.PI);

        double E = M;
        for (int k = 0; k < 8; k++)
            E = E + (M - E + e * System.Math.Sin(E)) / (1.0 - e * System.Math.Cos(E));

        double cosE = System.Math.Cos(E);
        double sinV = System.Math.Sqrt(1.0 - e * e) * System.Math.Sin(E) / (1.0 - e * cosE);
        double cosV = (cosE - e) / (1.0 - e * cosE);
        double nu   = System.Math.Atan2(sinV, cosV);

        var ascNode     = new Vector3((float)System.Math.Cos(AscendingNode), 0f, (float)System.Math.Sin(AscendingNode));
        var orbNormal   = Vector3.Transform(Vector3.UnitY,
                              Quaternion.CreateFromAxisAngle(ascNode, (float)Inclination));
        var periapsisDir = Vector3.Transform(ascNode,
                              Quaternion.CreateFromAxisAngle(orbNormal, (float)PeriapsisArgument));
        var lateralDir  = Vector3.Cross(orbNormal, periapsisDir);

        var pDir = new DVec3(periapsisDir.X, periapsisDir.Y, periapsisDir.Z);
        var lDir = new DVec3(lateralDir.X,   lateralDir.Y,  lateralDir.Z);

        // Radial = toward body from star; transverse = 90° ahead in orbit
        DVec3 radialDir     =  pDir * System.Math.Cos(nu) + lDir * System.Math.Sin(nu);
        DVec3 transverseDir = -pDir * System.Math.Sin(nu) + lDir * System.Math.Cos(nu);

        // Vis-viva decomposition
        double h  = System.Math.Sqrt(starGM * a * (1.0 - e * e));
        double vr = (starGM / h) * e * System.Math.Sin(nu);
        double vt = (starGM / h) * (1.0 + e * System.Math.Cos(nu));

        return radialDir * vr + transverseDir * vt;
    }

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

    // ── Factory (moons, asteroids; planets use PlanetFactory) ────────────────

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

        double rotPeriod = bodyType == BodyType.Moon
            ? period
            : RollRotationPeriod(bodyType, rng);

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
            RotationPeriod  = rotPeriod,
            RotationAxis    = new DVec3(0, 1, 0),
        };
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static double RollRotationPeriod(BodyType type, Core.Random.SeededRandom rng) => type switch
    {
        BodyType.GasGiant    => rng.NextDouble(36_000,    72_000),
        BodyType.IceGiant    => rng.NextDouble(50_000,   100_000),
        BodyType.EarthLike   => rng.NextDouble(72_000,   172_800),
        BodyType.OceanPlanet => rng.NextDouble(72_000,   172_800),
        BodyType.Desert      => rng.NextDouble(86_400,   259_200),
        BodyType.Volcanic    => rng.NextDouble(720_000, 1_800_000),
        BodyType.RockyPlanet => rng.NextDouble(86_400,   432_000),
        BodyType.IcePlanet   => rng.NextDouble(86_400,   360_000),
        _                    => 86_400,
    };

    private static BodyType RollBodyType(
        double orbitalRadius, double parentMassKg, Core.Random.SeededRandom rng, bool isMoon)
    {
        if (isMoon) return rng.NextBool(0.7) ? BodyType.Moon : BodyType.RockyPlanet;

        double au = Units.MetersToAU(orbitalRadius);
        if (au < 0.5)  return rng.Pick(new[] { BodyType.Volcanic, BodyType.RockyPlanet, BodyType.Desert });
        if (au < 1.5)  return rng.Pick(new[] { BodyType.EarthLike, BodyType.Desert, BodyType.RockyPlanet, BodyType.OceanPlanet });
        if (au < 4.0)  return rng.Pick(new[] { BodyType.RockyPlanet, BodyType.IcePlanet, BodyType.Desert });
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

    private static (AtmosphereType type, double height, Color color)
        GetAtmosphere(BodyType bodyType, Core.Random.SeededRandom rng)
    {
        return bodyType switch
        {
            BodyType.EarthLike => (
                AtmosphereType.Breathable,
                rng.NextDouble(60e3, 120e3),
                new Color(100, 150, 255)),

            BodyType.OceanPlanet => (
                AtmosphereType.Breathable,
                rng.NextDouble(80e3, 150e3),
                new Color(80, 140, 255)),

            BodyType.GasGiant => (
                AtmosphereType.Thick,
                rng.NextDouble(200e3, 500e3),
                new Color(
                    rng.NextInt(150, 255),
                    rng.NextInt(120, 200),
                    rng.NextInt(80, 180))),

            BodyType.IceGiant => (
                AtmosphereType.Thick,
                rng.NextDouble(100e3, 300e3),
                new Color(
                    rng.NextInt(80, 140),
                    rng.NextInt(160, 220),
                    rng.NextInt(200, 255))),

            BodyType.Volcanic => (
                AtmosphereType.Toxic,
                rng.NextDouble(30e3, 80e3),
                new Color(180, 80, 20)),

            BodyType.Desert => (
                rng.NextBool(0.5) ? AtmosphereType.Thin : AtmosphereType.None,
                rng.NextDouble(10e3, 40e3),
                new Color(210, 150, 100)),

            BodyType.IcePlanet => (
                rng.NextBool(0.3) ? AtmosphereType.Thin : AtmosphereType.None,
                rng.NextDouble(5e3, 20e3),
                new Color(200, 230, 255)),

            _ => (AtmosphereType.None, 0, Color.Transparent),
        };
    }
}
