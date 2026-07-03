using Inferior.Core;
using Inferior.Core.Math;
using Inferior.Game;
using Inferior.Rendering;
using Inferior.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.Game.UI;

public sealed partial class CockpitUI
{
    public void UpdateTargetingAndRadar(
        Camera3D camera, DVec3 shipPos, SpaceSimulation.ShipSnapshot? shipSnap,
        DVec3 padWorldPos, double padDistance, DVec3 padDirection)
    {
        UpdateTargetingUI(camera, padDistance, padDirection);
        UpdateRadarDisplay(shipSnap);
        UpdateLandingRadar(shipPos);
    }

    private void UpdateTargetingUI(Camera3D camera, double padDistance, DVec3 padDirection)
    {
        if (_targetingDirBall == null) return;
        _targetingDirBall.SetOrientation(camera.Forward, camera.Right, camera.Up);

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
        }

        // Pad target (green)
        if (_targeting.HasPadTarget && padDirection.Length > 0.5)
        {
            var pad    = _targeting.TargetedPad!;
            string distStr = padDistance < 100.0
                ? $"{padDistance:F1} m"
                : padDistance < 1000.0
                    ? $"{padDistance:F0} m"
                    : $"{padDistance / 1000.0:F1} km";
            _targetingDirBall.SetVector("pad",
                new Vector3((float)padDirection.X, (float)padDirection.Y, (float)padDirection.Z),
                new Color(60, 220, 90), "P");
        }
        else
        {
            _targetingDirBall.RemoveVector("pad");
        }
    }

    private void UpdateRadarDisplay(SpaceSimulation.ShipSnapshot? shipSnap)
    {
        if (_radarDisplay == null) return;
        _radarDisplay.Contacts        = _targeting.AllContacts;
        _radarDisplay.SelectedContact = _targeting.CurrentRadarTarget;

        var snap = shipSnap;
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

    private void UpdateLandingRadar(DVec3 shipPos)
    {
        if (_landingRadar == null) return;

        var navStation = _targeting.NavStationTarget;
        if (navStation != null)
        {
            DVec3 relGalaxy   = shipPos - _targeting.NavTargetPosition;
            DVec3 relEcliptic = _galaxyToEcliptic(relGalaxy);
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

    public void DrawTargetingHud(SpriteBatch sb, Camera3D camera, DVec3 padWorldPos, double padDistance)
    {
        var vp       = Matrix.Multiply(camera.ViewMatrix, camera.ProjectionMatrix);
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
        if (_targeting.HasPadTarget && padDistance > 0.1)
        {
            var pad = _targeting.TargetedPad!;
            DVec3 relPos  = padWorldPos - camera.UniversePosition;
            var   rel3    = new Vector3((float)relPos.X, (float)relPos.Y, (float)relPos.Z);
            Vector2? screen = TargetingSystem.ProjectToScreen(rel3, vp, viewport);
            if (screen != null)
            {
                Color padColor  = new Color(60, 220, 90);
                float size      = MathHelper.Clamp(2e6f / (float)padDistance, 6f, 36f);
                DrawBracket(sb, screen.Value, size, size * 0.40f, 2, padColor);

                string padId  = $"PAD {pad.PadIndex + 1:D2}";
                string distStr = padDistance < 100.0
                    ? $"{padDistance:F1} m"
                    : padDistance < 1000.0
                        ? $"{padDistance:F0} m"
                        : $"{padDistance / 1000.0:F1} km";
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
}
