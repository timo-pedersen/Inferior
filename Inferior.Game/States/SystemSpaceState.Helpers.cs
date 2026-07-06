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

    // ── Near/far clip ─────────────────────────────────────────────────────────

    // Bounds the near/far ratio so depth-buffer precision doesn't collapse when the
    // near plane shrinks to inspect something up close (e.g. a container).
    //
    // 1e12 is deliberately far above the ratio already tolerated today in normal
    // flight (~5e9 in open space at the default 10 km near clip; ~6e13 in third-person,
    // where near shrinks to ~1 m) — this bound is a no-op for both of those, and only
    // engages for the genuinely extreme case this brief targets: near collapsing toward
    // zero at point-blank container range, where the unbounded ratio reaches ~1e19.
    // Tune based on playtest — if fine surface detail still z-fights at typical
    // inspection distances, lower it; if distant objects disappear more aggressively
    // than feels right, raise it.
    private const double MaxNearFarRatio = 1e12;

    // Safety headroom past the farthest known module, applied to the independent far-clip
    // measurement below — covers minor model/AABB inaccuracy, not a tuning knob for
    // ratio comfort (that's MaxNearFarRatio's job, and only as a pathological backstop now).
    private const double FarSafetyMargin = 1.15;

    private (float near, float far) ComputeNearFarClip()
    {
        const float DefaultFar = 50_000f;

        var (near, farthestModuleDist, nearStation) = ComputeNearClipValue();

        if (!nearStation)
            return (near, (float)System.Math.Min(DefaultFar, near * MaxNearFarRatio));

        // Far is measured independently here — distance to the farthest module actually in
        // range, not a multiple of near. Deriving far from near (far = near * ratio) implicitly
        // assumes the nearest thing and the farthest thing you care about are the same object;
        // they're usually not (you're near one module, trying to still see another one hundreds
        // of metres off). That coupling is exactly what produced two reported symptoms: a
        // station vanishing while your distance to the module you were looking at hadn't
        // changed (near collapsed because of proximity to a *different*, closer module, and
        // dragged far down with it), and the mirror case — backing away from one module grew
        // near-clip enough to slice a different, closer module out of view.
        //
        // MaxNearFarRatio is still applied, but only as a backstop for a genuinely degenerate
        // near (near collapsing toward zero) — not as the primary source of far. Using the
        // tighter station-proximity ratio here instead would silently reintroduce the same
        // bug: near legitimately shrinks to ~1m-equivalent whenever you're close to *any*
        // nearby surface (a greeble, a corner, a small module), and near * 10,000 at that
        // point is only ~63m — well under the few-hundred-metre reach needed to keep a
        // farther module visible. The loose 1e12 ratio never engages against a real
        // (already-bounded-by-actual-geometry) desiredFar, so it stays a true backstop.
        double desiredFar = farthestModuleDist * Camera3D.RenderScale * FarSafetyMargin;
        float  far        = (float)System.Math.Min(desiredFar, near * MaxNearFarRatio);

        return (near, far);
    }

    // Near clip is proportional to the distance from the camera to the nearest station surface.
    // This keeps it large (10 km) in open space — good z-precision at system scale —
    // and shrinks it as you approach a station, reaching ~1 mm at hull contact.
    // Far-distance z-precision degrades when up-close, but that's acceptable: nothing
    // at AU-scale distances competes for depth buffer precision when you're docking.
    private (float near, double farthestModuleDist, bool nearStation) ComputeNearClipValue()
    {
        // In third-person, camera is ~80–90 m from the ship — clip at 1% of that distance
        // (default 10 km near clip would hide the ship entirely). Not a station-proximity
        // case — uses the global ratio cap, same as open space.
        if (_thirdPersonMode && _frameShipSnap != null)
        {
            double distToShip = (_frameShipSnap.Position - _camera.UniversePosition).Length;
            return ((float)(distToShip * 0.01 * Camera3D.RenderScale), 0.0, false);
        }

        DVec3  camPos        = _camera.UniversePosition;
        double minSurf       = double.MaxValue;
        double maxModuleSurf = 0.0;

        // Measure against each placed module's real position, not an idealized per-size-class
        // bounding sphere around the station's centre. A flat station-wide radius has no
        // relationship to the station's actual (procedurally grown, often lopsided) shape —
        // it either clips through real structure sitting outside the idealized sphere (a
        // far-flung module, or decoration protruding past it), or leaves near-clip too large
        // while weaving between modules that sit well inside it. Per-module distance fixes
        // both: proximity now reflects whichever real module you're actually closest to.
        // Station module counts are small (tens, not hundreds — see StationGenerator's
        // moduleLimit), so brute-force iteration here is cheap; no spatial index needed.
        //
        // Near and far want opposite sides of each module: minSurf wants the surface facing
        // the camera (dist - radius, how close can near-clip get before slicing this module),
        // while maxModuleSurf wants the surface facing away from the camera (dist + radius,
        // how far out does the farthest module's far edge actually reach) — using the same
        // near-side value for both would undershoot far-clip by up to a module's own diameter.
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
                double farSurf         = dist + moduleRadius;
                if (nearSurf < minSurf)       minSurf       = nearSurf;
                if (farSurf  > maxModuleSurf) maxModuleSurf = farSurf;
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

            // Non-linear falloff. The old near = refDist * 0.001 shrank near-clip
            // linearly with distance, which left only a 10cm near-clip at a completely
            // ordinary 100m approach — depth-buffer precision concentrates near the
            // near-clip plane, so that starved station geometry (walls, greebles
            // spanning tens of metres) of precision and caused z-fighting, independent
            // of far-clip. This curve stays metre-scale at ordinary approach distances
            // and only collapses toward cm/mm scale in the last few metres, which is
            // where close-up inspection (e.g. a container) actually needs it:
            //   refDist    near
            //    500 m     ~20 m
            //    300 m     ~10 m
            //    100 m     ~2.5 m
            //     20 m     ~31 cm
            //      5 m     ~5 cm
            //      1 m     ~6 mm
            //    0.1 m     ~0.3 mm
            // Tune NearClipCurveScale/Exponent if either end still looks wrong in
            // practice — Exponent controls how sharply it collapses at close range,
            // Scale sets the anchor (currently near(100m) = 2.5m).
            const double NearClipCurveExponent = 1.3;
            const double NearClipCurveScale    = 0.00628;
            double nearMeters = NearClipCurveScale * System.Math.Pow(refDist, NearClipCurveExponent);
            return ((float)(nearMeters * Camera3D.RenderScale), maxModuleSurf, true);
        }

        return (0.00001f, 0.0, false); // default: 10 km near clip
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
