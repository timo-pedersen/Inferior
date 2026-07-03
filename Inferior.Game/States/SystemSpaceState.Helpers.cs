using Inferior.Core;
using Inferior.Core.DataBus;
using Inferior.Core.Math;
using Inferior.Core.Simulation;
using Inferior.Galaxy;
using Inferior.Game.Hyperspace;
using Inferior.Game.StationGen;
using Inferior.Game.UI;
using Inferior.Gameplay;
using Inferior.Gameplay.Components;
using Inferior.Gameplay.Components.Power;
using Inferior.Gameplay.Sensors;
using Inferior.Gameplay.Ship;
using Inferior.Rendering;
using Inferior.UI;
using Inferior.UI.Controls;
using Inferior.UI.Controls.Cockpit;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System.Reflection.Metadata;

namespace Inferior.Game.States;

public sealed partial class SystemSpaceState
{

    // ── Helpers ───────────────────────────────────────────────────────────────

    private float AspectRatio =>
        (float)_gd.Viewport.Width / _gd.Viewport.Height;

    private void UpdateUI() { }

    private void UpdateReferenceFrame(DVec3 shipPos)
    {
        const double StationDist = 25_000.0; // 25 km

        // Priority 1: close station
        foreach (var (station, stPos) in _stationPositions)
        {
            if ((stPos - shipPos).Length < StationDist)
            {
                _refVelocity = EclipticToGalaxy(_system.GetStationVelocity(station, _gameTimeSeconds));
                _refName     = station.Name;
                return;
            }
        }

        // Priority 2: dominant body via gravity vector + gravitational weighting.
        //
        // Score = (m/r²) × cos(angle_between_dir_and_gravity).
        // Top-3 cull by m/r² first so a tiny distant planet behind the star can
        // never beat the star itself on angle alone.
        var gravEcl = new DVec3(_gravDirX, _gravDirY, _gravDirZ);
        if (gravEcl.Length < 0.01)
        {
            _refVelocity = DVec3.Zero;
            _refName     = _star.Name;
            return;
        }
        DVec3 gravGal = EclipticToGalaxy(gravEcl);

        // Top-3 slots by m/r² (named variables — no heap allocation)
        OrbitalBody? t0b = null, t1b = null, t2b = null;
        DVec3 t0p = DVec3.Zero, t1p = DVec3.Zero, t2p = DVec3.Zero;
        double t0w = 0, t1w = 0, t2w = 0;

        void Keep(OrbitalBody? body, DVec3 pos, double w)
        {
            if      (w > t0w) { t2b=t1b; t2p=t1p; t2w=t1w; t1b=t0b; t1p=t0p; t1w=t0w; t0b=body; t0p=pos; t0w=w; }
            else if (w > t1w) { t2b=t1b; t2p=t1p; t2w=t1w; t1b=body; t1p=pos; t1w=w; }
            else if (w > t2w) { t2b=body; t2p=pos; t2w=w; }
        }

        double starDist = shipPos.Length;
        if (starDist > 100.0) Keep(null, DVec3.Zero, _star.MassKg / (starDist * starDist));
        foreach (var (body, pos) in _bodyPositions)
        {
            double d = (pos - shipPos).Length;
            if (d > 100.0) Keep(body, pos, body.MassKg / (d * d));
        }

        OrbitalBody? bestBody = null; DVec3 bestPos = DVec3.Zero;
        double bestScore = 0.0; double winCos = 1.0;

        void Score(OrbitalBody? body, DVec3 pos, double w)
        {
            if (w == 0.0) return;
            DVec3 to = pos - shipPos; double d = to.Length;
            if (d < 100.0) return;
            var dir = new DVec3(to.X / d, to.Y / d, to.Z / d);
            double cos = dir.X * gravGal.X + dir.Y * gravGal.Y + dir.Z * gravGal.Z;
            if (cos <= 0.0) return;
            double s = w * cos;
            if (s > bestScore) { bestScore = s; winCos = cos; bestBody = body; bestPos = pos; }
        }

        Score(t0b, t0p, t0w); Score(t1b, t1p, t1w); Score(t2b, t2p, t2w);

        DVec3 domVelocity; string domName;
        if (bestBody == null)
        {
            domVelocity = DVec3.Zero;
            domName     = _star.Name;
        }
        else
        {
            domVelocity = GetBodyGalaxyVelocity(bestBody, _gameTimeSeconds);
            domName     = bestBody.Name;
        }

        // Blend: fully locked at ≤ 45°, linear fade to 0 at 90°
        double angle   = System.Math.Acos(System.Math.Clamp(winCos, -1.0, 1.0));
        double lockRad = System.Math.PI / 4.0;
        double zeroRad = System.Math.PI / 2.0;
        double blend   = angle <= lockRad ? 1.0
                       : angle <  zeroRad ? 1.0 - (angle - lockRad) / (zeroRad - lockRad)
                       : 0.0;

        // In atmosphere: ship.Velocity is planet-relative (zero reference).
        _refVelocity = _simulation.CurrentFlightMode is FlightMode.AtmosphericNewtonian or FlightMode.AtmosphericSlipstream
            ? DVec3.Zero
            : domVelocity * blend;
        _refName = domName;
    }

    // Returns the galaxy-space position of a planet or moon at the given game time.
    private DVec3 GetBodyGalaxyPosition(OrbitalBody body, double gameTime)
    {
        foreach (var planet in _system.Planets)
        {
            if (ReferenceEquals(planet, body))
                return EclipticToGalaxy(planet.GetPosition(gameTime, DVec3.Zero));
            foreach (var moon in planet.Children)
            {
                if (ReferenceEquals(moon, body))
                    return EclipticToGalaxy(moon.GetPosition(gameTime, planet.GetPosition(gameTime, DVec3.Zero)));
            }
        }
        return EclipticToGalaxy(body.GetPosition(gameTime, DVec3.Zero));
    }

    // Returns the analytically computed galaxy-space velocity of a planet or moon.
    private DVec3 GetBodyGalaxyVelocity(OrbitalBody body, double gameTime)
    {
        foreach (var planet in _system.Planets)
        {
            if (ReferenceEquals(planet, body))
                return EclipticToGalaxy(PlanetVelocityEcl(planet, gameTime));
            foreach (var moon in planet.Children)
            {
                if (ReferenceEquals(moon, body))
                {
                    var pv = PlanetVelocityEcl(planet, gameTime);
                    var mv = OrbitalVelocityEcl(gameTime, moon.Period, moon.PhaseOffset, moon.OrbitalRadius);
                    return EclipticToGalaxy(pv + mv);
                }
            }
        }
        return EclipticToGalaxy(OrbitalVelocityEcl(gameTime, body.Period, body.PhaseOffset, body.OrbitalRadius));
    }

    // Keplerian velocity for planets, circular fallback for legacy bodies.
    private static DVec3 PlanetVelocityEcl(OrbitalBody planet, double gameTime)
        => planet.SemiMajorAxis > 0.0 && planet.ParentMassKg > 0.0
            ? planet.ComputeVelocity(gameTime, Units.G * planet.ParentMassKg, DVec3.Zero)
            : OrbitalVelocityEcl(gameTime, planet.Period, planet.PhaseOffset, planet.OrbitalRadius);

    private static DVec3 OrbitalVelocityEcl(double gameTime, double period, double phaseOffset, double radius)
    {
        double angle = DMath.OrbitalAngle(gameTime, period, phaseOffset);
        double omega = 2.0 * System.Math.PI / period;
        return new DVec3(-System.Math.Sin(angle) * radius * omega, 0.0, System.Math.Cos(angle) * radius * omega);
    }

    // ── Ecliptic tilt ─────────────────────────────────────────────────────────

    private void ComputeEclipticRotation()
    {
        var tiltAxis = new Vector3(
            MathF.Cos(_system.EclipticTiltAzimuthRadians),
            0f,
            MathF.Sin(_system.EclipticTiltAzimuthRadians));
        _eclipticRotation = Matrix.CreateFromAxisAngle(tiltAxis, _system.EclipticTiltRadians);

        // Build double-precision rotation from the same axis/angle to avoid float
        // quantisation (~9 km at 1 AU) when transforming large universe coordinates.
        // Rodrigues formula for axis (ux, 0, uz) — uy = 0 by construction (tilt axis is horizontal).
        double ux  = tiltAxis.X, uz = tiltAxis.Z;
        double a   = _system.EclipticTiltRadians;
        double cos = System.Math.Cos(a), sin = System.Math.Sin(a), ic = 1.0 - cos;
        _er00 = cos + ux * ux * ic;  _er01 = -uz * sin;       _er02 = ux * uz * ic;
        _er10 = uz * sin;            _er11 = cos;              _er12 = -ux * sin;
        _er20 = ux * uz * ic;        _er21 = ux * sin;         _er22 = cos + uz * uz * ic;
    }

    // Full double-precision ecliptic-to-galaxy rotation.
    private DVec3 EclipticToGalaxy(DVec3 pos) => new(
        _er00 * pos.X + _er01 * pos.Y + _er02 * pos.Z,
        _er10 * pos.X + _er11 * pos.Y + _er12 * pos.Z,
        _er20 * pos.X + _er21 * pos.Y + _er22 * pos.Z);

    // Inverse rotation (transpose of the rotation matrix — orthogonal matrix property).
    // Used to convert galaxy-space relative positions back to ecliptic plane for the landing radar.
    private DVec3 GalaxyToEcliptic(DVec3 pos) => new(
        _er00 * pos.X + _er10 * pos.Y + _er20 * pos.Z,
        _er01 * pos.X + _er11 * pos.Y + _er21 * pos.Z,
        _er02 * pos.X + _er12 * pos.Y + _er22 * pos.Z);

    // ── Near clip ─────────────────────────────────────────────────────────────

    // Near clip is proportional to the distance from the camera to the nearest station surface.
    // This keeps it large (10 km) in open space — good z-precision at system scale —
    // and shrinks it as you approach a station, reaching ~1 mm at hull contact.
    // Far-distance z-precision degrades when up-close, but that's acceptable: nothing
    // at AU-scale distances competes for depth buffer precision when you're docking.
    private float ComputeNearClip()
    {
        // In third-person, camera is ~80–90 m from the ship — clip at 1% of that distance
        // (default 10 km near clip would hide the ship entirely).
        if (_thirdPersonMode && _frameShipSnap != null)
        {
            double distToShip = (_frameShipSnap.Position - _camera.UniversePosition).Length;
            return (float)(distToShip * 0.01 * Camera3D.RenderScale);
        }

        DVec3  camPos        = _camera.UniversePosition;
        double minSurf       = double.MaxValue;
        double nearestRadius = 250.0; // fallback: smallest station size

        foreach (var (station, stPos) in _stationPositions)
        {
            double r    = StationPhysicalRadius(station);
            double dist = (stPos - camPos).Length;
            double surf = System.Math.Max(dist - r, 0.0);
            if (surf < minSurf) { minSurf = surf; nearestRadius = r; }
        }

        if (minSurf < 500_000.0) // within 500 km of any station surface
        {
            // Floor the reference distance at the station's own radius so Z precision
            // is preserved even when flying through the interior (where surfDist = 0
            // would otherwise collapse the depth buffer to a single value).
            double refDist    = System.Math.Max(minSurf, nearestRadius);
            double nearMeters = refDist * 0.001;
            return (float)(nearMeters * Camera3D.RenderScale);
        }

        return 0.00001f; // default: 10 km near clip
    }

    // ── Proximity speed scale ─────────────────────────────────────────────────
    //
    // Two independent zones — star and bodies — each with three knobs:
    //   FarDist  : surface distance where scaling begins (full free speed beyond this)
    //   NearDist : surface distance where scaling is fully applied
    //   MinScale : multiplier at NearDist — expressed as (target m/s) / (max scroll step)
    //              so "top step → X m/s at NearDist" is readable at a glance.
    //
    // The most restrictive zone wins each frame.

    // Star — large zone, still fast enough near the star to orbit / escape
    private const double StarProxFarDist  = 2.25e11;        // 1.5 AU
    private const double StarProxNearDist = 1e10;            // 10,000,000 km
    private const double StarProxMinScale = 1e5 / 1e12;     // top step → 100 km/s

    // Planets & moons — zone around each body
    // Floor is 100 km/s at 1 km from surface. Atmosphere state takes over well above that,
    // so this only matters for airless bodies; atmospheric bodies transition at ~80 km altitude.
    private const double BodyProxFarDist  = 5e8;               // 500,000 km
    private const double BodyProxNearDist = 1e3;               // 1 km
    private const double BodyProxMinScale = 1e5 / 1e12;        // top step → 100 km/s

    // Stations — flat cap within 10 km of any station surface
    private const double StationProxCapDist  = 1e4;            // 10 km
    private const double StationProxMinScale = 2000.0 / 1e12;  // top step → 2000 m/s

    private double ComputeProximityScale()
    {
        // Star.RadiusMeters is double-converted in generation, so use the visual render
        // radius instead — StarVisualRadius=8 render units = 8/RenderScale = 8e9 m.
        const double StarVisualRadiusMeters = 8.0 / Camera3D.RenderScale;
        double starSurf = System.Math.Max(_camera.UniversePosition.Length - StarVisualRadiusMeters, 0.0);

        double starScale = ScaleForDist(starSurf, StarProxNearDist, StarProxFarDist, StarProxMinScale);

        double bodyScale = 1.0;
        foreach (var (body, pos) in _bodyPositions)
        {
            double surf = System.Math.Max((_camera.UniversePosition - pos).Length - body.RadiusMeters, 0.0);
            double s    = ScaleForDist(surf, BodyProxNearDist, BodyProxFarDist, BodyProxMinScale);
            if (s < bodyScale) bodyScale = s;
        }

        double stationScale = 1.0;
        foreach (var (station, pos) in _stationPositions)
        {
            double r    = StationPhysicalRadius(station);
            double surf = System.Math.Max((_camera.UniversePosition - pos).Length - r, 0.0);
            if (surf <= StationProxCapDist) stationScale = StationProxMinScale;
        }

        return System.Math.Min(System.Math.Min(starScale, bodyScale), stationScale);
    }

    // Ship speed uses the same star/body proximity zones as the camera, but the station
    // zone is a hard cap (2000 m/s max) rather than a proportional scale — so the ship
    // always has a usable docking speed regardless of the current scroll step.
    private double ComputeShipSpeed(DVec3 shipPos)
    {
        const double StarVisualRadiusMeters = 8.0 / Camera3D.RenderScale;
        double starSurf  = System.Math.Max(shipPos.Length - StarVisualRadiusMeters, 0.0);
        double starScale = ScaleForDist(starSurf, StarProxNearDist, StarProxFarDist, StarProxMinScale);

        double bodyScale = 1.0;
        foreach (var (body, pos) in _bodyPositions)
        {
            double surf = System.Math.Max((shipPos - pos).Length - body.RadiusMeters, 0.0);
            double s    = ScaleForDist(surf, BodyProxNearDist, BodyProxFarDist, BodyProxMinScale);
            if (s < bodyScale) bodyScale = s;
        }

        double speed = _shipBaseSpeed * System.Math.Min(starScale, bodyScale);

        // Hard cap near stations — independent of scroll step
        foreach (var (station, pos) in _stationPositions)
        {
            double r    = StationPhysicalRadius(station);
            double surf = System.Math.Max((shipPos - pos).Length - r, 0.0);
            if (surf <= StationProxCapDist)
                speed = System.Math.Min(speed, 2000.0);
        }

        return speed;
    }

    private static double ScaleForDist(double surfDist, double nearDist, double farDist, double minScale)
    {
        if (surfDist >= farDist)  return 1.0;
        if (surfDist <= nearDist) return minScale;

        double t = System.Math.Log(surfDist / nearDist)
                 / System.Math.Log(farDist  / nearDist);
        t = t * t * (3.0 - 2.0 * t); // smoothstep

        return System.Math.Exp(t * System.Math.Log(1.0 / minScale)) * minScale;
    }

    // ── 2D primitives (same as other states) ──────────────────────────────────

    private void DrawText(SpriteBatch sb, string text, Vector2 pos, Color color, float scale = 1.0f)
        => FontHelper.Draw(sb, _font, text, pos, color, scale);

    private void DrawRect(SpriteBatch sb, Rectangle rect, Color color)
        => sb.Draw(_pixel, rect, color);

    private void DrawRectBorder(SpriteBatch sb, Rectangle rect, Color color, int thickness = 1)
    {
        sb.Draw(_pixel, new Rectangle(rect.Left,  rect.Top,              rect.Width, thickness), color);
        sb.Draw(_pixel, new Rectangle(rect.Left,  rect.Bottom-thickness, rect.Width, thickness), color);
        sb.Draw(_pixel, new Rectangle(rect.Left,  rect.Top,  thickness,  rect.Height),           color);
        sb.Draw(_pixel, new Rectangle(rect.Right-thickness, rect.Top, thickness, rect.Height),   color);
    }
}
