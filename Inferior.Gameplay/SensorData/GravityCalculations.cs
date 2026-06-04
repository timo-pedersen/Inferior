using Inferior.Core.Math;
using Inferior.Gameplay.Physics;

namespace Inferior.Gameplay.SensorData;

/// <summary>
/// Gravitational physics calculations for use by sensors and the Environment query class.
/// Pure static functions — no state, no side effects.
/// </summary>
public static class GravityCalculations
{
    // G lives in Units — no need to duplicate it here.

    /// <summary>
    /// Minimum distance squared used to avoid singularity at body surface.
    /// 1000 km — well below any real stellar surface, safe for game use.
    /// </summary>
    private const double MinDistSq = 1e6 * 1e6; // (1000 km)²

    /// <summary>
    /// Net gravitational acceleration vector at <paramref name="position"/>, in m/s².
    /// Sums contributions from all massive bodies via Newton's law of gravitation.
    /// </summary>
    public static DVec3 GravityAt(DVec3 position, IReadOnlyList<CelestialBody> bodies)
    {
        DVec3 total = DVec3.Zero;

        foreach (var body in bodies)
        {
            DVec3  delta  = body.Position - position;
            double distSq = delta.LengthSquared;
            if (distSq < MinDistSq) continue;

            double accel = Units.G * body.Mass / distSq;
            total       += DVec3.Normalize(delta) * accel;
        }

        return total;
    }
}
