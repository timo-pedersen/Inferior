using Inferior.Core.Math;
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
    /// Stub — driven by spectral class and distance as a rough proxy.
    /// </summary>
    public static double ExternalTemperature
    {
        get
        {
            double dist = DistanceToNearestStar;
            double starR = NearestStar.Radius;
            if (dist <= 0.0 || starR <= 0.0) return 2.7;

            // Approximate stellar surface temperature from spectral class
            double surfT = NearestStar.Class switch
            {
                Galaxy.SpectralClass.O           => 40_000.0,
                Galaxy.SpectralClass.B           => 20_000.0,
                Galaxy.SpectralClass.A           => 8_500.0,
                Galaxy.SpectralClass.F           => 6_800.0,
                Galaxy.SpectralClass.G           => 5_778.0,
                Galaxy.SpectralClass.K           => 4_500.0,
                Galaxy.SpectralClass.M           => 3_000.0,
                Galaxy.SpectralClass.WhiteDwarf  => 25_000.0,
                Galaxy.SpectralClass.NeutronStar => 1_000_000.0,
                _                                => 2.7,
            };

            // Stefan-Boltzmann: equilibrium T ∝ T_star × sqrt(R_star / 2d)
            double ratio = starR / dist;
            return Math.Max(2.7, surfT * Math.Sqrt(ratio * 0.5));
        }
    }

    // ── Stellar properties ────────────────────────────────────────────────────

    /// <summary>Core pressure in Pascals — used by Star Siphon depth mechanic.</summary>
    public static double NearestStarCorePressure
        => Galaxy.StarPhysics.CorePressure(NearestStar.Class, NearestStar.Mass);

    public static double NearestStarCoreTemperature
        => Galaxy.StarPhysics.CoreTemperature(NearestStar.Mass, (int)NearestStar.Class);
}
