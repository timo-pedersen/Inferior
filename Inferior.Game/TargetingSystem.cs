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
    {
        _contacts[contact.Id] = contact;
        if (_radarTarget?.Id == contact.Id)
            _radarTarget = contact;
    }

    // Called from DataBus.RadarLost subscriber
    public void OnContactLost(string id)
    {
        _contacts.Remove(id);
        if (_radarTarget?.Id == id)
            SetRadarTarget(null);
    }

    // 'T' key — select the contact whose screen projection is closest to screen centre
    public void SelectClosestObjectToReticle(Matrix viewProjection, Viewport viewport)
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

    public void SelectClosestNavToReticle(
        Matrix viewProjection,
        Viewport viewport,
        DVec3 cameraPos,
        Star star,
        IReadOnlyList<(OrbitalBody body, DVec3 galaxyPos)> bodyPositions,
        IReadOnlyList<(Station station, DVec3 galaxyPos)> stationPositions)
    {
        var centre = new Vector2(viewport.Width * 0.5f, viewport.Height * 0.5f);
        float bestDist = float.MaxValue;
        OrbitalBody? bestBody = null;
        Station? bestStation = null;
        bool bestIsStar = false;

        TryCandidate(DVec3.Zero, screenDistance =>
        {
            if (screenDistance >= bestDist) return;
            bestDist = screenDistance;
            bestBody = null;
            bestStation = null;
            bestIsStar = true;
        });

        foreach (var (body, pos) in bodyPositions)
        {
            TryCandidate(pos, screenDistance =>
            {
                if (screenDistance >= bestDist) return;
                bestDist = screenDistance;
                bestBody = body;
                bestStation = null;
                bestIsStar = false;
            });
        }

        foreach (var (station, pos) in stationPositions)
        {
            TryCandidate(pos, screenDistance =>
            {
                if (screenDistance >= bestDist) return;
                bestDist = screenDistance;
                bestBody = null;
                bestStation = station;
                bestIsStar = false;
            });
        }

        if (bestIsStar)
            SetNavTargetStar(star);
        else if (bestBody != null)
            SetNavTarget(bestBody);
        else if (bestStation != null)
            SetNavTarget(bestStation);

        void TryCandidate(DVec3 targetPos, Action<float> accept)
        {
            DVec3 relative = targetPos - cameraPos;
            var rel3 = new Vector3((float)relative.X, (float)relative.Y, (float)relative.Z);
            Vector2? screen = ProjectToScreen(rel3, viewProjection, viewport);
            if (screen == null) return;
            accept(Vector2.Distance(screen.Value, centre));
        }
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
    private string?      _navStationPersistenceId;
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
        _navStationPersistenceId = null;
        _navIsStar  = false;
        NavTargetName = overrideName ?? body.Name;
        DataBus.System.Publish(Topics.System.All, new($"Nav target: {NavTargetName}"));
    }

    public void SetNavTarget(Station station)
    {
        _navBody                 = null;
        _navStation              = station;
        _navStationPersistenceId = station.PersistenceId;
        _navIsStar               = false;
        NavTargetName            = station.Name;
        DataBus.System.Publish(Topics.System.All, new($"Nav target: {NavTargetName}"));
    }

    public void SetNavTargetStar(Star star)
    {
        _navBody    = null;
        _navStation = null;
        _navStationPersistenceId = null;
        _navIsStar  = true;
        NavTargetName = star.Name;
        DataBus.System.Publish(Topics.System.All, new($"Nav target: {NavTargetName}"));
    }

    public void ClearNavTarget()
    {
        _navBody                 = null;
        _navStation              = null;
        _navStationPersistenceId = null;
        _navIsStar               = false;
        NavTargetName            = "";
        NavTargetPosition        = DVec3.Zero;
        NavTargetDistance        = 0;
        NavTargetDirection       = DVec3.Zero;
        DataBus.System.Publish(Topics.System.All, new("Nav target cleared"));
    }

    // ── Hyperspace target (galaxy star selection) ──────────────────────────────

    private DVec3? _hypGalPos;
    private Star?  _hypStar;

    public string  HyperspaceTargetName       { get; private set; } = "";
    public Star?   HyperspaceTargetStar       => _hypStar;
    public bool    HasHyperspaceTarget        => _hypGalPos.HasValue;
    public double  HyperspaceTargetDistanceLY { get; private set; }
    public DVec3   HyperspaceTargetDirection  { get; private set; }

    public void SetHyperspaceTarget(Star star)
    {
        _hypStar             = star;
        _hypGalPos           = star.GalacticPos;
        HyperspaceTargetName = star.Name;
    }

    public void ClearHyperspaceTarget()
    {
        _hypStar                   = null;
        _hypGalPos                 = null;
        HyperspaceTargetName       = "";
        HyperspaceTargetDistanceLY = 0;
        HyperspaceTargetDirection  = DVec3.Zero;
    }

    // ── Pad target (independent of radar and nav targets) ─────────────────────

    private Station?    _padTargetStation;
    private LandingPad? _padTarget;

    public bool        HasPadTarget       => _padTarget != null;
    public LandingPad? TargetedPad        => _padTarget;
    public Station?    TargetedPadStation => _padTargetStation;

    public void SetPadTarget(Station station, LandingPad pad)
    {
        _padTargetStation = station;
        _padTarget        = pad;
        string label = $"PAD {pad.PadIndex + 1:D2} [{pad.PadSize}]";
        DataBus.System.Publish(Topics.System.All, new($"Pad targeted: {label}"));
    }

    public void ClearPadTarget()
    {
        if (_padTarget == null) return;
        _padTargetStation = null;
        _padTarget        = null;
        DataBus.System.Publish(Topics.System.All, new("Pad target cleared"));
    }

    // L-key: cycles through pads of the nearest station with populated geometry.
    // First press → nearest pad. Each subsequent press → next pad. Last pad → clear.
    public void CyclePad(
        DVec3 shipPos,
        IReadOnlyList<(Station station, DVec3 worldPos)> stationPositions,
        double gameTime)
    {
        // Find nearest station that has populated landing pads
        Station? nearest   = null;
        DVec3    nearestPos = default;
        double   nearestD  = double.MaxValue;
        foreach (var (station, pos) in stationPositions)
        {
            if (station.LandingPads.Count == 0) continue;
            // Require at least one pad with populated normal
            bool hasPads = false;
            foreach (var p in station.LandingPads)
                if (p.LocalNormal.Length > 0.5) { hasPads = true; break; }
            if (!hasPads) continue;
            double d = (pos - shipPos).Length;
            if (d < nearestD) { nearestD = d; nearest = station; nearestPos = pos; }
        }
        if (nearest == null) return;

        // Build the list of usable pads (populated geometry)
        var usable = new List<LandingPad>();
        foreach (var p in nearest.LandingPads)
            if (p.LocalNormal.Length > 0.5) usable.Add(p);
        if (usable.Count == 0) return;

        if (_padTargetStation == nearest && _padTarget != null)
        {
            // Already targeting a pad on this station — advance or clear
            int idx = usable.IndexOf(_padTarget);
            if (idx < 0 || idx == usable.Count - 1)
                ClearPadTarget();
            else
                SetPadTarget(nearest, usable[idx + 1]);
        }
        else
        {
            // New station — select nearest pad to ship
            Quaternion ori        = nearest.GetOrientation(gameTime);
            LandingPad? bestPad  = null;
            double      bestDist = double.MaxValue;
            foreach (var pad in usable)
            {
                var local  = new Vector3((float)pad.LocalPosition.X, (float)pad.LocalPosition.Y, (float)pad.LocalPosition.Z);
                var offset = Vector3.Transform(local, ori);
                var padPos = nearestPos + new DVec3(offset.X, offset.Y, offset.Z);
                double d   = (padPos - shipPos).Length;
                if (d < bestDist) { bestDist = d; bestPad = pad; }
            }
            if (bestPad != null) SetPadTarget(nearest, bestPad);
        }
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
            DataBus.System.Publish(Topics.System.All, new($"Target: {contact.Value.DisplayName}"));
        }
        else
        {
            DataBus.Target.Publish(Topics.Target.Changed, default);
            DataBus.System.Publish(Topics.System.All, new("Target cleared"));
        }
    }

    // Projects a contact's render-space position to screen pixels.
    // RelativePosition is in metres from the current render camera origin.
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
            string matchId = _navStationPersistenceId ?? _navStation.Name;
            bool byId = _navStationPersistenceId != null;
            foreach (var (station, pos) in stationPositions)
            {
                string candidate = byId ? (station.PersistenceId ?? station.Name) : station.Name;
                if (candidate == matchId) { targetPos = pos; break; }
            }
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
