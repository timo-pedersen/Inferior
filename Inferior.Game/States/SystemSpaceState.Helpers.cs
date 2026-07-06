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

    // Enters a different star system, re-using OnEnter logic without a full state transition.
    private void EnterSystem(Star star, DVec3 spawnPos, Quaternion spawnOri, FlightMode mode)
    {
        _star   = star;
        _system = StarSystem.Generate(star, GalaxyGenerator.SystemSeed(star));
        _eclipticRotation = Matrix.Identity;  // reset until proper ecliptic rotation is set

        // Rebuild skybox for new star
        var (skyPoints, skyGlow, targetable) = SkyboxRenderer.Build(_star, GalaxyGenerator.Generate());
        _skyboxRenderer.Load(skyPoints, skyGlow);
        _targetableStars = targetable;

        _camera.SetPose(spawnPos, spawnOri);
        _simulation.TeleportShip(spawnPos, spawnOri);
        _simulation.SetFlightMode(mode);

        DataBus.System.Publish(Topics.System.All, new($"Arrived in {star.Name}"));
    }

    // ── 3-tier render passes ─────────────────────────────────────────────────
    //
    // One near/far pair can't serve millimetre fastener detail and system-scale
    // distances at once — verified dead end (no floating-point depth format on this
    // platform rules out reversed-Z; the ratio math doesn't close for a single pass
    // at any value; deriving far from near assumes the nearest and farthest things
    // you care about are the same object, which they usually aren't). Three fixed-
    // scope passes, composited far-to-near with only the depth buffer cleared
    // between them (never colour), so a nearer pass paints over a farther one's
    // output with no cross-pass depth testing needed — correctness comes from the
    // passes covering strictly decreasing, non-overlapping-by-construction ranges,
    // not from depth comparison.
    //
    // Mid tier's outer boundary (57,000 m) is derived from angular size: a feature
    // stops being worth a dedicated pass once it projects to a few screen pixels —
    // below that, texture filtering/AA already blends it away, no LOD needed.
    //   d = 2r · verticalRes / (N · FOV_rad)
    // Using the largest real module radius (41.57 m — measured from the actual
    // registry, largest is the 16×16×80 connector), N=3 px, 4K vertical (2160,
    // checked as the harder case since more pixels/degree keeps detail resolvable
    // longer), 60° vertical FOV (this game's fixed floor — narrower FOV keeps
    // detail visible even longer, so 60° is correctly the case needing the largest
    // margin):
    //   d = 2×41.57×2160 / (3×1.0472) = 179,582 / 3.1416 ≈ 57,162 m → 57,000 m
    // Comfortably covers the empirical 510m real-station and 2,500m legacy
    // mega-station figures (23–112x margin).
    private const double MidTierNear = 5.0;        // metres — near tier hands off here
    private const double MidTierFar  = 57_000.0;   // metres — derived above
    private const float  FarTierFar  = 50_000f;    // render units — system-scale default, unchanged

    // Near tier's own far boundary can't be a fixed constant either — its near value
    // ranges from ~5cm (5m proximity) to sub-micron at the curve's degenerate floor,
    // over 5 decades on its own. far = near × ratio keeps that ratio constant by
    // construction, comfortable across most of the near tier's range. Floored at
    // MidTierNear so a shrinking near never drops far below where the mid tier picks
    // up — without the floor, sub-15cm proximity would open a real gap (ratio-term
    // alone falls below 5m there). The floor closes the gap; it does not make the
    // most extreme sub-centimetre end "comfortable" — that residual is inherent to
    // the near tier's own range and would need a 4th tier or a curve reshape, out of
    // scope here.
    private const double NearTierComfortableRatio = 10_000.0;

    private sealed record RenderPassConfig(float Near, float Far, System.Action DrawCallback);

    // Far-to-near so depth-clearing between passes is always safe: whatever's already
    // in the colour buffer is strictly farther than what's about to draw on top of it.
    private List<RenderPassConfig> BuildActivePasses()
    {
        double nearTierNearReal = ComputeNearTierNear();
        double nearTierFarReal  = System.Math.Max(nearTierNearReal * NearTierComfortableRatio, MidTierNear);

        float farTierNear  = (float)(MidTierFar * Camera3D.RenderScale);
        float midTierNear  = (float)(MidTierNear * Camera3D.RenderScale);
        float midTierFar   = farTierNear; // tiles exactly against the far tier's near
        float nearTierNear = (float)(nearTierNearReal * Camera3D.RenderScale);
        float nearTierFar  = (float)(nearTierFarReal * Camera3D.RenderScale);

        return
        [
            new RenderPassConfig(farTierNear,  FarTierFar, DrawFarPassContent),
            new RenderPassConfig(midTierNear,  midTierFar, DrawMidPassContent),
            new RenderPassConfig(nearTierNear, nearTierFar, DrawNearPassContent),
        ];
    }

    // Near tier's own near-clip — extreme close-up (fasteners, container insets,
    // rivets). Unchanged curve from prior tuning; the farthest-module tracking that
    // used to live here has moved to the mid tier's fixed far, since real numbers
    // showed no station needs more than ~500m of reach (see BuildActivePasses' header).
    private double ComputeNearTierNear()
    {
        // In third-person, camera is ~80–90 m from the ship — clip at 1% of that distance
        // (default would hide the ship entirely).
        if (_thirdPersonMode && _frameShipSnap != null)
        {
            double distToShip = (_frameShipSnap.Position - _camera.UniversePosition).Length;
            return distToShip * 0.01;
        }

        DVec3  camPos  = _camera.UniversePosition;
        double minSurf = double.MaxValue;

        // Measure against each placed module's real position, not an idealized per-size-class
        // bounding sphere around the station's centre — a flat station-wide radius has no
        // relationship to the station's actual (procedurally grown, often lopsided) shape.
        // Station module counts are small (tens, not hundreds — see StationGenerator's
        // moduleLimit), so brute-force iteration here is cheap; no spatial index needed.
        foreach (var (station, stPos) in _stationPositions)
        {
            if (!_stationGeometry.TryGetValue(station, out var modules)) continue;

            var sysQ       = station.GetOrientation(_gameTimeSeconds);
            var stationRot = new Quaternion(sysQ.X, sysQ.Y, sysQ.Z, sysQ.W);

            foreach (var mod in modules)
            {
                Vector3 modOffset      = Vector3.Transform(mod.Transform.Translation, stationRot);
                DVec3   modUniversePos = stPos + new DVec3(modOffset.X, modOffset.Y, modOffset.Z);
                double moduleRadius    = (mod.AabbMax - mod.AabbMin).Length() * 0.5;
                double dist            = (modUniversePos - camPos).Length;
                double nearSurf        = System.Math.Max(dist - moduleRadius, 0.0);
                if (nearSurf < minSurf) minSurf = nearSurf;
            }
        }

        if (minSurf < 500_000.0) // within 500 km of any station module
        {
            // Floor the reference distance just above zero (rather than at the station's
            // own radius) so the near clip can shrink almost all the way to the camera —
            // needed to inspect small nearby objects (e.g. containers) up close without
            // the near plane slicing through them. Only guards against a degenerate
            // (zero) near plane; z-precision loss when flying through the station
            // interior is an accepted tradeoff.
            double refDist = System.Math.Max(minSurf, 0.001);

            // Non-linear falloff — stays metre-scale at ordinary approach distances and
            // only collapses toward cm/mm scale in the last few metres:
            //   refDist    near
            //    500 m     ~20 m
            //    300 m     ~10 m
            //    100 m     ~2.5 m
            //     20 m     ~31 cm
            //      5 m     ~5 cm
            //      1 m     ~6 mm
            //    0.1 m     ~0.3 mm
            const double NearClipCurveExponent = 1.3;
            const double NearClipCurveScale    = 0.00628;
            return NearClipCurveScale * System.Math.Pow(refDist, NearClipCurveExponent);
        }

        // Nothing station-related in range — near tier has nothing to draw. Pick a
        // default whose ratio-term lands exactly at the MidTierNear floor (0.0005m ×
        // 10,000 = 5m), so the unused pass carries zero overlap rather than an
        // arbitrary one.
        return 0.0005;
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
}
