using Inferior.Core.Math;

namespace Inferior.Galaxy;

// ── Station size classification ────────────────────────────────────────────────

public enum StationSize
{
    Small,
    Medium,
    Large,
}

// ── Services available at a station ───────────────────────────────────────────

[Flags]
public enum StationService
{
    None             = 0,
    Consumables      = 1 << 0,  // fuel, metal rods, coolant, ammunition
    Traders          = 1 << 1,  // cargo market
    Outfitters       = 1 << 2,  // components, hull elements
    MissionProviders = 1 << 3,  // mission board
    InfoBrokers      = 1 << 4,  // information market
    Repairs          = 1 << 5,  // hull and component repair
}

// ── Station ───────────────────────────────────────────────────────────────────

/// <summary>
/// A space station that orbits a planet, moon, or star.
/// Orbital position computed the same way as OrbitalBody — purely deterministic from game time.
/// </summary>
public sealed class Station
{
    // ── Identity ──────────────────────────────────────────────────────────────

    public string       Name     { get; init; } = "";
    public StationSize  Size     { get; init; }
    public StationService Services { get; init; }

    // ── Pads ──────────────────────────────────────────────────────────────────

    public int SmallPadCount { get; init; }
    public int LargePadCount { get; init; }

    public IReadOnlyList<LandingPad> LandingPads => _pads;
    private readonly List<LandingPad> _pads = new();

    // ── Orbit ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parent body this station orbits. Null means the station orbits the star directly.
    /// </summary>
    public OrbitalBody? OrbitParent { get; init; }

    public double OrbitalRadius { get; init; }  // m from parent centre
    public double Period        { get; init; }  // seconds for one full orbit
    public double PhaseOffset   { get; init; }  // radians, randomised at generation

    // ── Orientation ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fixed axial tilt in radians applied around local X. Randomised at generation.
    /// </summary>
    public float AxialTilt    { get; init; }

    /// <summary>
    /// Slow spin rate in radians/second around local Y.  Deterministic per station.
    /// Typical range: one full rotation every 5–24 hours.
    /// </summary>
    public float SlowRotation { get; init; }

    /// <summary>
    /// Returns the station's orientation quaternion at a given game time.
    /// The tilt is fixed; the spin accumulates over time.
    /// </summary>
    public System.Numerics.Quaternion GetOrientation(double gameTime)
    {
        float spin = (float)(gameTime * SlowRotation);
        var tilt   = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitX, AxialTilt);
        var rotate = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitY, spin);
        return System.Numerics.Quaternion.Normalize(rotate * tilt);
    }

    // ── Position

    /// <summary>
    /// Returns station position in system space (metres from star) at a given game time.
    /// parentPos is the parent body's position at that time. Pass DVec3.Zero if orbiting the star.
    /// Purely deterministic — no state.
    /// </summary>
    public DVec3 GetPosition(double gameTime, DVec3 parentPos)
    {
        double angle = DMath.OrbitalAngle(gameTime, Period, PhaseOffset);
        return DMath.CircularOrbitPosition(parentPos, OrbitalRadius, angle);
    }

    // ── Persistence stub ──────────────────────────────────────────────────────

    /// <summary>
    /// Stable identifier for persistence delta storage.
    /// Format: "{starName}:{orbitParentName}:{stationName}" — unique within a save.
    /// Null until persistence is implemented.
    /// </summary>
    public string? PersistenceId { get; init; }

    // ── Factory ───────────────────────────────────────────────────────────────

    private static readonly string[] NamePrefixes =
    [
        "Alpha", "Beta", "Gamma", "Delta", "Epsilon", "Zeta", "Eta", "Theta",
        "Sigma", "Omega", "Nova", "Sol", "Deep", "Far", "High", "Port",
        "Iron", "Grey", "Pale", "Dark", "Swift", "Still", "Last", "First",
    ];

    private static readonly string[] NameTypes =
    [
        "Station", "Outpost", "Platform", "Depot", "Terminal",
        "Base", "Hub", "Beacon", "Relay", "Waypoint", "Haven", "Anchorage",
    ];

    /// <summary>
    /// Generate a station orbiting the given parent body (or star if orbitParent is null).
    /// parentMassKg: mass of the body being orbited (used to compute orbital period).
    /// </summary>
    public static Station Generate(
        string           starName,
        OrbitalBody?     orbitParent,
        double           parentMassKg,
        Core.Random.SeededRandom rng)
    {
        var size = RollSize(rng);

        string prefix = rng.Pick(NamePrefixes);
        string type   = rng.Pick(NameTypes);
        string name   = $"{prefix} {type}";

        double orbitRadius = orbitParent != null
            ? rng.NextDouble(orbitParent.RadiusMeters * 3.0, orbitParent.RadiusMeters * 8.0)
            : rng.NextDouble(Units.AU * 0.5, Units.AU * 3.0);  // close stellar orbit

        double period = Units.OrbitalPeriod(orbitRadius, parentMassKg);

        var (smallPads, largePads) = RollPadCounts(size, rng);
        var services = RollServices(size, rng);

        // Axial tilt: ±15 degrees, randomised per station
        float axialTilt = (float)rng.NextDouble(-MathF.PI / 12.0, MathF.PI / 12.0);

        // Slow rotation: one revolution every 5–24 hours, sign randomised
        double hoursPerRev = rng.NextDouble(5.0, 24.0);
        float  slowRot     = (float)(MathF.Tau / (hoursPerRev * 3600.0))
                             * (rng.NextBool(0.5) ? 1f : -1f);

        var station = new Station
        {
            Name          = name,
            Size          = size,
            Services      = services,
            SmallPadCount = smallPads,
            LargePadCount = largePads,
            OrbitParent   = orbitParent,
            OrbitalRadius = orbitRadius,
            Period        = period,
            PhaseOffset   = rng.NextAngle(),
            AxialTilt     = axialTilt,
            SlowRotation  = slowRot,
            PersistenceId = $"{starName}:{orbitParent?.Name ?? "star"}:{name}",
        };

        // Generate landing pads
        for (int i = 0; i < smallPads; i++)
            station._pads.Add(new LandingPad { PadIndex = i, PadSize = PadSize.Small });
        for (int i = 0; i < largePads; i++)
            station._pads.Add(new LandingPad { PadIndex = smallPads + i, PadSize = PadSize.Large });

        return station;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static StationSize RollSize(Core.Random.SeededRandom rng)
    {
        double roll = rng.NextDouble(0, 1);
        return roll < 0.55 ? StationSize.Small
             : roll < 0.85 ? StationSize.Medium
             :               StationSize.Large;
    }

    private static (int small, int large) RollPadCounts(StationSize size, Core.Random.SeededRandom rng)
    {
        return size switch
        {
            StationSize.Small  => (rng.NextInt(2, 6),  0),
            StationSize.Medium => (rng.NextInt(3, 8),  rng.NextInt(1, 3)),
            StationSize.Large  => (rng.NextInt(4, 10), rng.NextInt(2, 6)),
            _                  => (2, 0),
        };
    }

    private static StationService RollServices(StationSize size, Core.Random.SeededRandom rng)
    {
        // All stations have basic consumables
        var services = StationService.Consumables;

        if (size >= StationSize.Small  && rng.NextBool(0.8)) services |= StationService.Repairs;
        if (size >= StationSize.Small  && rng.NextBool(0.6)) services |= StationService.Traders;
        if (size >= StationSize.Medium && rng.NextBool(0.7)) services |= StationService.Outfitters;
        if (size >= StationSize.Medium && rng.NextBool(0.5)) services |= StationService.MissionProviders;
        if (size >= StationSize.Large  && rng.NextBool(0.6)) services |= StationService.InfoBrokers;

        return services;
    }
}
