using Inferior.Core.Math;
using Inferior.Core.World;

namespace Inferior.Core.Simulation;

/// <summary>
/// Static query class for world state relevant to sensors and noise sources.
/// Acts as the ship's local computer view of the surrounding environment.
///
/// The sim thread sets ShipPosition/ShipVelocity/SimWorld once per tick before
/// running sensors. All values are derived — nothing is cached here.
///
/// Sensor and noise functions call into Environment rather than holding direct
/// world references, keeping the sensor layer decoupled from world representation.
/// </summary>
public static class Environment
{
    // ── Updated by sim thread once per tick ───────────────────────────────────

    internal static SimWorld  SimWorld        { get; set; } = new();
    internal static DVec3  ShipPosition { get; set; }
    internal static DVec3  ShipVelocity { get; set; }

    // ── Nearest star ──────────────────────────────────────────────────────────

    public static CelestialBody NearestStar
        => SimWorld.NearestStar(ShipPosition);

    public static double DistanceToNearestStar
        => (NearestStar.Position - ShipPosition).Length;

    public static double NearestStarRadius
        => NearestStar.Radius;

    /// <summary>Unit vector from ship toward nearest star.</summary>
    public static DVec3 DirectionToNearestStar
        => DVec3.Normalize(NearestStar.Position - ShipPosition);

    /// <summary>Angle in radians between ship forward and nearest star.</summary>
    public static double AngleToNearestStar(DVec3 shipForward)
        => System.Math.Acos(
               System.Math.Clamp(DVec3.Dot(shipForward, DirectionToNearestStar), -1.0, 1.0));

    public static double NearestStarAxialTilt     => NearestStar.AxialTilt;
    public static double NearestStarRotationPeriod => NearestStar.RotationPeriod;

    // ── Nearest planet / body ─────────────────────────────────────────────────

    public static CelestialBody NearestBody
        => SimWorld.NearestMassiveBody(ShipPosition);

    public static double DistanceToNearestBody
        => (NearestBody.Position - ShipPosition).Length;

    /// <summary>Distance from ship to the body's surface (can be negative if inside).</summary>
    public static double DistanceToSurface
        => DistanceToNearestBody - NearestBody.Radius;

    // ── Field vectors ─────────────────────────────────────────────────────────

    public static DVec3  GravitationalVector   => SimWorld.GravityAt(ShipPosition);
    public static double GravitationalStrength => GravitationalVector.Length;

    /// <summary>Magnetic field at ship position — significant near neutron stars.</summary>
    public static DVec3  MagneticFieldVector   => SimWorld.MagneticFieldAt(ShipPosition);
    public static double MagneticFieldStrength => MagneticFieldVector.Length;

    /// <summary>Radiation flux in W/m² from all stellar sources.</summary>
    public static double RadiationFlux => SimWorld.RadiationAt(ShipPosition);

    // ── Stellar properties ────────────────────────────────────────────────────

    /// <summary>Core pressure in Pascals — used by Star Siphon depth mechanic.</summary>
    public static double NearestStarCorePressure
        => StarPhysics.CorePressure(NearestStar.Mass, NearestStar.Class);

    /// <summary>Core temperature in Kelvin.</summary>
    public static double NearestStarCoreTemperature
        => StarPhysics.CoreTemperature(NearestStar.Mass, NearestStar.Class);
}
