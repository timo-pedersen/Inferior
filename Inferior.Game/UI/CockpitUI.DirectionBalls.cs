using Inferior.Core.DataBus;
using Inferior.Core.Math;
using Inferior.Galaxy;
using Inferior.Rendering;
using Inferior.UI.Controls.Cockpit;
using Microsoft.Xna.Framework;

namespace Inferior.Game.UI;

public sealed partial class CockpitUI
{
    public void UpdateDirectionBalls(
        Camera3D camera, Matrix eclipticRotation,
        double gravDirX, double gravDirY, double gravDirZ,
        List<(OrbitalBody body, DVec3 pos)> bodyPositions,
        List<(Galaxy.Station station, DVec3 pos)> stationPositions)
    {
        UpdateDirectionBall(_systemDirBall, camera, eclipticRotation,
            gravDirX, gravDirY, gravDirZ, bodyPositions, stationPositions);
        UpdateDirectionBall(_cockpitDirBall, camera, eclipticRotation,
            gravDirX, gravDirY, gravDirZ, bodyPositions, stationPositions);
    }

    private void UpdateDirectionBall(
        DirectionBall? ball, Camera3D camera, Matrix eclipticRotation,
        double gravDirX, double gravDirY, double gravDirZ,
        List<(OrbitalBody body, DVec3 pos)> bodyPositions,
        List<(Galaxy.Station station, DVec3 pos)> stationPositions)
    {
        if (ball == null) return;
        ball.SetOrientation(camera.Forward, camera.Right, camera.Up);

        var toStar = DVec3.Zero - camera.UniversePosition;
        if (toStar.Length > 0.001)
        {
            toStar = toStar / toStar.Length;
            ball.SetVector("star",
                new Vector3((float)toStar.X, (float)toStar.Y, (float)toStar.Z),
                new Color(255, 220, 80), "*"); // "★"
        }

        var gravEcliptic = new Vector3((float)gravDirX, (float)gravDirY, (float)gravDirZ);
        if (gravEcliptic.LengthSquared() > 0.001f)
        {
            var gravGalaxy = Vector3.TransformNormal(gravEcliptic, eclipticRotation);
            ball.SetVector("grav", gravGalaxy, new Color(220, 60, 200), "g", dotRadius: 2.0f);
        }

        // Clear station markers from the previous frame so out-of-range ones don't persist.
        for (int i = 0; i < stationPositions.Count; i++)
            ball.RemoveVector($"station_{i}");

        // Collect all bodies plus stations within 100 km into a ranked list.
        // Sorting by distance lets us assign the largest dot to the closest object.
        var ranked = new List<(string key, Vector3 dir, Color color, double dist)>(
            bodyPositions.Count + stationPositions.Count);

        for (int i = 0; i < bodyPositions.Count; i++)
        {
            var (body, bodyPos) = bodyPositions[i];
            var toBody = bodyPos - camera.UniversePosition;
            double dist = toBody.Length;
            if (dist < 1e7) continue;   // skip if somehow coincident

            var dir = new Vector3(
                (float)(toBody.X / dist),
                (float)(toBody.Y / dist),
                (float)(toBody.Z / dist));
            var color = body.BodyType == BodyType.Moon
                ? new Color(100, 130, 150)
                : new Color(100, 200, 160);
            ranked.Add(($"body_{i}", dir, color, dist));
        }

        const double StationRange = 100_000.0; // 100 km
        for (int i = 0; i < stationPositions.Count; i++)
        {
            var (_, stPos) = stationPositions[i];
            var toStation  = stPos - camera.UniversePosition;
            double dist    = toStation.Length;
            if (dist > StationRange || dist < 1.0) continue;

            var dir = new Vector3(
                (float)(toStation.X / dist),
                (float)(toStation.Y / dist),
                (float)(toStation.Z / dist));
            ranked.Add(($"station_{i}", dir, new Color(200, 180, 80), dist));
        }

        // Sort closest-first; rank 0 = largest dot (8 px), decreasing by 1 per rank, floor 3 px.
        ranked.Sort(static (a, b) => a.dist.CompareTo(b.dist));
        for (int i = 0; i < ranked.Count; i++)
        {
            var (key, dir, color, _) = ranked[i];
            ball.SetVector(key, dir, color, "", MathF.Max(3f, 8f - i));
        }
    }

    public void NotifyRadarContact(RadarContact c)
    {
        if (_cockpitDirBall == null) return;
        float len = c.RelativePosition.Length();
        if (len < 1f) return;
        var dir = c.RelativePosition / len;
        var col = c.Type switch
        {
            ContactType.Station => new Color(80,  200, 140),
            ContactType.Ship    => new Color(220,  80,  80),
            _                   => new Color(120, 120, 120),
        };
        _cockpitDirBall.SetVector($"radar_{c.Id}", dir, col);
    }

    public void NotifyRadarContactLost(string id)
        => _cockpitDirBall?.RemoveVector($"radar_{id}");
}
