using Inferior.Core.DataBus;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.UI.Controls.Cockpit;

/// <summary>
/// Docking approach instrument. Encodes four parameters simultaneously:
///
///   Lateral offset       — ship circle X position vs pad circle centre
///   Height above pad     — ship circle radius (larger = higher)
///   Heading deviation    — crosshair arm direction; top arm has nose arrow
///   Pitch deviation      — circle shape: round = face-on; ellipse → line = 90° off
///
/// Goal: align the ship circle (crosshair + arrow) over the pad circle.
/// Upside-down → ship circle is greyed out, no position shown.
///
/// Inactive when PadTargeted = 0 or outside activation range.
/// Data driven via DataBus.ScalarTelemetry subscriptions (topic prefix "Ship.LandingSupport.*").
/// </summary>
public sealed class DockingInstrument : Control, IDisposable
{
    // ── Incoming data (from DataBus subscriptions, updated on drain) ──────────
    private double _padTargeted;
    private double _padSizeClass;
    private double _padDistance;
    private double _heightAbovePad;
    private double _lateralOffset;
    private double _longitudinalOffset;
    private double _headingDev;
    private double _pitchDev;
    private double _upsideDown;

    // ── Subscriptions ─────────────────────────────────────────────────────────

    private readonly Action<double>[] _handlers;
    private readonly List<IDisposable> _subscriptions = [];

    public DockingInstrument()
    {
        // Store handlers so they can be unsubscribed if needed
        _handlers =
        [
            v => _padTargeted        = v,
            v => _padSizeClass       = v,
            v => _padDistance        = v,
            v => _heightAbovePad     = v,
            v => _lateralOffset      = v,
            v => _longitudinalOffset = v,
            v => _headingDev         = v * (180.0 / Math.PI),
            v => _pitchDev           = v * (180.0 / Math.PI),
            v => _upsideDown         = v,
        ];

        _subscriptions.Add(DataBus.ScalarTelemetry.Subscribe($"Ship.{Topics.LandingSupport.PadTargeted}",        _handlers[0]));
        _subscriptions.Add(DataBus.ScalarTelemetry.Subscribe($"Ship.{Topics.LandingSupport.PadSizeClass}",       _handlers[1]));
        _subscriptions.Add(DataBus.ScalarTelemetry.Subscribe($"Ship.{Topics.LandingSupport.PadDistance}",        _handlers[2]));
        _subscriptions.Add(DataBus.ScalarTelemetry.Subscribe($"Ship.{Topics.LandingSupport.HeightAbovePad}",     _handlers[3]));
        _subscriptions.Add(DataBus.ScalarTelemetry.Subscribe($"Ship.{Topics.LandingSupport.LateralOffset}",      _handlers[4]));
        _subscriptions.Add(DataBus.ScalarTelemetry.Subscribe($"Ship.{Topics.LandingSupport.LongitudinalOffset}", _handlers[5]));
        _subscriptions.Add(DataBus.ScalarTelemetry.Subscribe($"Ship.{Topics.LandingSupport.HeadingDeviation}",   _handlers[6]));
        _subscriptions.Add(DataBus.ScalarTelemetry.Subscribe($"Ship.{Topics.LandingSupport.PitchDeviation}",     _handlers[7]));
        _subscriptions.Add(DataBus.ScalarTelemetry.Subscribe($"Ship.{Topics.LandingSupport.UpsideDown}",         _handlers[8]));
    }

    public void Dispose()
    {
        foreach (IDisposable subscription in _subscriptions)
            subscription.Dispose();
        _subscriptions.Clear();
    }

    // ── Draw ──────────────────────────────────────────────────────────────────

    public override void Draw(SpriteBatch sb, UIRenderer renderer, Theme theme)
    {
        if (!Visible) return;

        var ab = AbsoluteBounds;
        renderer.FillRect(sb, ab, BackColor ?? theme.PanelBackground);
        renderer.DrawRect(sb, ab, ForeColor ?? theme.PanelBorder, 1);

        bool active = _padTargeted >= 0.5;

        if (!active)
        {
            renderer.DrawTextCentred(sb, "NO PAD", ab,
                theme.Font, theme.SmallScale * 0.8f, theme.TextDisabled);
            return;
        }

        // ── Layout ────────────────────────────────────────────────────────────

        int   pad        = 10;
        float cx         = ab.X + ab.Width  * 0.5f;
        float cy         = ab.Y + ab.Height * 0.5f - 14;  // shift up slightly for label row
        float areaR      = MathF.Min(ab.Width, ab.Height) * 0.5f - pad;

        // ── Geometry ─────────────────────────────────────────────────────────

        // Pad circle: fixed reference, radius = 35% of available area
        float padR = areaR * 0.35f;

        // Max lateral range shown: 3× pad radius (beyond this ship is clamped to edge)
        float lateralScale  = padR * 3f;   // metres per padR
        float maxLateralM   = lateralScale; // ±lateralScale metres = ±padR pixels
        float pixPerMetre   = padR / maxLateralM;

        // Ship circle:
        //   - Matches padR when landed (height = 0)
        //   - Grows linearly with altitude up to MaxTrackingHeightM, then clamps
        //   - Negative heights (wrong side of pad) clamp to 0 → circle stays at padR
        const float MaxTrackingHeightM = 150f;
        float heightM = Math.Clamp((float)_heightAbovePad, 0f, MaxTrackingHeightM);
        float shipR   = padR * (1f + heightM / MaxTrackingHeightM);
        shipR         = MathF.Min(shipR, areaR * 0.85f);

        // Pitch deformation: 0° → shipR (round), 90° → 0 (line)
        float pitchRad     = (float)(_pitchDev * System.Math.PI / 180.0);
        float shipRY       = shipR * MathF.Cos(pitchRad);   // vertical semi-axis
        shipRY             = MathF.Max(shipRY, 1.5f);        // never fully disappears

        // Lateral/longitudinal offsets → ship circle displacement (clamped to visible area)
        float edgeClamp        = areaR - shipR * 0.5f;
        float lateralPx        = (float)System.Math.Clamp(_lateralOffset      * pixPerMetre, -edgeClamp, edgeClamp);
        float longitudinalPx   = (float)System.Math.Clamp(_longitudinalOffset * pixPerMetre, -edgeClamp, edgeClamp);

        // Heading angle: 0 = nose up (toward pad forward), clockwise positive
        float headingRad   = (float)(_headingDev * System.Math.PI / 180.0);

        bool  upsideDown   = _upsideDown >= 0.5;
        bool  isLarge      = _padSizeClass >= 0.5;

        // ── Colours ───────────────────────────────────────────────────────────

        var colPad        = new Color(80, 200, 130);          // pad circle: green
        var colPadDim     = new Color(35, 90, 55);            // pad crosshair arms
        var colShip       = upsideDown
            ? new Color(90, 90, 90)                           // grey when inverted
            : new Color(200, 220, 255);                       // ship circle: cool white
        var colShipArrow  = upsideDown ? colShip : new Color(255, 210, 80); // amber nose arrow
        var colText       = theme.TextNormal;
        var colDim        = theme.TextDisabled;

        // ── Draw pad circle + crosshair ───────────────────────────────────────

        DrawEllipse(sb, renderer, new Vector2(cx, cy), padR, padR, colPad, 48);

        // Pad crosshair: thin arms, arrows on both top and bottom
        float armLen = padR + 8f;
        DrawCrosshair(sb, renderer, new Vector2(cx, cy), armLen, 0f, colPadDim, arrowBothEnds: true, arrowColor: colPad);

        // Pad size label (tiny, inside pad circle)
        string sizeLabel = isLarge ? "L" : "S";
        renderer.DrawTextCentred(sb, sizeLabel,
            new Rectangle((int)(cx - 10), (int)(cy - 8), 20, 16),
            theme.Font, theme.SmallScale * 0.65f, colPadDim);

        // ── Draw ship circle + crosshair (only position if not upside down) ───

        float shipCx = upsideDown ? cx : cx + lateralPx;
        float shipCy = upsideDown ? cy : cy + longitudinalPx;  // fwd = up on screen; positive offset = behind pad = below centre

        DrawEllipse(sb, renderer, new Vector2(shipCx, shipCy), shipR, shipRY, colShip, 48);
        DrawCrosshair(sb, renderer, new Vector2(shipCx, shipCy),
            MathF.Max(shipR, shipRY) + 6f, headingRad,
            colShip, arrowBothEnds: false, arrowColor: colShipArrow);

        // ── Labels ────────────────────────────────────────────────────────────

        int labelY = ab.Bottom - pad - 14;
        float ts   = theme.SmallScale * 0.75f;

        string distText = _padDistance < 1000.0
            ? $"{_padDistance:F0}m"
            : $"{_padDistance / 1000.0:F1}km";

        string hdgText  = $"HDG {_headingDev:F0}°";
        string htText   = $"H {_heightAbovePad:F0}m";

        renderer.DrawText(sb, distText, new Vector2(ab.X + pad,       labelY), theme.Font, ts, colText);
        renderer.DrawText(sb, hdgText,  new Vector2(ab.X + ab.Width / 2 - 20, labelY), theme.Font, ts, colText);
        renderer.DrawText(sb, htText,   new Vector2(ab.Right - pad - 60, labelY), theme.Font, ts, colText);

        if (upsideDown)
        {
            renderer.DrawTextCentred(sb, "INVERTED",
                new Rectangle(ab.X, labelY - 16, ab.Width, 16),
                theme.Font, ts, new Color(255, 80, 60));
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void DrawEllipse(SpriteBatch sb, UIRenderer renderer,
        Vector2 centre, float rx, float ry, Color color, int segments)
    {
        float step = MathF.PI * 2f / segments;
        for (int i = 0; i < segments; i++)
        {
            float a0 = i * step, a1 = (i + 1) * step;
            renderer.DrawLine(sb,
                centre + new Vector2(MathF.Cos(a0) * rx, MathF.Sin(a0) * ry),
                centre + new Vector2(MathF.Cos(a1) * rx, MathF.Sin(a1) * ry),
                color);
        }
    }

    // Draws a crosshair centred at 'centre', arm length 'armLen', rotated by 'angle'.
    // arrowBothEnds: pads have arrows on both top and bottom arms; ship has only top.
    private static void DrawCrosshair(SpriteBatch sb, UIRenderer renderer,
        Vector2 centre, float armLen, float angle, Color lineColor,
        bool arrowBothEnds, Color arrowColor)
    {
        // Four arm tips (up, right, down, left relative to angle)
        var up    = AngleDir(angle);
        var right = AngleDir(angle + MathF.PI * 0.5f);

        // centre + up*armLen → tip is at screen-UP direction (Y negative) when angle=0
        Vector2 tipU = centre + up    * armLen;
        Vector2 tipD = centre - up    * armLen;
        Vector2 tipR = centre + right * armLen;
        Vector2 tipL = centre - right * armLen;

        renderer.DrawLine(sb, tipU, tipD, lineColor);
        renderer.DrawLine(sb, tipL, tipR, lineColor);

        // Arrows (small chevrons at arm tips)
        DrawArrow(sb, renderer, centre, tipU, arrowColor, 6f);
        if (arrowBothEnds)
            DrawArrow(sb, renderer, centre, tipD, arrowColor, 6f);
    }

    // Draws a small arrowhead at 'tip' pointing away from 'origin'
    private static void DrawArrow(SpriteBatch sb, UIRenderer renderer,
        Vector2 origin, Vector2 tip, Color color, float size)
    {
        Vector2 dir  = Vector2.Normalize(tip - origin);
        Vector2 perp = new Vector2(-dir.Y, dir.X);

        Vector2 base1 = tip - dir * size + perp * (size * 0.5f);
        Vector2 base2 = tip - dir * size - perp * (size * 0.5f);

        renderer.DrawLine(sb, tip, base1, color);
        renderer.DrawLine(sb, tip, base2, color);
    }

    private static Vector2 AngleDir(float angle)
        => new Vector2(MathF.Sin(angle), -MathF.Cos(angle));
}
