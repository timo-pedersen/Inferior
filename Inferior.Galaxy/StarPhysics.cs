using Inferior.Core.Math;

namespace Inferior.Galaxy;

/// <summary>
/// Derived stellar properties — calculated from mass and class, never stored.
/// Used by Environment and the Star Siphon depth mechanic.
/// Stub — formulae to be filled in when stellar physics is implemented.
/// </summary>
public static class StarPhysics
{
    // ── Calibration ───────────────────────────────────────────────────────
    // Virial coefficient K: reproduces P_c☉ = 2.65 × 10¹⁶ Pa for the Sun.
    //   P_c = K_VIRIAL * G * M² / (4π * R⁴)
    private const double K_VIRIAL = 295.0;

    // Stretched-exponential profile fit to the standard solar model:
    //   P(r) = P_c * exp(−PROFILE_K * (r/R)^PROFILE_N)
    private const double PROFILE_K = 20.0;
    private const double PROFILE_N = 1.5;

    /// <summary>
    /// Approximates internal pressure at radial distance <paramref name="distanceFromCentre"/>
    /// inside a main-sequence star.  Accuracy is order-of-magnitude; intended for game use.
    ///
    /// Physical model:
    ///   R(M)   — empirical mass–radius law, exponent tuned per spectral class
    ///   P_c(M) — virial-theorem central pressure, calibrated to the solar model
    ///   P(r)   — stretched-exponential profile reproducing the solar pressure curve
    /// </summary>
    /// <param name="solarClass">Spectral class (governs mass–radius exponent)</param>
    /// <param name="solarMass">Stellar mass in solar masses (M☉)</param>
    /// <param name="distanceFromCentre">Radial distance from stellar centre (metres)</param>
    /// <returns>
    /// Pressure in Pascals.  Returns 0 for r ≥ stellar radius.
    /// </returns>
    public static double InnerStellarPressure(
        SpectralClass solarClass,
        double solarMass,
        double distanceFromCentre)
    {
        double massKg = solarMass * Units.SolarMass;
        double stellarRadius = StellarRadius(solarClass, solarMass);

        if (distanceFromCentre >= stellarRadius)
            return 0.0;

        // Central pressure (Pa) via calibrated virial theorem
        double centralPressure = K_VIRIAL * Units.G * (massKg * massKg)
                               / (4.0 * Math.PI * Math.Pow(stellarRadius, 4.0));

        // Normalised radius [0, 1)
        double u = distanceFromCentre / stellarRadius;

        // Steep core-peaked profile: matches the qualitative shape in your attached plot
        return centralPressure * Math.Exp(-PROFILE_K * Math.Pow(u, PROFILE_N));
    }


    /// <summary>
    /// R = R☉ × M^α.  The exponent α encodes structural differences:
    ///   small α → radius grows slowly with mass   (radiation-dominated, O/B)
    ///   large α → stronger coupling               (convective interior, M)
    /// </summary>
    public static double StellarRadius(SpectralClass solarClass, double solarMass)
    {
        double alpha = solarClass switch
        {
            SpectralClass.O => 0.57,  // radiation-dominated envelope
            SpectralClass.B => 0.70,
            SpectralClass.A => 0.80,
            SpectralClass.F => 0.80,
            SpectralClass.G => 0.80,  // calibration anchor: 1 M☉ → 1 R☉
            SpectralClass.K => 0.82,
            SpectralClass.M => 0.88,  // fully convective
            _ => 0.80
        };

        return Units.SolarRadius * Math.Pow(solarMass, alpha);
    }

    /// <summary>Approximate core pressure in Pascals.</summary>
    /// <param name="mass">Star mass in kg.</param>
    /// <param name="starClass">Spectral class index (0 = O, 6 = M).</param>
    public static double CorePressure(SpectralClass starClass, double mass)
        => InnerStellarPressure(starClass, mass, 0.0);

    /// <summary>Approximate core temperature in Kelvin.</summary>
    public static double CoreTemperature(double mass, int starClass)
        => 0.0; // TODO: stellar structure approximation
}
