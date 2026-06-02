using Inferior.Core.Math;

namespace Inferior.Core.World;

/// <summary>
/// All live game objects in the current star system.
/// Stub — real implementation lives in Inferior.Gameplay.
/// The query interface here is the stable contract that Environment depends on.
/// </summary>
public class SimWorld
{
    // TODO: populated by Inferior.Gameplay with real orbital bodies each tick

    public CelestialBody NearestStar(DVec3 position)
        => _stubStar; // TODO: find actual nearest star from body list

    public CelestialBody NearestMassiveBody(DVec3 position)
        => _stubStar; // TODO: find nearest body by mass threshold

    public DVec3 GravityAt(DVec3 position)
        => DVec3.Zero; // TODO: sum gravitational acceleration from all bodies

    public DVec3 MagneticFieldAt(DVec3 position)
        => DVec3.Zero; // TODO: stellar magnetic field model

    public double RadiationAt(DVec3 position)
        => 0.0; // TODO: inverse-square stellar radiation flux

    // Stub sentinel — prevents null refs before real data is wired up
    private static readonly CelestialBody _stubStar = new()
    {
        Position       = DVec3.Zero,
        Radius         = 6.957e8,   // solar radius
        Mass           = 1.989e30,  // solar mass
        RotationPeriod = 2.192e6,   // ~25.4 days (sun)
        AxialTilt      = 0.1309,    // ~7.5°
    };
}
