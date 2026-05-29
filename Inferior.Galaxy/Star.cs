using Microsoft.Xna.Framework;
using Inferior.Core.Math;

namespace Inferior.Galaxy;

// ── Spectral class ────────────────────────────────────────────────────────────

public enum SpectralClass
{
    O,  // Blue-white,  30 000–50 000 K, very rare, hostile
    B,  // Blue-white,  10 000–30 000 K, rare
    A,  // White,        7 500–10 000 K
    F,  // Yellow-white, 6 000–7 500 K
    G,  // Yellow,       5 200–6 000 K  (Sol is G2)
    K,  // Orange,       3 700–5 200 K
    M,  // Red,          2 400–3 700 K, most common
    // ── Stellar remnants (rare discoveries) ──
    NeutronStar,
    BlackHole,
    WhiteDwarf,
}

// ── Star data ─────────────────────────────────────────────────────────────────

/// <summary>
/// Immutable data describing a star.
/// Generated once from a seed and never changes for a given galaxy.
/// </summary>
public sealed class Star
{
    // Identity
    public int    GalaxyIndex  { get; init; }   // unique index in galaxy array
    public string Name         { get; init; } = "";
    public DVec3  GalacticPos  { get; init; }   // light-years from galaxy centre

    // Classification
    public SpectralClass SpectralClass { get; init; }

    // Physical
    public double MassKg       { get; init; }   // kg
    public double RadiusMeters { get; init; }   // m
    public double Luminosity   { get; init; }   // relative to Sol = 1.0
    public double Temperature  { get; init; }   // Kelvin

    // Derived ─────────────────────────────────────────────────────────────────

    /// <summary>Habitable zone inner edge in meters from star.</summary>
    public double HabitableZoneInner => Units.AU * 0.95 * System.Math.Sqrt(Luminosity);

    /// <summary>Habitable zone outer edge in meters from star.</summary>
    public double HabitableZoneOuter => Units.AU * 1.37 * System.Math.Sqrt(Luminosity);

    // ── Lighting profile ──────────────────────────────────────────────────────

    /// <summary>Directional light colour cast by this star on scene geometry.</summary>
    public Color LightColor => SpectralClass switch
    {
        SpectralClass.O          => new Color(180, 210, 255),
        SpectralClass.B          => new Color(200, 220, 255),
        SpectralClass.A          => new Color(240, 245, 255),
        SpectralClass.F          => new Color(255, 248, 230),
        SpectralClass.G          => new Color(255, 240, 200),
        SpectralClass.K          => new Color(255, 210, 140),
        SpectralClass.M          => new Color(255, 160,  80),
        SpectralClass.WhiteDwarf => new Color(220, 230, 255),
        SpectralClass.NeutronStar=> new Color(180, 220, 255),
        SpectralClass.BlackHole  => Color.Black,
        _                        => Color.White,
    };

    /// <summary>Ambient light intensity. O/B/Neutron = very dark shadows.</summary>
    public float AmbientIntensity => SpectralClass switch
    {
        SpectralClass.O           => 0.03f,
        SpectralClass.B           => 0.04f,
        SpectralClass.NeutronStar => 0.02f,
        SpectralClass.BlackHole   => 0.00f,
        SpectralClass.M           => 0.12f,
        _                         => 0.07f,
    };

    /// <summary>Glow/billboard tint seen in skybox and system view.</summary>
    public Color GlowColor => SpectralClass switch
    {
        SpectralClass.O           => new Color(140, 180, 255),
        SpectralClass.B           => new Color(160, 200, 255),
        SpectralClass.A           => new Color(220, 230, 255),
        SpectralClass.F           => new Color(255, 245, 210),
        SpectralClass.G           => new Color(255, 230, 150),
        SpectralClass.K           => new Color(255, 180, 80),
        SpectralClass.M           => new Color(255, 100, 40),
        SpectralClass.WhiteDwarf  => new Color(200, 220, 255),
        SpectralClass.NeutronStar => new Color(160, 200, 255),
        SpectralClass.BlackHole   => Color.Black,
        _                         => Color.White,
    };

    // ── Map display ───────────────────────────────────────────────────────────

    /// <summary>Colour used for this star's dot on the galaxy map.</summary>
    public Color MapColor => GlowColor;

    /// <summary>Relative size of map dot — brighter/hotter = larger.</summary>
    public float MapDotSize => SpectralClass switch
    {
        SpectralClass.O           => 3.5f,
        SpectralClass.B           => 3.0f,
        SpectralClass.A           => 2.5f,
        SpectralClass.F           => 2.0f,
        SpectralClass.G           => 2.0f,
        SpectralClass.K           => 1.8f,
        SpectralClass.M           => 1.5f,
        SpectralClass.NeutronStar => 1.2f,
        SpectralClass.WhiteDwarf  => 1.2f,
        SpectralClass.BlackHole   => 2.0f,  // visible as absence/ring
        _                         => 1.5f,
    };

    // ── Factory ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Generate a star from a seeded random.
    /// rng should already be derived for this star's index.
    /// </summary>
    public static Star Generate(
        int index, string name, DVec3 galacticPos,
        Core.Random.SeededRandom rng)
    {
        var spectralClass = RollSpectralClass(rng);
        var (mass, radius, luminosity, temp) = GetPhysicals(spectralClass, rng);

        return new Star
        {
            GalaxyIndex   = index,
            Name          = name,
            GalacticPos   = galacticPos,
            SpectralClass = spectralClass,
            MassKg        = mass,
            RadiusMeters  = radius,
            Luminosity    = luminosity,
            Temperature   = temp,
        };
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static SpectralClass RollSpectralClass(Core.Random.SeededRandom rng)
    {
        // Approximate real stellar distribution (IMF) with added rare types
        SpectralClass[] classes = [
            SpectralClass.M, SpectralClass.M, SpectralClass.M, SpectralClass.M,
            SpectralClass.M, SpectralClass.M, SpectralClass.M,   // 47%
            SpectralClass.K, SpectralClass.K, SpectralClass.K,   // 20% (boosted for gameplay)
            SpectralClass.G, SpectralClass.G, SpectralClass.G,   // 20% (boosted)
            SpectralClass.F, SpectralClass.F,                     // 7%
            SpectralClass.A,                                      // 3%
            SpectralClass.B,                                      // 1.5%
            SpectralClass.O,                                      // 0.5%
            SpectralClass.WhiteDwarf,                             // rare
            SpectralClass.NeutronStar,                            // very rare
            SpectralClass.BlackHole,                              // very rare
        ];

        double[] weights = [
            7, 7, 7, 7, 7, 7, 7,
            3, 3, 3,
            3, 3, 3,
            2, 2,
            1.5,
            0.8,
            0.3,
            0.8,
            0.4,
            0.2,
        ];

        return rng.WeightedPick(classes, weights);
    }

    private static (double mass, double radius, double luminosity, double temp)
        GetPhysicals(SpectralClass sc, Core.Random.SeededRandom rng)
    {
        // Returns (mass in solar masses, radius in solar radii, luminosity, temp K)
        // then converts to SI at the end
        (double massS, double radS, double lum, double temp) = sc switch
        {
            SpectralClass.O => (
                rng.NextDouble(20, 90),
                rng.NextDouble(7, 15),
                rng.NextDouble(30_000, 1_000_000),
                rng.NextDouble(30_000, 50_000)),
            SpectralClass.B => (
                rng.NextDouble(3, 20),
                rng.NextDouble(2, 7),
                rng.NextDouble(100, 30_000),
                rng.NextDouble(10_000, 30_000)),
            SpectralClass.A => (
                rng.NextDouble(1.5, 3),
                rng.NextDouble(1.4, 2),
                rng.NextDouble(6, 100),
                rng.NextDouble(7_500, 10_000)),
            SpectralClass.F => (
                rng.NextDouble(1.05, 1.5),
                rng.NextDouble(1.1, 1.4),
                rng.NextDouble(1.5, 6),
                rng.NextDouble(6_000, 7_500)),
            SpectralClass.G => (
                rng.NextDouble(0.8, 1.05),
                rng.NextDouble(0.85, 1.1),
                rng.NextDouble(0.5, 1.5),
                rng.NextDouble(5_200, 6_000)),
            SpectralClass.K => (
                rng.NextDouble(0.45, 0.8),
                rng.NextDouble(0.6, 0.85),
                rng.NextDouble(0.1, 0.5),
                rng.NextDouble(3_700, 5_200)),
            SpectralClass.M => (
                rng.NextDouble(0.08, 0.45),
                rng.NextDouble(0.1, 0.6),
                rng.NextDouble(0.001, 0.1),
                rng.NextDouble(2_400, 3_700)),
            SpectralClass.WhiteDwarf => (
                rng.NextDouble(0.5, 1.2),
                rng.NextDouble(0.008, 0.02),
                rng.NextDouble(0.0001, 0.01),
                rng.NextDouble(8_000, 40_000)),
            SpectralClass.NeutronStar => (
                rng.NextDouble(1.2, 2.2),
                0.00001,    // ~10km radius
                0.00001,
                rng.NextDouble(100_000, 1_000_000)),
            SpectralClass.BlackHole => (
                rng.NextDouble(3, 30),
                0.0,        // event horizon, not a physical surface
                0.0,
                0.0),
            _ => (1.0, 1.0, 1.0, 5_778),
        };

        return (
            massS  * Units.SolarMass,
            radS   * Units.SolarRadius,
            lum,
            temp);
    }
}
