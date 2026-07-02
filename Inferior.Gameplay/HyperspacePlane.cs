using Inferior.Core.Math;
using Inferior.Galaxy;

namespace Inferior.Gameplay;

/// <summary>
/// A single star projected onto the hyperspace plane with its disturbance metadata.
/// </summary>
public sealed record PlaneDisturbance(
    Star   Star,
    DVec3  ProjectedPos,    // galactic position on the plane (ly)
    double Strength,        // 0..1 — 1 = star exactly on the plane
    double SignedDistLY);   // signed distance from plane in ly

/// <summary>
/// The flat hyperspace plane: defined by the ship's up vector (normal) and its galactic
/// position at the moment H is pressed. Forward and Right are the ship's in-plane axes.
/// Construct once per hyperspace entry; immutable after that.
/// </summary>
public sealed class HyperspacePlane
{
    public DVec3 Origin  { get; }
    public DVec3 Normal  { get; }   // ship up at entry — plane normal
    public DVec3 Forward { get; }   // ship forward projected onto plane (unit)
    public DVec3 Right   { get; }   // cross(Normal, Forward) — right in plane

    private readonly IReadOnlyList<PlaneDisturbance> _disturbances;

    public HyperspacePlane(DVec3 origin, DVec3 shipUp, DVec3 shipForward, Star[] allStars)
    {
        Origin  = origin;
        Normal  = shipUp.Normalized();

        // Project ship forward onto the plane: remove the component along the normal
        var fwdProj = shipForward - Normal * DVec3.Dot(shipForward, Normal);
        Forward = fwdProj.LengthSquared < 1e-10
            ? DVec3.Cross(Normal, DVec3.UnitX).Normalized()  // degenerate fallback
            : fwdProj.Normalized();

        Right = DVec3.Cross(Normal, Forward).Normalized();

        _disturbances = ComputeDisturbances(allStars);
    }

    public IReadOnlyList<PlaneDisturbance> Disturbances => _disturbances;

    /// <summary>
    /// Converts a galactic position to 2-D plane coordinates (u along Right, v along Forward).
    /// </summary>
    public (double u, double v) ToPlaneCoords(DVec3 galPos)
    {
        var delta = galPos - Origin;
        return (DVec3.Dot(delta, Right), DVec3.Dot(delta, Forward));
    }

    /// <summary>
    /// Converts 2-D plane coords back to a galactic position on the plane.
    /// </summary>
    public DVec3 FromPlaneCoords(double u, double v)
        => Origin + Right * u + Forward * v;

    private List<PlaneDisturbance> ComputeDisturbances(Star[] stars)
    {
        var result = new List<PlaneDisturbance>();
        foreach (var star in stars)
        {
            double d = DVec3.Dot(star.GalacticPos - Origin, Normal);
            if (Math.Abs(d) > FlatHyperspaceConstants.MaxDisturbanceRadiusLY) continue;
            double strength  = 1.0 - Math.Abs(d) / FlatHyperspaceConstants.MaxDisturbanceRadiusLY;
            DVec3  projected = star.GalacticPos - Normal * d;
            result.Add(new PlaneDisturbance(star, projected, strength, d));
        }
        return result;
    }

    /// <summary>
    /// Checks whether the player at <paramref name="playerPos"/> (galactic ly) is close enough
    /// to a disturbance and heading toward it, triggering a dropout.
    /// Returns the triggering disturbance, or null if no dropout.
    /// </summary>
    public PlaneDisturbance? CheckDropout(DVec3 playerPos, DVec3 playerForward)
    {
        double cosThreshold = Math.Cos(
            FlatHyperspaceConstants.DropoutConeHalfAngleDeg * Math.PI / 180.0);
        double radiusSq = FlatHyperspaceConstants.DropoutRadiusLY
                        * FlatHyperspaceConstants.DropoutRadiusLY;

        foreach (var d in _disturbances)
        {
            var toDisturbance = d.ProjectedPos - playerPos;
            if (toDisturbance.LengthSquared > radiusSq) continue;
            if (toDisturbance.LengthSquared < 1e-20)    return d; // inside

            double cosAngle = DVec3.Dot(playerForward, toDisturbance.Normalized());
            if (cosAngle >= cosThreshold) return d;
        }
        return null;
    }
}
