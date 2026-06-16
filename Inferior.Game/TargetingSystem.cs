using Inferior.Core.DataBus;
using Inferior.Core.Math;
using Inferior.Galaxy;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.Game;

/// <summary>
/// Tracks three simultaneous targets: ship (radar contact), nav, and hyperspace.
/// Updated each frame from the ship's current position.
/// Lives on the main thread — not simulation-thread safe.
/// </summary>
public sealed class TargetingSystem
{
    // ── Radar contact selection ────────────────────────────────────────────────

    private readonly Dictionary<string, RadarContact> _contacts = [];
    private RadarContact? _radarTarget;

    public RadarContact? CurrentRadarTarget => _radarTarget;
    public bool          HasRadarTarget     => _radarTarget.HasValue;

    public IEnumerable<RadarContact> AllContacts => _contacts.Values;

    // Called from DataBus.Radar subscriber — keep contact list current
    public void OnContactUpdated(RadarContact contact)
        => _contacts[contact.Id] = contact;

    // Called from DataBus.RadarLost subscriber
    public void OnContactLost(string id)
    {
        _contacts.Remove(id);
        if (_radarTarget?.Id == id)
            SetRadarTarget(null);
    }

    // 'C' key — select the contact whose screen projection is closest to screen centre
    public void SelectClosestToReticle(Matrix viewProjection, Viewport viewport)
    {
        var     centre   = new Vector2(viewport.Width * 0.5f, viewport.Height * 0.5f);
        float   bestDist = float.MaxValue;
        RadarContact? best = null;

        foreach (var contact in _contacts.Values)
        {
            Vector2? screen = ProjectToScreen(contact.RelativePosition, viewProjection, viewport);
            if (screen == null) continue;
            float d = Vector2.Distance(screen.Value, centre);
            if (d < bestDist) { bestDist = d; best = contact; }
        }
        SetRadarTarget(best);
    }

    // Left-click in UI mode — select contact nearest to cursor within snap radius
    public void SelectAtCursor(Vector2 mousePos, Matrix viewProjection, Viewport viewport)
    {
        const float MaxClickRadius = 80f;
        float bestDist = MaxClickRadius;
        RadarContact? best = null;

        foreach (var contact in _contacts.Values)
        {
            Vector2? screen = ProjectToScreen(contact.RelativePosition, viewProjection, viewport);
            if (screen == null) continue;
            float d = Vector2.Distance(screen.Value, mousePos);
            if (d < bestDist) { bestDist = d; best = contact; }
        }
        if (best.HasValue)
            SetRadarTarget(best);
        // No match within radius: leave existing target unchanged
    }

    public void ClearRadarTarget() => SetRadarTarget(null);

    // ── Nav target (planet / moon / star / station) ────────────────────────────

    private OrbitalBody? _navBody;
    private Station?     _navStation;
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
        _navBody           = null;
        _navStation        = null;
        _navIsStar         = false;
        NavTargetName      = "";
        NavTargetPosition  = DVec3.Zero;
        NavTargetDistance  = 0;
        NavTargetDirection = DVec3.Zero;
    }

    // ── Hyperspace target (galaxy star selection) ──────────────────────────────

    private DVec3? _hypGalPos;

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
        _hypGalPos                 = null;
        HyperspaceTargetName       = "";
        HyperspaceTargetDistanceLY = 0;
        HyperspaceTargetDirection  = DVec3.Zero;
    }

    // ── Accessors for current nav target type ─────────────────────────────────

    public OrbitalBody? NavBodyTarget    => _navBody;
    public Station?     NavStationTarget => _navStation;
    public bool         NavIsStar        => _navIsStar;

    // ── Update (called each frame from main thread) ────────────────────────────

    public void Update(
        DVec3 shipPos,
        DVec3 currentStarGalPos,
        IReadOnlyList<(OrbitalBody body, DVec3 galaxyPos)>  bodyPositions,
        IReadOnlyList<(Station station, DVec3 galaxyPos)>   stationPositions)
    {
        UpdateNavTarget(shipPos, bodyPositions, stationPositions);
        UpdateHyperspaceTarget(currentStarGalPos);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private void SetRadarTarget(RadarContact? contact)
    {
        _radarTarget = contact;
        if (contact.HasValue)
        {
            DataBus.Target.Publish(Topics.Target.Changed, contact.Value);
            DataBus.System.Publish(Topics.System.All, $"Target: {contact.Value.DisplayName}");
        }
        else
        {
            DataBus.Target.Publish(Topics.Target.Changed, default);
            DataBus.System.Publish(Topics.System.All, "Target cleared");
        }
    }

    // Projects a contact's render-space position to screen pixels.
    // RelativePosition is in metres (relative to ship = camera origin).
    // Returns null when the contact is behind the camera or off-screen.
    internal static Vector2? ProjectToScreen(Vector3 relPosMetres, Matrix viewProjection, Viewport vp)
    {
        Vector3 renderPos = relPosMetres * (float)Rendering.Camera3D.RenderScale;
        Vector4 clip = Vector4.Transform(new Vector4(renderPos, 1f), viewProjection);
        if (clip.W <= 0f) return null;

        float ndcX = clip.X / clip.W;
        float ndcY = clip.Y / clip.W;

        float sx = ( ndcX + 1f) * 0.5f * vp.Width;
        float sy = (-ndcY + 1f) * 0.5f * vp.Height;

        const float Margin = 200f;
        if (sx < -Margin || sx > vp.Width  + Margin) return null;
        if (sy < -Margin || sy > vp.Height + Margin) return null;

        return new Vector2(sx, sy);
    }

    private void UpdateNavTarget(
        DVec3 shipPos,
        IReadOnlyList<(OrbitalBody body, DVec3 galaxyPos)>  bodyPositions,
        IReadOnlyList<(Station station, DVec3 galaxyPos)>   stationPositions)
    {
        if (!HasNavTarget) return;

        DVec3 targetPos;

        if (_navIsStar)
        {
            targetPos = DVec3.Zero;
        }
        else if (_navBody != null)
        {
            targetPos = DVec3.Zero;
            foreach (var (body, pos) in bodyPositions)
                if (body.Name == _navBody.Name) { targetPos = pos; break; }
        }
        else if (_navStation != null)
        {
            targetPos = DVec3.Zero;
            foreach (var (station, pos) in stationPositions)
                if (station.Name == _navStation.Name) { targetPos = pos; break; }
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
}
