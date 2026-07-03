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

    // ── Targeting HUD ────────────────────────────────────────────────────────

    private void DrawTargetingHUD(SpriteBatch sb)
    {
        var vp       = Matrix.Multiply(_camera.ViewMatrix, _camera.ProjectionMatrix);
        var viewport = _gd.Viewport;

        // Station / body contacts
        foreach (var contact in _targeting.AllContacts)
        {
            Vector2? screen = TargetingSystem.ProjectToScreen(contact.RelativePosition, vp, viewport);
            if (screen == null) continue;

            bool  isTarget = _targeting.CurrentRadarTarget?.Id == contact.Id;
            float dist     = contact.RelativePosition.Length();
            float size     = MathHelper.Clamp(3e6f / dist, 8f, 44f);
            float arm      = size * 0.40f;

            Color bracketColor = isTarget
                ? new Color(0, 220, 220)
                : new Color(100, 100, 100);

            DrawBracket(sb, screen.Value, size, arm, 2, bracketColor);

            if (isTarget)
            {
                string distStr = dist < 1000f
                    ? $"{dist:F0} m"
                    : $"{dist / 1000f:F1} km";
                Vector2 labelPos = screen.Value + new Vector2(-40f, size + 6f);
                FontHelper.Draw(sb, _font, contact.DisplayName, labelPos,                        new Color(0, 220, 220));
                FontHelper.Draw(sb, _font, distStr,             labelPos + new Vector2(0f, 18f), new Color(0, 180, 180));
            }
        }

        // Pad target bracket (green, slightly smaller than station brackets)
        if (_targeting.HasPadTarget && _padDistance > 0.1)
        {
            var pad = _targeting.TargetedPad!;
            DVec3 relPos  = _padWorldPos - _camera.UniversePosition;
            var   rel3    = new Vector3((float)relPos.X, (float)relPos.Y, (float)relPos.Z);
            Vector2? screen = TargetingSystem.ProjectToScreen(rel3, vp, viewport);
            if (screen != null)
            {
                Color padColor  = new Color(60, 220, 90);
                float size      = MathHelper.Clamp(2e6f / (float)_padDistance, 6f, 36f);
                DrawBracket(sb, screen.Value, size, size * 0.40f, 2, padColor);

                string padId  = $"PAD {pad.PadIndex + 1:D2}";
                string distStr = _padDistance < 100.0
                    ? $"{_padDistance:F1} m"
                    : _padDistance < 1000.0
                        ? $"{_padDistance:F0} m"
                        : $"{_padDistance / 1000.0:F1} km";
                Vector2 labelPos = screen.Value + new Vector2(-30f, size + 6f);
                FontHelper.Draw(sb, _font, padId,    labelPos,                        padColor);
                FontHelper.Draw(sb, _font, distStr,  labelPos + new Vector2(0f, 18f), new Color(40, 180, 70));
            }
        }
    }

    // Four L-shaped corner brackets centred on `centre`.
    private void DrawBracket(SpriteBatch sb, Vector2 centre, float size, float arm, int thickness, Color color)
    {
        int s  = (int)size;
        int al = (int)arm;
        int t  = thickness;
        int cx = (int)centre.X;
        int cy = (int)centre.Y;

        // Top-left
        sb.Draw(_pixel, new Rectangle(cx - s,      cy - s,      al, t),  color);
        sb.Draw(_pixel, new Rectangle(cx - s,      cy - s,      t,  al), color);
        // Top-right
        sb.Draw(_pixel, new Rectangle(cx + s - al, cy - s,      al, t),  color);
        sb.Draw(_pixel, new Rectangle(cx + s - t,  cy - s,      t,  al), color);
        // Bottom-left
        sb.Draw(_pixel, new Rectangle(cx - s,      cy + s - t,  al, t),  color);
        sb.Draw(_pixel, new Rectangle(cx - s,      cy + s - al, t,  al), color);
        // Bottom-right
        sb.Draw(_pixel, new Rectangle(cx + s - al, cy + s - t,  al, t),  color);
        sb.Draw(_pixel, new Rectangle(cx + s - t,  cy + s - al, t,  al), color);
    }

    private void UpdateDirectionBall(DirectionBall? ball)
    {
        if (ball == null) return;
        ball.SetOrientation(_camera.Forward, _camera.Right, _camera.Up);

        var toStar = DVec3.Zero - _camera.UniversePosition;
        if (toStar.Length > 0.001)
        {
            toStar = toStar / toStar.Length;
            ball.SetVector("star",
                new Vector3((float)toStar.X, (float)toStar.Y, (float)toStar.Z),
                new Color(255, 220, 80), "*"); // "★"
        }

        var gravEcliptic = new Vector3((float)_gravDirX, (float)_gravDirY, (float)_gravDirZ);
        if (gravEcliptic.LengthSquared() > 0.001f)
        {
            var gravGalaxy = Vector3.TransformNormal(gravEcliptic, _eclipticRotation);
            ball.SetVector("grav", gravGalaxy, new Color(220, 60, 200), "g", dotRadius: 2.0f);
        }

        // Clear station markers from the previous frame so out-of-range ones don't persist.
        for (int i = 0; i < _stationPositions.Count; i++)
            ball.RemoveVector($"station_{i}");

        // Collect all bodies plus stations within 100 km into a ranked list.
        // Sorting by distance lets us assign the largest dot to the closest object.
        var ranked = new List<(string key, Vector3 dir, Color color, double dist)>(
            _bodyPositions.Count + _stationPositions.Count);

        for (int i = 0; i < _bodyPositions.Count; i++)
        {
            var (body, bodyPos) = _bodyPositions[i];
            var toBody = bodyPos - _camera.UniversePosition;
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
        for (int i = 0; i < _stationPositions.Count; i++)
        {
            var (_, stPos) = _stationPositions[i];
            var toStation  = stPos - _camera.UniversePosition;
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

    // Pushes planets, moons, and stations into TargetingSystem each frame so the
    // C-key and click-to-target handlers have contacts to work with.
    // Positions are already galaxy-space from the current frame's _bodyPositions /
    // _stationPositions.  RelativePosition is the vector from camera to contact in
    // metres (float precision is fine for screen-space projection).
    private void FeedRadarContacts()
    {
        DVec3 camPos = _camera.UniversePosition;

        // Remove any stale body:* contacts (planets/moons are not shown on radar)
        var staleBodyIds = new List<string>();
        foreach (var id in _radarContactIds)
            if (id.StartsWith("body:", StringComparison.Ordinal))
                staleBodyIds.Add(id);
        foreach (var id in staleBodyIds)
        {
            _targeting.OnContactLost(id);
            _cockpitDirBall?.RemoveVector($"radar_{id}");
            _radarContactIds.Remove(id);
        }

        foreach (var (station, galaxyPos) in _stationPositions)
        {
            string id      = $"station:{station.Name}";
            DVec3  del     = galaxyPos - camPos;
            var    contact = new RadarContact(
                id, station.Name,
                new Vector3((float)del.X, (float)del.Y, (float)del.Z),
                Vector3.Zero, ContactType.Station);
            _targeting.OnContactUpdated(contact);
            UpdateCockpitDirBallContact(contact);
            _radarContactIds.Add(id);
        }

        // TODO: remove test containers — debug contacts for radar testing
        foreach (var tc in _testContainers)
        {
            DVec3 stPos = DVec3.Zero;
            foreach (var (s, sPos) in _stationPositions)
                if (ReferenceEquals(s, tc.Station)) { stPos = sPos; break; }
            DVec3 pos   = stPos + tc.Offset;
            DVec3 del   = pos - camPos;
            var   contact = new RadarContact(
                tc.Id, tc.Name,
                new Vector3((float)del.X, (float)del.Y, (float)del.Z),
                Vector3.Zero, ContactType.Debris);
            _targeting.OnContactUpdated(contact);
            UpdateCockpitDirBallContact(contact);
            _radarContactIds.Add(tc.Id);
        }
    }

    private void UpdateCockpitDirBallContact(RadarContact c)
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

    private void UpdateTargetingUI()
    {
        if (_targetingDirBall == null) return;
        _targetingDirBall.SetOrientation(_camera.Forward, _camera.Right, _camera.Up);

        var tc = _ui?.Theme;

        // Radar target (ship/station contact)
        if (_targeting.HasRadarTarget)
        {
            var contact = _targeting.CurrentRadarTarget!.Value;
            float distM = contact.RelativePosition.Length();
            var col = tc?.TargetShip ?? new Color(0, 220, 220);
            var dir = Vector3.Normalize(contact.RelativePosition);
            _targetingDirBall.SetVector("ship", dir, col, "T");
            if (_targetLineShip != null)
                _targetLineShip.Text = $"Target: {contact.DisplayName} ({Units.FormatDistance(distM)})";
        }
        else
        {
            _targetingDirBall.RemoveVector("ship");
            if (_targetLineShip != null)
                _targetLineShip.Text = "Target: None";
        }

        // Nav target (yellow)
        if (_targeting.HasNavTarget)
        {
            var d    = _targeting.NavTargetDirection;
            var col  = tc?.TargetNav ?? new Color(255, 200, 50);
            _targetingDirBall.SetVector("nav", new Vector3((float)d.X, (float)d.Y, (float)d.Z), col, "N");
            if (_targetLineNav != null)
                _targetLineNav.Text = $"Nav: {_targeting.NavTargetName} ({Units.FormatDistance(_targeting.NavTargetDistance)})";
        }
        else
        {
            _targetingDirBall.RemoveVector("nav");
            if (_targetLineNav != null) _targetLineNav.Text = "Nav: None";
        }

        // Hyperspace target (blue)
        if (_targeting.HasHyperspaceTarget)
        {
            var d   = _targeting.HyperspaceTargetDirection;
            var col = tc?.TargetHyp ?? new Color(80, 160, 255);
            _targetingDirBall.SetVector("hyp", new Vector3((float)d.X, (float)d.Y, (float)d.Z), col, "H");
            if (_targetLineHyp != null)
                _targetLineHyp.Text = $"Hyp: {_targeting.HyperspaceTargetName} ({_targeting.HyperspaceTargetDistanceLY:F1} ly)";
        }
        else
        {
            _targetingDirBall.RemoveVector("hyp");
            if (_targetLineHyp != null) _targetLineHyp.Text = "Hyp: None";
            _lockedSkyboxStar = null;  // keep ring in sync with targeting system
        }

        // Pad target (green)
        if (_targeting.HasPadTarget && _padDirection.Length > 0.5)
        {
            var pad    = _targeting.TargetedPad!;
            string distStr = _padDistance < 100.0
                ? $"{_padDistance:F1} m"
                : _padDistance < 1000.0
                    ? $"{_padDistance:F0} m"
                    : $"{_padDistance / 1000.0:F1} km";
            _targetingDirBall.SetVector("pad",
                new Vector3((float)_padDirection.X, (float)_padDirection.Y, (float)_padDirection.Z),
                new Color(60, 220, 90), "P");
        }
        else
        {
            _targetingDirBall.RemoveVector("pad");
        }
    }

    private void UpdateRadarDisplay()
    {
        if (_radarDisplay == null) return;
        _radarDisplay.Contacts        = _targeting.AllContacts;
        _radarDisplay.SelectedContact = _targeting.CurrentRadarTarget;

        var snap = _frameShipSnap;
        _radarDisplay.LocalFrameSpeedMs = snap != null ? (float)snap.Velocity.Length : 0f;

        // Ship-local orientation axes so the radar can project contacts correctly.
        if (snap != null)
        {
            _radarDisplay.ShipForward = new Vector3((float)snap.Forward.X, (float)snap.Forward.Y, (float)snap.Forward.Z);
            _radarDisplay.ShipUp      = new Vector3((float)snap.Up.X,      (float)snap.Up.Y,      (float)snap.Up.Z);
            _radarDisplay.ShipRight   = Vector3.Cross(_radarDisplay.ShipForward, _radarDisplay.ShipUp);
        }

        // Approach speed — closing rate along selected contact direction
        float approachMs = 0f;
        if (_targeting.HasRadarTarget)
        {
            var c    = _targeting.CurrentRadarTarget!.Value;
            float rlen = c.RelativePosition.Length();
            if (rlen > 1f)
                approachMs = -Vector3.Dot(c.RelativeVelocity, c.RelativePosition / rlen);
        }
        _radarDisplay.ApproachSpeedMs = approachMs;

        // PWR LED — radar is always active while in SystemSpace
        _radarDisplay.PwrLed = true;
    }

    private void UpdateLandingRadar()
    {
        if (_landingRadar == null) return;

        var navStation = _targeting.NavStationTarget;
        if (navStation != null)
        {
            DVec3 relGalaxy   = (_frameShipSnap?.Position ?? _camera.UniversePosition)
                              - _targeting.NavTargetPosition;
            DVec3 relEcliptic = GalaxyToEcliptic(relGalaxy);
            _landingRadar.HasStation     = true;
            _landingRadar.StationName    = navStation.Name;
            _landingRadar.DistanceMeters = _targeting.NavTargetDistance;
            _landingRadar.RelX           = (float)relEcliptic.X;
            _landingRadar.RelZ           = (float)relEcliptic.Z;
        }
        else
        {
            _landingRadar.HasStation  = false;
            _landingRadar.StationName = "";
        }
    }

    // Computes pad world position from current station orbit + orientation, then
    // publishes Docking.* topics to the Instruments bus.
    private void UpdatePadTargetPosition()
    {
        if (!_targeting.HasPadTarget)
        {
            DataBus.Instruments.Publish(Topics.Docking.PadTargeted, 0.0);
            _padWorldPos  = DVec3.Zero;
            _padDistance  = 0.0;
            _padDirection = DVec3.Zero;
            _simulation.SetPadTarget(null);
            return;
        }

        var station = _targeting.TargetedPadStation!;
        var pad     = _targeting.TargetedPad!;

        // Find station's current world position
        DVec3 stationPos = DVec3.Zero;
        foreach (var (s, pos) in _stationPositions)
            if (s == station) { stationPos = pos; break; }

        // Transform pad local position and normal by station orientation
        Quaternion ori       = station.GetOrientation(_gameTimeSeconds);
        var localPos         = new Vector3((float)pad.LocalPosition.X, (float)pad.LocalPosition.Y, (float)pad.LocalPosition.Z);
        var localNrm         = new Vector3((float)pad.LocalNormal.X,   (float)pad.LocalNormal.Y,   (float)pad.LocalNormal.Z);
        var offset           = Vector3.Transform(localPos, ori);
        var worldNrmV        = Vector3.Normalize(Vector3.TransformNormal(localNrm, Matrix.CreateFromQuaternion(ori)));
        _padWorldPos         = stationPos + new DVec3(offset.X, offset.Y, offset.Z);
        DVec3 worldNormal    = new DVec3(worldNrmV.X, worldNrmV.Y, worldNrmV.Z);

        DVec3 shipPos        = _frameShipSnap?.Position ?? _camera.UniversePosition;
        DVec3 delta          = _padWorldPos - shipPos;
        _padDistance         = delta.Length;
        _padDirection        = _padDistance > 1.0 ? delta * (1.0 / _padDistance) : DVec3.Zero;

        // Rotate the station-local forward axis (stored by StationGenerator to match the visual arrow)
        // to world space via the station orientation quaternion.
        var localFwdV = new Vector3((float)pad.LocalForward.X, (float)pad.LocalForward.Y, (float)pad.LocalForward.Z);
        var worldFwdV = Vector3.Normalize(Vector3.TransformNormal(localFwdV, Matrix.CreateFromQuaternion(ori)));
        DVec3 padForward = new DVec3(worldFwdV.X, worldFwdV.Y, worldFwdV.Z);

        // Push to sim thread for LandingSupportSystem
        var padData = new LandingPadData(
            WorldPosition: _padWorldPos,
            WorldNormal:   worldNormal,
            ForwardAxis:   padForward,
            PadSize:       pad.PadSize,
            BayId:         $"PAD {pad.PadIndex + 1:D2}",
            StationName:   station.Name);
        _simulation.SetPadTarget(padData);

        // Publish to Instruments bus so Step 3 docking instrument can subscribe
        DataBus.Instruments.Publish(Topics.Docking.PadTargeted,   1.0);
        DataBus.Instruments.Publish(Topics.Docking.PadDistance,   _padDistance);
        DataBus.Instruments.Publish(Topics.Docking.PadDirectionX, _padDirection.X);
        DataBus.Instruments.Publish(Topics.Docking.PadDirectionY, _padDirection.Y);
        DataBus.Instruments.Publish(Topics.Docking.PadDirectionZ, _padDirection.Z);
        DataBus.Instruments.Publish(Topics.Docking.PadSizeClass,  pad.PadSize == Galaxy.PadSize.Large ? 1.0 : 0.0);
    }
}
