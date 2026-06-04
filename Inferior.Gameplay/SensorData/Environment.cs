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

    // ── Stellar properties ────────────────────────────────────────────────────

    /// <summary>Core pressure in Pascals — used by Star Siphon depth mechanic.</summary>
    public static double NearestStarCorePressure
        => Galaxy.StarPhysics.CorePressure(NearestStar.Class, NearestStar.Mass);

    public static double NearestStarCoreTemperature
        => Galaxy.StarPhysics.CoreTemperature(NearestStar.Mass, (int)NearestStar.Class);
}
