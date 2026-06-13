using Inferior.Core.Math;
using Inferior.Galaxy;
using Inferior.Gameplay.Physics;

namespace Inferior.Gameplay.SensorData;

/// <summary>
/// Static query class for world state relevant to sensors and noise sources.
/// Acts as the ship's local computer view of the surrounding environment.
///
/// The sim thread sets ShipPosition/ShipVelocity/World once per tick before
/// running sensors. All values are derived on-demand — nothing is cached here.
///
/// Sensors and noise functions call into Environment rather than holding direct
/// world references, keeping the sensor layer decoupled from world representation.
/// </summary>
public static class Environment
{
    // ── Updated by sim thread once per tick via UpdateFromSimThread() ────────────

    public static SimWorld World        { get; private set; } = new();
    public static DVec3    ShipPosition { get; private set; }
    public static DVec3    ShipVelocity { get; private set; }

    /// <summary>
    /// Single point of entry for sim-thread state updates.
    /// Private setters keep accidental writes from other callers impossible.
    /// </summary>
    public static void UpdateFromSimThread(SimWorld world, DVec3 shipPos, DVec3 shipVel)
    {
        World        = world;
        ShipPosition = shipPos;
        ShipVelocity = shipVel;
    }

    // ── Nearest star ──────────────────────────────────────────────────────────

    public static CelestialBody NearestStar          => World.NearestStar(ShipPosition);
    public static double        DistanceToNearestStar => (NearestStar.Position - ShipPosition).Length;
    public static double        NearestStarRadius     => NearestStar.Radius;

    /// <summary>Unit vector from ship toward nearest star.</summary>
    public static DVec3 DirectionToNearestStar
        => DVec3.Normalize(NearestStar.Position - ShipPosition);

    /// <summary>Angle in radians between ship forward and nearest star.</summary>
    public static double AngleToNearestStar(DVec3 shipForward)
        => System.Math.Acos(
               System.Math.Clamp(DVec3.Dot(shipForward, DirectionToNearestStar), -1.0, 1.0));

    public static double NearestStarAxialTilt      => NearestStar.AxialTilt;
    public static double NearestStarRotationPeriod => NearestStar.RotationPeriod;

    // ── Nearest planet / body ─────────────────────────────────────────────────

    public static CelestialBody NearestBody           => World.NearestMassiveBody(ShipPosition);
    public static double        DistanceToNearestBody => (NearestBody.Position - ShipPosition).Length;

    /// <summary>Distance from ship to the body's surface (negative if inside).</summary>
    public static double DistanceToSurface => DistanceToNearestBody - NearestBody.Radius;

    // ── Field vectors ─────────────────────────────────────────────────────────

    /// <summary>
    /// Net gravitational acceleration vector at ship position, in m/s².
    /// Computed directly via GravityCalculations — bypasses SimWorld to avoid circular deps.
    /// </summary>
    public static DVec3  GravitationalVector
        => GravityCalculations.GravityAt(ShipPosition, World.MassiveBodies);

    public static double GravitationalStrength => GravitationalVector.Length;

    /// <summary>Magnetic field at ship position — significant near neutron stars.</summary>
    public static DVec3  MagneticFieldVector   => World.MagneticFieldAt(ShipPosition);
    public static double MagneticFieldStrength => MagneticFieldVector.Length;

    /// <summary>Radiation flux in W/m² from all stellar sources.</summary>
    public static double RadiationFlux => World.RadiationAt(ShipPosition);

    /// <summary>
    /// External pressure at ship hull (Pa). ~0 in open space; significant inside atmospheres.
    /// Stub — will be driven by atmosphere layer once planetary entry is implemented.
    /// </summary>
    public static double ExternalPressure => 0.0;

    /// <summary>
    /// External temperature at ship hull (K). Approaches CMB (~2.7 K) in deep space;
    /// rises steeply near stellar photospheres and inside atmospheres.
    /// Driven by spectral class and distance as a rough proxy.
    /// </summary>
    public static double ExternalTemperature
    {
        get
        {
            double dist  = DistanceToNearestStar;
            double starR = NearestStar.Radius;
            if (dist <= 0.0 || starR <= 0.0) return 2.7;
            double surfT = StellarSurfaceTemperatureK(NearestStar.Class);
            // Stefan-Boltzmann equilibrium: T ∝ T_star × sqrt(R_star / 2d)
            return Math.Max(2.7, surfT * Math.Sqrt(starR / dist * 0.5));
        }
    }

    // ── Stellar properties ────────────────────────────────────────────────────

    /// <summary>Core pressure in Pascals — used by Star Siphon depth mechanic.</summary>
    public static double NearestStarCorePressure
        => Galaxy.StarPhysics.CorePressure(NearestStar.Class, NearestStar.Mass);

    public static double NearestStarCoreTemperature
        => Galaxy.StarPhysics.CoreTemperature(NearestStar.Mass, (int)NearestStar.Class);

    // ── Gravitationally dominant body ─────────────────────────────────────────

    /// <summary>
    /// The body whose gravitational acceleration at the ship position is strongest.
    /// Typically the star at interplanetary distances; a planet during close approach.
    /// Returns null only if no bodies have been populated yet.
    /// </summary>
    public static CelestialBody? GetGravitationallyDominantBody()
    {
        const double G = 6.674e-11;
        CelestialBody? best = null;
        double bestG = 0;
        foreach (var body in World.MassiveBodies)
        {
            double rSq = (body.Position - ShipPosition).LengthSquared;
            if (rSq < 1.0) rSq = 1.0;
            double g = G * body.Mass / rSq;
            if (g > bestG) { bestG = g; best = body; }
        }
        return best;
    }

    // ── Nearest orbital body (planets/moons with atmosphere data) ─────────────

    /// <summary>
    /// Nearest planet or moon with full generation data.
    /// Returns null if no orbital bodies have been populated yet.
    /// </summary>
    public static (OrbitalBody Body, DVec3 Position)? GetNearestBody()
    {
        var bodies = World.OrbitalBodies;
        if (bodies.Count == 0) return null;
        OrbitalBody? bestBody = null;
        DVec3        bestPos  = default;
        double       bestDSq  = double.MaxValue;
        foreach (var (b, p) in bodies)
        {
            double dSq = (p - ShipPosition).LengthSquared;
            if (dSq < bestDSq) { bestDSq = dSq; bestBody = b; bestPos = p; }
        }
        return bestBody == null ? null : (bestBody, bestPos);
    }

    // ── Body distance queries ─────────────────────────────────────────────────

    public static double DistanceFromCenter(OrbitalBody body, DVec3 bodyPos)
        => (bodyPos - ShipPosition).Length;

    /// <summary>Distance from ship to body surface in metres. Negative if inside the body.</summary>
    public static double DistanceFromSurface(OrbitalBody body, DVec3 bodyPos)
        => DistanceFromCenter(body, bodyPos) - body.RadiusMeters;

    // ── Atmosphere ────────────────────────────────────────────────────────────

    public static double AtmosphericHeight(OrbitalBody body) => body.AtmosphereHeight;

    /// <summary>Approximate surface pressure derived from atmosphere type.</summary>
    public static double AtmosphericSurfacePressure(OrbitalBody body) => body.AtmosphereType switch
    {
        AtmosphereType.Thin       =>   2_000.0,   // ~0.02 atm
        AtmosphereType.Breathable => 101_325.0,   // 1 atm
        AtmosphereType.Thick      => 400_000.0,   // ~4 atm
        AtmosphereType.Toxic      => 200_000.0,   // ~2 atm
        AtmosphereType.Corrosive  => 800_000.0,   // ~8 atm
        _                         =>       0.0,
    };

    /// <summary>
    /// Atmospheric pressure at the ship's current position relative to <paramref name="bodyPos"/>.
    /// Exponential falloff matching the visual shader: P = surfaceP * exp(-h / scaleHeight),
    /// where scaleHeight = AtmosphereHeight / 8 (mirrors Earth ~100 km / 8.5 km scale height).
    /// Clamped to surfaceP at or below the surface; effectively zero above AtmosphereHeight.
    /// </summary>
    public static double AtmosphericPressure(OrbitalBody body, DVec3 bodyPos)
    {
        double surfaceP = AtmosphericSurfacePressure(body);
        if (surfaceP <= 0) return 0.0;
        double h       = DistanceFromSurface(body, bodyPos);
        if (h <= 0) return surfaceP;
        double atmH    = body.AtmosphereHeight;
        if (atmH <= 0) return 0.0;
        double scaleH  = atmH / 8.0;
        return surfaceP * System.Math.Exp(-h / scaleH);
    }

    // ── Solar spectrum ────────────────────────────────────────────────────────

    /// <summary>
    /// Centre wavelengths of the 10 spectral bins, in nanometres.
    /// Range: 150 nm (far UV) → 2000 nm (near IR).
    /// </summary>
    public static readonly double[] SpectrumWavelengthsNm =
        [150, 250, 350, 450, 550, 650, 750, 900, 1200, 2000];

    public const int SpectrumBins = 10;

    // UV: bins 0-2 (150-350 nm); visible: 3-6 (450-750 nm); IR: 7-9 (900-2000 nm)
    private static readonly double[] _spectrumBandwidthNm =
        [100, 100, 100, 100, 100, 100, 100, 200, 400, 800];

    // Planck constants
    private const double C1 = 3.741771852e-16; // 2hc²  W·m²
    private const double C2 = 1.438776877e-2;  // hc/k  m·K

    /// <summary>
    /// Fill <paramref name="output"/> (must be ≥ SpectrumBins) with the normalised
    /// solar spectral irradiance at the ship's current position (0–1 per bin).
    /// No heap allocation — caller owns the buffer.
    /// </summary>
    public static void GetSolarVisibleSpectrum(Span<double> output)
    {
        if (output.Length < SpectrumBins) return;
        double T     = StellarSurfaceTemperatureK(NearestStar.Class);
        double scale = SolarInvSqScale();
        double max   = 0;
        for (int i = 0; i < SpectrumBins; i++)
        {
            double v   = Planck(SpectrumWavelengthsNm[i] * 1e-9, T) * scale;
            output[i]  = v;
            if (v > max) max = v;
        }
        if (max > 0)
            for (int i = 0; i < SpectrumBins; i++) output[i] /= max;
    }

    /// <summary>Total integrated solar heat flux at ship position (W/m² integrated across all bins).</summary>
    public static double GetSolarHeat()
    {
        double T = StellarSurfaceTemperatureK(NearestStar.Class);
        double s = SolarInvSqScale();
        double total = 0;
        for (int i = 0; i < SpectrumBins; i++)
            total += Planck(SpectrumWavelengthsNm[i] * 1e-9, T) * _spectrumBandwidthNm[i];
        return total * s;
    }

    /// <summary>UV component (bins 0-2, 150-350 nm) of solar flux at ship position.</summary>
    public static double GetSolarUV()
    {
        double T = StellarSurfaceTemperatureK(NearestStar.Class);
        double s = SolarInvSqScale();
        double total = 0;
        for (int i = 0; i < 3; i++)
            total += Planck(SpectrumWavelengthsNm[i] * 1e-9, T) * _spectrumBandwidthNm[i];
        return total * s;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static double SolarInvSqScale()
    {
        double d = DistanceToNearestStar, r = NearestStar.Radius;
        return (d > 0 && r > 0) ? (r / d) * (r / d) : 1.0;
    }

    private static double Planck(double lambdaM, double T)
    {
        if (T <= 0) return 0.0;
        double exp = C2 / (lambdaM * T);
        if (exp > 700) return 0.0;
        return C1 / (lambdaM * lambdaM * lambdaM * lambdaM * lambdaM * (Math.Exp(exp) - 1.0));
    }

    internal static double StellarSurfaceTemperatureK(SpectralClass sc) => sc switch
    {
        SpectralClass.O           =>  40_000.0,
        SpectralClass.B           =>  20_000.0,
        SpectralClass.A           =>   8_500.0,
        SpectralClass.F           =>   6_800.0,
        SpectralClass.G           =>   5_778.0,
        SpectralClass.K           =>   4_500.0,
        SpectralClass.M           =>   3_000.0,
        SpectralClass.WhiteDwarf  =>  25_000.0,
        SpectralClass.NeutronStar => 1_000_000.0,
        _                         =>   5_778.0,
    };
}
