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
    internal const string InitialStarterStationName = "Far Station";
    internal const double InitialStarterStationStandOffMeters = 500.0;
    internal const double SystemMapStationArrivalStandOffMeters = 2_000.0;

    internal readonly record struct StarterStationRelocationPlan(
        bool ShouldRelocate,
        string? StationPersistenceId,
        string? Diagnostic);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private float AspectRatio =>
        (float)_gd.Viewport.Width / _gd.Viewport.Height;

    private void UpdateUI() { }

    internal static bool IsInitialNewGameStarterEntry(SystemSpacePayload payload)
        => payload.InitialNewGameStarterEntry
        && payload.TargetBody == null
        && payload.StationArrival == null
        && payload.SpawnPos == null
        && payload.SpawnOrientation == null
        && payload.Layout == null;

    internal static StarterStationRelocationPlan CreateInitialStarterStationRelocationPlan(
        SystemSpacePayload payload,
        IEnumerable<Galaxy.Station> stations)
    {
        if (!IsInitialNewGameStarterEntry(payload))
            return new StarterStationRelocationPlan(false, null, null);

        var matches = stations
            .Where(station => string.Equals(station.Name, InitialStarterStationName, StringComparison.Ordinal))
            .ToArray();

        if (matches.Length != 1)
        {
            string diagnostic = matches.Length == 0
                ? $"{InitialStarterStationName} not found in generated starter system; preserving default starter spawn."
                : $"{InitialStarterStationName} is ambiguous in generated starter system ({matches.Length} matches); preserving default starter spawn.";
            return new StarterStationRelocationPlan(false, null, diagnostic);
        }

        string? persistenceId = matches[0].PersistenceId;
        if (string.IsNullOrWhiteSpace(persistenceId))
        {
            return new StarterStationRelocationPlan(
                false,
                null,
                $"{InitialStarterStationName} has no stable persistence id; preserving default starter spawn.");
        }

        return new StarterStationRelocationPlan(true, persistenceId, null);
    }

    private bool QueueInitialStarterStationRelocation(SystemSpacePayload payload)
    {
        var plan = CreateInitialStarterStationRelocationPlan(payload, _system.Stations);

        if (plan.Diagnostic != null)
            DataBus.System.Publish(Topics.System.All,
                new SystemMessage(plan.Diagnostic, SystemMessagePriority.ImportantWarning));

        if (!plan.ShouldRelocate)
            return false;

        _simulation.RequestStationRelocation(
            plan.StationPersistenceId!,
            InitialStarterStationStandOffMeters);
        return true;
    }

    private bool QueueStationArrivalRelocation(StationArrivalTarget target)
    {
        if (string.IsNullOrWhiteSpace(target.PersistenceId))
        {
            DataBus.System.Publish(Topics.System.All,
                new SystemMessage(
                    "Station arrival rejected: destination has no stable persistence id.",
                    SystemMessagePriority.ImportantWarning));
            return false;
        }

        if (!double.IsFinite(target.SurfaceStandOffMeters) || target.SurfaceStandOffMeters < 0.0)
        {
            string name = target.DisplayName ?? target.PersistenceId;
            DataBus.System.Publish(Topics.System.All,
                new SystemMessage(
                    $"Station arrival rejected: {name} has invalid stand-off {target.SurfaceStandOffMeters:R} m.",
                    SystemMessagePriority.ImportantWarning));
            return false;
        }

        _simulation.RequestStationRelocation(
            target.PersistenceId,
            target.SurfaceStandOffMeters);
        return true;
    }

    // ── Ecliptic tilt ─────────────────────────────────────────────────────────

    private void ComputeEclipticRotation()
    {
        var tiltAxis = new Vector3(
            MathF.Cos(_system.EclipticTiltAzimuthRadians),
            0f,
            MathF.Sin(_system.EclipticTiltAzimuthRadians));
        _eclipticRotation = Matrix.CreateFromAxisAngle(tiltAxis, _system.EclipticTiltRadians);
    }

    // Full double-precision ecliptic-to-galaxy rotation.
    private DVec3 EclipticToGalaxy(DVec3 pos)
        => CoordinateTransforms.EclipticToGalaxy(
            pos, _system.EclipticTiltAzimuthRadians, _system.EclipticTiltRadians);

    // Used to convert galaxy-space relative positions back to ecliptic plane for the landing radar.
    private DVec3 GalaxyToEcliptic(DVec3 pos)
        => CoordinateTransforms.GalaxyToEcliptic(
            pos, _system.EclipticTiltAzimuthRadians, _system.EclipticTiltRadians);

    // Enters a different star system, re-using OnEnter logic without a full state transition.
    private void EnterSystem(Star star, DVec3 spawnPos, Quaternion spawnOri, FlightMode mode)
    {
        _star   = star;
        _system = StarSystem.Generate(star, GalaxyGenerator.SystemSeed(star));
        ComputeEclipticRotation();
        _simulation.InstallSystem(_star, _system);

        // Rebuild skybox for new star
        var (skyPoints, skyGlow, targetable) = SkyboxRenderer.Build(_star, GalaxyGenerator.Generate());
        _skyboxRenderer.Load(skyPoints, skyGlow);
        _targetableStars = targetable;
        _camera.SetPose(spawnPos, spawnOri);
        RebuildStationGeometry();
        _stationPositions.Clear();
        foreach (var tc in _testContainers) { tc.Vb.Dispose(); tc.Ib.Dispose(); }
        _testContainers.Clear();
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

    // Near tier: fixed 100mm–5m. Verified via the depth-precision formula
    // Δz(z) ≈ z²·(f−n)/(n·f·2²⁴) that this range holds microns of precision
    // throughout — far finer than anything visually relevant — so no dynamic,
    // distance-computed near value is needed. Far side matches MidTierNear exactly,
    // so the two tiers tile with no gap.
    //
    // Known, accepted limitation: with near fixed at 100mm and no collision system,
    // nothing stops the camera from getting closer than 100mm and clipping through
    // geometry — the same category of limitation any fixed near-clip always has
    // without a minimum-standoff enforcement. Not a regression from fixing the
    // tiers; worth solving whenever collision detection itself gets designed.
    private const float NearTierNear = 0.1f;   // metres — 100mm
    private const float NearTierFar  = 5.0f;   // metres — matches MidTierNear

    private sealed record RenderPassConfig(float Near, float Far, DetailLevel Level, System.Action<DetailLevel> DrawCallback);

    // Far-to-near so depth-clearing between passes is always safe: whatever's already
    // in the colour buffer is strictly farther than what's about to draw on top of it.
    // No per-frame computation needed for any tier — all three boundaries are fixed.
    private List<RenderPassConfig> BuildActivePasses()
    {
        float farTierNear  = (float)(MidTierFar * Camera3D.RenderScale);
        float midTierNear  = (float)(MidTierNear * Camera3D.RenderScale);
        float midTierFar   = farTierNear; // tiles exactly against the far tier's near
        float nearTierNear = (float)(NearTierNear * Camera3D.RenderScale);
        float nearTierFar  = (float)(NearTierFar * Camera3D.RenderScale);

        return
        [
            new RenderPassConfig(farTierNear,  FarTierFar,  DetailLevel.Minimal, DrawFarPassContent),
            new RenderPassConfig(midTierNear,  midTierFar,  DetailLevel.Medium,  DrawMidPassContent),
            new RenderPassConfig(nearTierNear, nearTierFar, DetailLevel.Full,    DrawNearPassContent),
        ];
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
