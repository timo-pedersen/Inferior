using Inferior.Core.Math;
using Inferior.Galaxy;

namespace Inferior.Game;

/// <summary>
/// Tracks three simultaneous targets: ship, nav, and hyperspace.
/// Updated each frame from the ship's current position.
/// Lives on the main thread — not simulation-thread safe.
/// </summary>
public sealed class TargetingSystem
{
    // ── Nav target (planet / moon / star / station) ────────────────────────────

    private OrbitalBody? _navBody;     // planet or moon target
    private Station?     _navStation;  // station target
    private bool         _navIsStar;

    public string  NavTargetName      { get; private set; } = "";
    public bool    HasNavTarget       => _navBody != null || _navStation != null || _navIsStar;
    public DVec3   NavTargetPosition  { get; private set; }
    public double  NavTargetDistance  { get; private set; }
    public DVec3   NavTargetDirection { get; private set; }

    public void SetNavTarget(OrbitalBody body, string? overrideName = null)
    {
        _navBody    = body;
        _navStation = null;
        _navIsStar  = false;
        NavTargetName = overrideName ?? body.Name;
    }

    public void SetNavTarget(Station station)
    {
        _navBody    = null;
        _navStation = station;
        _navIsStar  = false;
        NavTargetName = station.Name;
    }

    public void SetNavTargetStar(Star star)
    {
        _navBody    = null;
        _navStation = null;
        _navIsStar  = true;
        NavTargetName = star.Name;
    }

    public void ClearNavTarget()
    {
        _navBody      = null;
        _navStation   = null;
        _navIsStar    = false;
        NavTargetName = "";
        NavTargetPosition  = DVec3.Zero;
        NavTargetDistance  = 0;
        NavTargetDirection = DVec3.Zero;
    }

    // ── Hyperspace target (galaxy star selection) ──────────────────────────────

    private DVec3? _hypGalPos;  // galactic position of target star (light-years)

    public string  HyperspaceTargetName       { get; private set; } = "";
    public bool    HasHyperspaceTarget        => _hypGalPos.HasValue;
    public double  HyperspaceTargetDistanceLY { get; private set; }
    public DVec3   HyperspaceTargetDirection  { get; private set; }

    public void SetHyperspaceTarget(Star star)
    {
        _hypGalPos           = star.GalacticPos;
        HyperspaceTargetName = star.Name;
    }

    public void ClearHyperspaceTarget()
    {
        _hypGalPos                  = null;
        HyperspaceTargetName        = "";
        HyperspaceTargetDistanceLY  = 0;
        HyperspaceTargetDirection   = DVec3.Zero;
    }

    // ── Accessors for current nav target type ─────────────────────────────────

    public OrbitalBody? NavBodyTarget    => _navBody;
    public Station?     NavStationTarget => _navStation;
    public bool         NavIsStar        => _navIsStar;

    // ── Ship target (radar — stub) ─────────────────────────────────────────────

    // Radar-detected ship targets are not yet implemented.
    // These properties will be populated by the radar system when implemented.
    public string  ShipTargetName      => "";
    public bool    HasShipTarget       => false;
    public double  ShipTargetDistance  => 0;
    public DVec3   ShipTargetDirection => DVec3.Zero;

    // ── Update (called each frame from main thread) ────────────────────────────

    /// <summary>
    /// Recomputes all target positions, distances, and directions from ship position.
    /// currentStarGalPos: galactic position of the current star (light-years) — used for
    /// hyperspace distance calculation.
    /// </summary>
    public void Update(
        DVec3      shipPos,
        DVec3      currentStarGalPos,
        double     gameTime,
        StarSystem system)
    {
        UpdateNavTarget(shipPos, gameTime, system);
        UpdateHyperspaceTarget(currentStarGalPos);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private void UpdateNavTarget(DVec3 shipPos, double gameTime, StarSystem system)
    {
        if (!HasNavTarget) return;

        DVec3 targetPos;

        if (_navIsStar)
        {
            // Star is always at system origin
            targetPos = DVec3.Zero;
        }
        else if (_navBody != null)
        {
            // Resolve body's current position (handles planets and moons via parent chain)
            targetPos = ResolveBodyPosition(_navBody, gameTime, system);
        }
        else if (_navStation != null)
        {
            targetPos = system.GetStationPosition(_navStation, gameTime);
        }
        else
        {
            return;
        }

        NavTargetPosition  = targetPos;
        NavTargetDistance  = (targetPos - shipPos).Length;
        DVec3 delta        = targetPos - shipPos;
        double len         = delta.Length;
        NavTargetDirection = len > 1e-6 ? delta * (1.0 / len) : DVec3.Zero;
    }

    private void UpdateHyperspaceTarget(DVec3 currentStarGalPos)
    {
        if (!_hypGalPos.HasValue) return;

        DVec3  delta = _hypGalPos.Value - currentStarGalPos;
        double dist  = delta.Length;
        HyperspaceTargetDistanceLY = dist;
        HyperspaceTargetDirection  = dist > 1e-10 ? delta * (1.0 / dist) : DVec3.Zero;
    }

    private static DVec3 ResolveBodyPosition(OrbitalBody body, double gameTime, StarSystem system)
    {
        // Check if this body is a planet
        foreach (var planet in system.Planets)
        {
            if (ReferenceEquals(planet, body))
                return planet.GetPosition(gameTime, DVec3.Zero);

            // Check moons of this planet
            foreach (var moon in planet.Children)
            {
                if (ReferenceEquals(moon, body))
                {
                    DVec3 planetPos = planet.GetPosition(gameTime, DVec3.Zero);
                    return moon.GetPosition(gameTime, planetPos);
                }
            }
        }

        // Fallback: treat as orbiting star
        return body.GetPosition(gameTime, DVec3.Zero);
    }
}
