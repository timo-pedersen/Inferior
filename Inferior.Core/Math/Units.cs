namespace Inferior.Core.Math;

/// <summary>
/// Physical constants and unit conversions.
/// All simulation values are in SI units (meters, seconds, kilograms).
/// Use these constants to convert when needed for display or generation.
/// </summary>
public static class Units
{
    // ── Gravitational constant ────────────────────────────────────────────────
    public const double G = 6.674e-11;          // m³ kg⁻¹ s⁻²

    // ── Distance ──────────────────────────────────────────────────────────────
    public const double AU         = 1.496e11;  // meters per astronomical unit
    public const double LightYear  = 9.461e15;  // meters per light year
    public const double LightSecond= 2.998e8;   // meters per light second
    public const double Parsec     = 3.086e16;  // meters per parsec
    public const double KM         = 1_000.0;   // meters per kilometer

    // ── Mass ──────────────────────────────────────────────────────────────────
    public const double SolarMass  = 1.989e30;  // kg
    public const double EarthMass  = 5.972e24;  // kg
    public const double MoonMass   = 7.342e22;  // kg

    // ── Radius ────────────────────────────────────────────────────────────────
    public const double SolarRadius = 6.957e8;  // m
    public const double EarthRadius = 6.371e6;  // m
    public const double MoonRadius  = 1.737e6;  // m

    // ── Time ──────────────────────────────────────────────────────────────────
    public const double MinuteInSeconds = 60.0;
    public const double HourInSeconds   = 3_600.0;
    public const double DayInSeconds    = 86_400.0;
    public const double YearInSeconds   = 3.156e7;  // Julian year

    // ── Speed ─────────────────────────────────────────────────────────────────
    public const double SpeedOfLight  = 2.998e8;   // m/s
    public const double SpeedOfSound  = 343.0;     // m/s (sea level Earth, reference)

    // ── Minimum simulation precision ──────────────────────────────────────────
    public const double MinimumDistance = 0.01;    // 1 cm, smallest meaningful unit

    // ── Conversions ───────────────────────────────────────────────────────────

    public static double MetersToAU(double meters)         => meters / AU;
    public static double AUToMeters(double au)             => au * AU;
    public static double MetersToLightYears(double meters) => meters / LightYear;
    public static double LightYearsToMeters(double ly)     => ly * LightYear;
    public static double MetersToKM(double meters)         => meters / KM;
    public static double KMToMeters(double km)             => km * KM;

    // ── Orbital mechanics ─────────────────────────────────────────────────────

    /// <summary>
    /// Orbital period from radius and parent mass (Kepler's third law).
    /// Returns period in seconds.
    /// </summary>
    public static double OrbitalPeriod(double orbitalRadiusMeters, double parentMassKg)
        => 2.0 * System.Math.PI *
           System.Math.Sqrt(
               System.Math.Pow(orbitalRadiusMeters, 3.0) / (G * parentMassKg));

    /// <summary>
    /// Orbital velocity for a circular orbit.
    /// Returns speed in m/s.
    /// </summary>
    public static double OrbitalVelocity(double orbitalRadiusMeters, double parentMassKg)
        => System.Math.Sqrt(G * parentMassKg / orbitalRadiusMeters);

    /// <summary>
    /// Hill sphere radius — gravitational dominance zone of a body
    /// relative to its parent. Approximate L1 distance.
    /// </summary>
    public static double HillSphereRadius(
        double orbitalRadius, double bodyMass, double parentMass)
        => orbitalRadius * System.Math.Pow(bodyMass / (3.0 * parentMass), 1.0 / 3.0);

    /// <summary>
    /// Surface gravity in m/s² from mass and radius.
    /// Earth = 9.81, Moon = 1.62.
    /// </summary>
    public static double SurfaceGravity(double massKg, double radiusMeters)
        => G * massKg / (radiusMeters * radiusMeters);

    // ── Display formatting ────────────────────────────────────────────────────

    /// <summary>Human-readable distance string, auto-selects unit.</summary>
    public static string FormatDistance(double meters)
    {
        if (meters < KM)            return $"{meters:F1} m";
        if (meters < AU * 0.1)      return $"{MetersToKM(meters):F1} km";
        if (meters < LightYear)     return $"{MetersToAU(meters):F2} AU";
        return                             $"{MetersToLightYears(meters):F2} ly";
    }

    /// <summary>Human-readable time string, auto-selects unit.</summary>
    public static string FormatTime(double seconds)
    {
        if (seconds < MinuteInSeconds) return $"{seconds:F1}s";
        if (seconds < HourInSeconds)   return $"{seconds / MinuteInSeconds:F1}m";
        if (seconds < DayInSeconds)    return $"{seconds / HourInSeconds:F1}h";
        if (seconds < YearInSeconds)   return $"{seconds / DayInSeconds:F1}d";
        return                                $"{seconds / YearInSeconds:F2}y";
    }

    /// <summary>Human-readable speed string.</summary>
    public static string FormatSpeed(double metersPerSecond)
    {
        double c = metersPerSecond / SpeedOfLight;
        if (c >= 0.01) return $"{c:F3}c";
        if (metersPerSecond >= KM) return $"{MetersToKM(metersPerSecond):F1} km/s";
        return $"{metersPerSecond:F1} m/s";
    }
}
