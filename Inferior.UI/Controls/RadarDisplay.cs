using Inferior.Core.DataBus;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.UI.Controls;

/// <summary>Radar exclusion zone (jammer, EMP, blocking body). Rendering path only — not wired.</summary>
public readonly record struct RadarExclusionZone(Vector3 WorldPosition, float Radius, string Label);

/// <summary>
/// Top-down oval radar showing contacts relative to the ship.
/// Ship forward maps to the top of the oval.
/// The oval represents the ship's horizontal plane seen from above at a perspective angle;
/// vertical axis is compressed by PerspectiveFactor ≈ 0.65.
///
/// Five hardcoded range steps (500 m → 100 km); player clicks range label to cycle.
/// Layer toggles (ELEV / TEXT / OOB / RINGS) and LOG mode handled internally.
/// Updated each frame by the game state via data properties.
/// </summary>
public sealed class RadarDisplay : Control
{
    // ── Range steps ───────────────────────────────────────────────────────────
    private static readonly float[] RangeSteps = [500f, 2_000f, 10_000f, 30_000f, 100_000f];
    private static readonly float[] LogKValues  = [5f,    20f,    80f,    300f,    1_000f];
    private int _rangeStep = 2;  // default = 10 km

    // ── Data — set each frame by game state ──────────────────────────────────
    public IEnumerable<RadarContact>?         Contacts          { get; set; }
    public RadarContact?                      SelectedContact   { get; set; }
    /// <summary>Ship speed relative to local frame (right bar). Log-scaled up to SpeedBarMaxMs.</summary>
    public float                              LocalFrameSpeedMs { get; set; }
    /// <summary>Approach speed relative to selected contact (left bar). Positive = closing.</summary>
    public float                              ApproachSpeedMs   { get; set; }
    public IReadOnlyList<RadarExclusionZone>? ExclusionZones    { get; set; }

    // ── Ship orientation — set each frame so contacts are projected onto ship-local axes ──
    /// <summary>Ship forward vector in world/galaxy space (normalised).</summary>
    public Vector3 ShipForward { get; set; } = -Vector3.UnitZ;
    /// <summary>Ship right vector in world/galaxy space (normalised).</summary>
    public Vector3 ShipRight   { get; set; } = Vector3.UnitX;
    /// <summary>Ship up vector in world/galaxy space (normalised).</summary>
    public Vector3 ShipUp      { get; set; } = Vector3.UnitY;

    // ── Speed bar tuning ─────────────────────────────────────────────────────
    private const float SpeedBarMaxMs = 500f;
    private const float LogK          = 0.025f;

    // ── LED states — set by game state ────────────────────────────────────────
    /// <summary>PWR: green when radar online.</summary>
    public bool PwrLed         { get; set; }
    /// <summary>HOT: amber when overtemp; blink red when HotLedCritical.</summary>
    public bool HotLed         { get; set; }
    public bool HotLedCritical { get; set; }
    /// <summary>SCAN: blink green when active scan running.</summary>
    public bool ScanLed        { get; set; }
    /// <summary>JAM: amber when jamming active.</summary>
    public bool JamLed         { get; set; }
    /// <summary>FULL: blink red when contact list full.</summary>
    public bool FullLed        { get; set; }

    // ── Layer toggles ─────────────────────────────────────────────────────────
    private bool _showElev  = true;
    private bool _showText  = false;
    private bool _showOob   = true;
    private bool _showRings = true;
    private bool _logMode   = false;

    // ── Animation ─────────────────────────────────────────────────────────────
    private double _time;

    // ── Layout ────────────────────────────────────────────────────────────────
    private const int HeaderH     = 17;   // range label + LOG button
    private const int TogglesH    = 15;   // ELEV / TEXT / OOB / RINGS checkboxes
    private const int LedRowH     = 22;   // PWR … FULL LEDs
    private const int SpeedBarW   = 13;
    private const int SpeedBarGap = 4;
    private const int OobMarginPx = 13;   // extra pixels from disc edge to OOB ring
    private const int DiscInnerPad = 3;   // pixels between OOB ring and main-area edge

    // ── Input ─────────────────────────────────────────────────────────────────

    public override void Update(double dt)
    {
        _time += dt;
        base.Update(dt);
    }

    public override bool HandleInput(InputState input)
    {
        if (!Visible || !Enabled) return false;
        if (!input.LeftReleased) return base.HandleInput(input);

        if (RangeClickRect().Contains(input.MousePosition))
        {
            _rangeStep = (_rangeStep + 1) % RangeSteps.Length;
            return true;
        }
        if (LogRect().Contains(input.MousePosition))
        {
            _logMode = !_logMode;
            return true;
        }
        for (int i = 0; i < 4; i++)
        {
            if (ToggleRect(i).Contains(input.MousePosition))
            {
                CycleToggle(i);
                return true;
            }
        }
        return base.HandleInput(input);
    }

    private void CycleToggle(int i)
    {
        switch (i)
        {
            case 0: _showElev  = !_showElev;  break;
            case 1: _showText  = !_showText;  break;
            case 2: _showOob   = !_showOob;   break;
            case 3: _showRings = !_showRings; break;
        }
    }

    // ── Draw ──────────────────────────────────────────────────────────────────

    public override void Draw(SpriteBatch sb, UIRenderer renderer, Theme theme)
    {
        if (!Visible) return;

        var ab = AbsoluteBounds;
        renderer.FillRect(sb, ab, BackColor ?? theme.PanelBackground);
        renderer.DrawRect(sb, ab, ForeColor ?? theme.PanelBorder, 1);

        float range = RangeSteps[_rangeStep];

        DrawHeader(sb, renderer, theme, ab, range);
        DrawToggles(sb, renderer, theme, ab);
        DrawLeds(sb, renderer, theme, ab);

        // Main area (between toggle row and LED row)
        int mainTop = ab.Y + HeaderH + TogglesH;
        int mainH   = ab.Height - HeaderH - TogglesH - LedRowH;

        // Speed bar positions
        int barLeft  = ab.X + 1;
        int barRight = ab.Right - 1 - SpeedBarW;

        // Disc centre and radii — height is the constraining dimension
        int discLeft = barLeft  + SpeedBarW + SpeedBarGap;
        int discW    = barRight - SpeedBarGap - discLeft;
        int cx       = discLeft + discW / 2;
        int cy       = mainTop  + mainH  / 2;
        const float PerspectiveFactor = 0.65f;
        int oobRy    = Math.Max(1, mainH / 2 - DiscInnerPad);
        int oobRx    = Math.Max(1, (int)(oobRy / PerspectiveFactor));
        int ry       = Math.Max(1, oobRy - OobMarginPx);
        int rx       = Math.Max(1, (int)(ry / PerspectiveFactor));

        DrawDisc(sb, renderer, theme, cx, cy, rx, ry, oobRx, oobRy, range);
        DrawSpeedBars(sb, renderer, barLeft, barRight, mainTop, mainH);
    }

    // ── Header ────────────────────────────────────────────────────────────────

    private void DrawHeader(SpriteBatch sb, UIRenderer renderer, Theme theme,
        Rectangle ab, float range)
    {
        // Range label (clickable — cycles through steps)
        string rangeText = $"RADAR  {FormatRange(range)}";
        renderer.DrawText(sb, rangeText,
            new Vector2(ab.X + 4, ab.Y + (HeaderH - 10) * 0.5f),
            theme.Font, theme.SmallScale * 0.74f, new Color(90, 160, 90));

        // LOG toggle button
        var lr  = LogRect();
        renderer.FillRect(sb, lr, _logMode ? new Color(20, 50, 20) : new Color(10, 22, 10));
        renderer.DrawRect(sb, lr, new Color(40, 80, 40), 1);
        renderer.DrawTextCentred(sb, "LOG", lr, theme.Font, theme.SmallScale * 0.64f,
            _logMode ? new Color(100, 200, 100) : new Color(45, 75, 45));
    }

    private static string FormatRange(float metres) => metres switch
    {
        < 1_000f   => $"{metres:F0} m",
        < 100_000f => $"{metres / 1000f:F1} km",
        _          => $"{metres / 1000f:F0} km",
    };

    // ── Toggle row ────────────────────────────────────────────────────────────

    private static readonly string[] ToggleLabels = ["ELEV", "TEXT", "OOB", "RINGS"];

    private void DrawToggles(SpriteBatch sb, UIRenderer renderer, Theme theme, Rectangle ab)
    {
        ReadOnlySpan<bool> states = [_showElev, _showText, _showOob, _showRings];
        for (int i = 0; i < 4; i++)
        {
            var  r    = ToggleRect(i);
            bool on   = states[i];
            int  csz  = TogglesH - 5;
            var  cbox = new Rectangle(r.X + 3, r.Y + (TogglesH - csz) / 2, csz, csz);

            renderer.DrawRect(sb, cbox, new Color(45, 90, 45), 1);
            if (on)
                renderer.FillRect(sb,
                    new Rectangle(cbox.X + 2, cbox.Y + 2, cbox.Width - 4, cbox.Height - 4),
                    new Color(70, 190, 70));

            var labelArea = new Rectangle(cbox.Right + 2, r.Y, r.Right - cbox.Right - 2, TogglesH);
            renderer.DrawTextLeft(sb, ToggleLabels[i], labelArea,
                theme.Font, theme.SmallScale * 0.64f,
                on ? new Color(110, 190, 110) : new Color(50, 75, 50));
        }
    }

    // ── LED row ───────────────────────────────────────────────────────────────

    private static readonly string[] LedLabels = ["PWR", "HOT", "SCAN", "JAM", "FULL"];

    private void DrawLeds(SpriteBatch sb, UIRenderer renderer, Theme theme, Rectangle ab)
    {
        bool blink  = (_time % 0.5) > 0.25;
        int  ledY   = ab.Bottom - LedRowH;
        int  ledW   = ab.Width / 5;
        int  dotSz  = 8;
        int  dotYOff = (LedRowH - dotSz - 10) / 2;

        for (int i = 0; i < 5; i++)
        {
            int     lx        = ab.X + i * ledW;
            var     (col, on) = LedState(i, blink);
            Color   dotCol    = on ? col : new Color(18, 24, 18);
            Color   labelCol  = on ? col : new Color(38, 48, 38);

            var dot = new Rectangle(lx + (ledW - dotSz) / 2, ledY + dotYOff, dotSz, dotSz);
            renderer.FillRect(sb, dot, dotCol);
            renderer.DrawRect(sb, dot, new Color(38, 52, 38), 1);

            var labelArea = new Rectangle(lx, dot.Bottom + 1, ledW, LedRowH - dotYOff - dotSz - 2);
            renderer.DrawTextCentred(sb, LedLabels[i], labelArea,
                theme.Font, theme.SmallScale * 0.60f, labelCol);
        }
    }

    private (Color col, bool on) LedState(int index, bool blink) => index switch
    {
        0 => (new Color( 80, 220,  80), PwrLed),
        1 => HotLedCritical
               ? (new Color(220,  50,  50), blink)
               : (new Color(200, 160,  40), HotLed),
        2 => (new Color( 80, 220,  80), ScanLed && blink),
        3 => (new Color(200, 160,  40), JamLed),
        4 => (new Color(220,  50,  50), FullLed && blink),
        _ => (Color.Transparent, false),
    };

    // ── Disc ──────────────────────────────────────────────────────────────────

    private void DrawDisc(SpriteBatch sb, UIRenderer renderer, Theme theme,
        int cx, int cy, int rx, int ry, int oobRx, int oobRy, float range)
    {
        // Filled oval (scanlines)
        var discFill = new Color(6, 16, 8);
        for (int dy = -ry; dy <= ry; dy++)
        {
            float t     = (float)dy / ry;
            int   xhalf = (int)(rx * MathF.Sqrt(MathF.Max(0f, 1f - t * t)));
            if (xhalf < 1) continue;
            renderer.FillRect(sb, new Rectangle(cx - xhalf, cy + dy, xhalf * 2, 1), discFill);
        }

        // OOB ring
        if (_showOob)
            DrawEllipse(sb, renderer, cx, cy, oobRx, oobRy, new Color(22, 38, 80));

        // Disc edge
        DrawEllipse(sb, renderer, cx, cy, rx, ry, new Color(28, 85, 38));

        // Range rings
        if (_showRings)
        {
            var ringCol = new Color(40, 120, 50);
            for (int ring = 1; ring <= 3; ring++)
                DrawEllipse(sb, renderer, cx, cy, rx * ring / 4f, ry * ring / 4f, ringCol);
        }

        // Forward tick
        renderer.DrawLine(sb, new Vector2(cx, cy - ry), new Vector2(cx, cy - ry + 7),
            new Color(45, 140, 55));

        // Ship marker (small up-triangle at centre)
        DrawTriangle(sb, renderer, cx, cy, 4.5f, filled: true, new Color(55, 175, 75));

        // Exclusion zones
        if (ExclusionZones != null)
            foreach (var zone in ExclusionZones)
                DrawExclusionZone(sb, renderer, zone, cx, cy, rx, ry, oobRx, oobRy, range);

        // Contacts
        if (Contacts != null)
            foreach (var c in Contacts)
                DrawContact(sb, renderer, theme, c, cx, cy, rx, ry, oobRx, oobRy, range);
    }

    // ── Contact drawing ───────────────────────────────────────────────────────

    private void DrawContact(SpriteBatch sb, UIRenderer renderer, Theme theme,
        RadarContact c, int cx, int cy, int rx, int ry, int oobRx, int oobRy, float range)
    {
        // Project contact's relative position onto ship-local axes.
        // RelativePosition is in world/galaxy space; ShipForward/Right/Up describe
        // the ship's orientation in that same space.
        var   rel     = c.RelativePosition;
        float rightC  = Vector3.Dot(rel, ShipRight);    // +right  → disc right
        float fwdC    = Vector3.Dot(rel, ShipForward);  // +forward → disc top
        float upC     = Vector3.Dot(rel, ShipUp);       // +up     → elevation bar upward

        float horiz = MathF.Sqrt(rightC * rightC + fwdC * fwdC);

        // Bearing unit vector in ship-local horizontal plane
        float bRight = horiz > 0f ? rightC / horiz : 0f;
        float bFwd   = horiz > 0f ? fwdC   / horiz : 1f;

        bool isOob    = horiz > range;
        bool selected = SelectedContact.HasValue && SelectedContact.Value.Id == c.Id;
        Color col     = selected ? new Color(255, 240, 60) : ContactColor(c);

        if (isOob)
        {
            if (!_showOob) return;
            // Place on OOB ring at correct bearing (forward = disc top = negative screen-Y)
            renderer.DrawDot(sb,
                new Vector2(cx + bRight * oobRx, cy - bFwd * oobRy),
                1.8f, col);
            return;
        }

        // Map to disc
        float normDist = horiz / range;
        float frac     = _logMode
            ? MathF.Log(1f + normDist * LogKValues[_rangeStep])
              / MathF.Log(1f + LogKValues[_rangeStep])
            : normDist;
        frac = MathF.Min(1f, frac);

        float px = cx + bRight * frac * rx;
        float py = cy - bFwd   * frac * ry;  // forward = up on disc = negative screen-Y

        // Elevation bar
        if (_showElev)
        {
            float dist = rel.Length();
            if (dist > 0.1f)
            {
                float elevSin = upC / dist;
                float barLen  = ry * elevSin * 0.55f;
                if (MathF.Abs(barLen) > 1f)
                    renderer.DrawLine(sb,
                        new Vector2(px, py),
                        new Vector2(px, py - barLen),
                        col, 1f);
            }
        }

        DrawMarker(sb, renderer, c, px, py, col);

        // Text label
        if (_showText)
        {
            renderer.DrawText(sb, c.DisplayName,
                new Vector2(px + 7, py - 5),
                theme.Font, theme.SmallScale * 0.60f,
                new Color(130, 150, 130));
        }
    }

    private static Color ContactColor(RadarContact c) => c.Type switch
    {
        ContactType.Station => new Color( 80, 200, 140),  // teal — neutral
        ContactType.Missile => new Color(220,  55,  55),  // red  — hostile
        ContactType.Ship    => new Color(120, 145, 215),  // blue — unknown
        ContactType.Asteroid
        or ContactType.Debris
                            => new Color(100, 105, 110),  // grey — small object
        _                   => new Color( 90,  95, 100),
    };

    private static void DrawMarker(SpriteBatch sb, UIRenderer renderer,
        RadarContact c, float px, float py, Color col)
    {
        switch (c.Type)
        {
            case ContactType.Station:
                DrawDiamond(sb, renderer, px, py, 5f, col);
                break;
            case ContactType.Missile:
                DrawTriangle(sb, renderer, (int)px, (int)py, 4f, filled: true, col);
                break;
            case ContactType.Ship:
                DrawTriangle(sb, renderer, (int)px, (int)py, 5f, filled: false, col);
                break;
            case ContactType.Asteroid:
            case ContactType.Debris:
                DrawDash(sb, renderer, px, py, col);
                break;
            default:
                DrawHollowCircle(sb, renderer, px, py, 3.5f, col);
                break;
        }
    }

    // ── Exclusion zones ───────────────────────────────────────────────────────

    private void DrawExclusionZone(SpriteBatch sb, UIRenderer renderer,
        RadarExclusionZone zone, int cx, int cy, int rx, int ry,
        int oobRx, int oobRy, float range)
    {
        var   rel    = zone.WorldPosition;
        float rightC = Vector3.Dot(rel, ShipRight);
        float fwdC   = Vector3.Dot(rel, ShipForward);
        float horiz  = MathF.Sqrt(rightC * rightC + fwdC * fwdC);
        float bRight = horiz > 0f ? rightC / horiz : 0f;
        float bFwd   = horiz > 0f ? fwdC   / horiz : 1f;
        float frac   = MathF.Min(1f, horiz / range);
        float px     = cx + bRight * frac * rx;
        float py     = cy - bFwd   * frac * ry;
        float zr    = MathF.Max(8f, MathF.Min(zone.Radius / range * rx, rx * 0.5f));

        // Dark filled circle
        var fill = new Color(38, 8, 8);
        for (int dy = -(int)zr; dy <= (int)zr; dy++)
        {
            float xhalf = MathF.Sqrt(MathF.Max(0f, zr * zr - dy * dy));
            if (xhalf < 1f) continue;
            renderer.FillRect(sb, new Rectangle((int)(px - xhalf), (int)(py + dy), (int)(xhalf * 2), 1), fill);
        }
        DrawCircle(sb, renderer, (int)px, (int)py, zr, new Color(100, 28, 28), 20);
    }

    // ── Speed bars ────────────────────────────────────────────────────────────

    private static float LogBarFrac(float ms)
    {
        float abs  = MathF.Abs(ms);
        float denom = MathF.Log(1f + SpeedBarMaxMs * LogK);
        return denom > 0f
            ? Math.Clamp(MathF.Log(1f + abs * LogK) / denom, 0f, 1f)
            : 0f;
    }

    private void DrawSpeedBars(SpriteBatch sb, UIRenderer renderer,
        int barLeft, int barRight, int mainTop, int mainH)
    {
        // Left bar — bidirectional approach speed (log-scaled)
        // Positive = closing (below centre), negative = opening (above centre)
        float signedFrac = ApproachSpeedMs >= 0f
            ? LogBarFrac(ApproachSpeedMs)
            : -LogBarFrac(ApproachSpeedMs);

        DrawBiBar(sb, renderer,
            barLeft, mainTop, SpeedBarW, mainH,
            signedFrac,
            new Color(28, 65, 160),   // opening colour (above centre)
            new Color(70, 165, 245)); // closing colour (below centre)

        // Right bar — local frame speed (unidirectional, log-scaled)
        DrawUniBar(sb, renderer,
            barRight, mainTop, SpeedBarW, mainH,
            LogBarFrac(LocalFrameSpeedMs),
            new Color(28, 105, 38),
            new Color(195, 175, 45));
    }

    private static void DrawBiBar(SpriteBatch sb, UIRenderer renderer,
        int x, int y, int w, int h, float fraction,
        Color openCol, Color closeCol)
    {
        renderer.FillRect(sb, new Rectangle(x, y, w, h), new Color(8, 14, 8));
        int midY = y + h / 2;
        renderer.FillRect(sb, new Rectangle(x, midY, w, 1), new Color(28, 55, 28));

        float clamped = Math.Clamp(fraction, -1f, 1f);
        if (MathF.Abs(clamped) < 0.015f) return;

        if (clamped > 0f)
        {
            int fillH = Math.Max(1, (int)(clamped * (h / 2f)));
            renderer.FillRect(sb, new Rectangle(x, midY + 1, w, fillH), closeCol);
        }
        else
        {
            int fillH = Math.Max(1, (int)(-clamped * (h / 2f)));
            renderer.FillRect(sb, new Rectangle(x, midY - fillH, w, fillH), openCol);
        }
    }

    private static void DrawUniBar(SpriteBatch sb, UIRenderer renderer,
        int x, int y, int w, int h, float fraction,
        Color dimCol, Color litCol)
    {
        renderer.FillRect(sb, new Rectangle(x, y, w, h), new Color(8, 14, 8));
        if (fraction < 0.01f) return;
        int fillH = Math.Max(1, (int)(fraction * h));
        renderer.FillRect(sb, new Rectangle(x, y + h - fillH, w, fillH),
            Color.Lerp(dimCol, litCol, fraction));
    }

    // ── Primitive helpers ─────────────────────────────────────────────────────

    private static void DrawDiamond(SpriteBatch sb, UIRenderer renderer,
        float cx, float cy, float r, Color col)
    {
        renderer.DrawLine(sb, new Vector2(cx,     cy - r), new Vector2(cx + r, cy    ), col);
        renderer.DrawLine(sb, new Vector2(cx + r, cy    ), new Vector2(cx,     cy + r), col);
        renderer.DrawLine(sb, new Vector2(cx,     cy + r), new Vector2(cx - r, cy    ), col);
        renderer.DrawLine(sb, new Vector2(cx - r, cy    ), new Vector2(cx,     cy - r), col);
    }

    private static void DrawTriangle(SpriteBatch sb, UIRenderer renderer,
        int cx, int cy, float r, bool filled, Color col)
    {
        var tip   = new Vector2(cx,              cy - r);
        var left  = new Vector2(cx - r * 0.82f,  cy + r * 0.7f);
        var right = new Vector2(cx + r * 0.82f,  cy + r * 0.7f);
        renderer.DrawLine(sb, tip,   left,  col);
        renderer.DrawLine(sb, left,  right, col);
        renderer.DrawLine(sb, right, tip,   col);
        if (filled)
            renderer.DrawDot(sb, new Vector2(cx, cy + r * 0.1f), r * 0.38f, col);
    }

    private static void DrawDash(SpriteBatch sb, UIRenderer renderer,
        float cx, float cy, Color col)
        => renderer.DrawLine(sb, new Vector2(cx - 4f, cy), new Vector2(cx + 4f, cy), col, 2f);

    private static void DrawHollowCircle(SpriteBatch sb, UIRenderer renderer,
        float cx, float cy, float r, Color col)
    {
        const int N = 10;
        for (int i = 0; i < N; i++)
        {
            float a0 = i       / (float)N * MathF.Tau;
            float a1 = (i + 1) / (float)N * MathF.Tau;
            renderer.DrawLine(sb,
                new Vector2(cx + r * MathF.Cos(a0), cy + r * MathF.Sin(a0)),
                new Vector2(cx + r * MathF.Cos(a1), cy + r * MathF.Sin(a1)),
                col);
        }
    }

    private static void DrawEllipse(SpriteBatch sb, UIRenderer renderer,
        int cx, int cy, float rx, float ry, Color col)
    {
        const int N = 40;
        Vector2   prev = new(cx + rx * MathF.Sin(0f), cy - ry * MathF.Cos(0f));
        for (int i = 1; i <= N; i++)
        {
            float   a    = i / (float)N * MathF.Tau;
            Vector2 curr = new(cx + rx * MathF.Sin(a), cy - ry * MathF.Cos(a));
            renderer.DrawLine(sb, prev, curr, col);
            prev = curr;
        }
    }

    private static void DrawCircle(SpriteBatch sb, UIRenderer renderer,
        int cx, int cy, float r, Color col, int segments)
    {
        Vector2 prev = new(cx + r, cy);
        for (int i = 1; i <= segments; i++)
        {
            float   a    = i / (float)segments * MathF.Tau;
            Vector2 curr = new(cx + r * MathF.Cos(a), cy + r * MathF.Sin(a));
            renderer.DrawLine(sb, prev, curr, col);
            prev = curr;
        }
    }

    // ── Layout helpers ────────────────────────────────────────────────────────

    private Rectangle RangeClickRect()
    {
        var ab = AbsoluteBounds;
        return new Rectangle(ab.X + 2, ab.Y + 1, 80, HeaderH - 2);
    }

    private Rectangle LogRect()
    {
        var ab = AbsoluteBounds;
        return new Rectangle(ab.X + 84, ab.Y + 2, 30, HeaderH - 4);
    }

    private Rectangle ToggleRect(int i)
    {
        var ab = AbsoluteBounds;
        int tw = ab.Width / 4;
        return new Rectangle(ab.X + i * tw, ab.Y + HeaderH, tw, TogglesH);
    }
}
