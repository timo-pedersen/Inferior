using Inferior.Core.DataBus;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.UI.Controls;

/// <summary>
/// Top-down oval radar showing nearby contacts relative to the ship.
/// Forward direction (-Z) maps to the top of the oval.
///
/// Set Contacts, SelectedContact, LocalFrameSpeedMs, and MaxRangeMeters each
/// frame from the game state.
/// </summary>
public sealed class RadarDisplay : Control
{
    // ── Data — set each frame by the game state ───────────────────────────────

    public IEnumerable<RadarContact>? Contacts         { get; set; }
    public RadarContact?              SelectedContact   { get; set; }
    public float                      LocalFrameSpeedMs { get; set; }
    public float                      MaxSpeedMs        { get; set; } = 5e9f;
    public float                      MaxRangeMeters    { get; set; } = 3e12f;

    // ── Constants ─────────────────────────────────────────────────────────────

    private const int   SpeedBarW        = 12;
    private const int   SpeedBarGap      = 5;
    private const int   TitleH           = 16;
    private const int   RingCount        = 3;
    private const int   RingSegs         = 40;
    private const int   SpeedBarSegCount = 8;
    private const int   SpeedBarSegGap   = 1;
    private const float MaxClosingRateMs = 5e8f;   // 500 Mm/s full-scale

    // ── Draw ──────────────────────────────────────────────────────────────────

    public override void Draw(SpriteBatch sb, UIRenderer renderer, Theme theme)
    {
        if (!Visible) return;

        var ab = AbsoluteBounds;

        renderer.FillRect(sb, ab, BackColor ?? theme.PanelBackground);
        renderer.DrawRect(sb, ab, ForeColor ?? theme.PanelBorder, 1);

        // Title bar
        string rangeText = MaxRangeMeters >= 1e9f
            ? $"RADAR  {MaxRangeMeters / 1e9f:F1} Gm"
            : $"RADAR  {MaxRangeMeters / 1e6f:F0} Mm";
        renderer.DrawText(sb, rangeText,
            new Vector2(ab.X + 4, ab.Y + (TitleH - 10) * 0.5f),
            theme.Font, theme.SmallScale * 0.72f, theme.TextDisabled);

        // Speed bar column bounds
        int barLeft  = ab.X + 1;
        int barRight = ab.Right - 1 - SpeedBarW;
        int barTop   = ab.Y + TitleH;
        int barH     = ab.Height - TitleH - 1;

        // Disc area between the two speed bar columns
        int discLeft = barLeft  + SpeedBarW + SpeedBarGap;
        int discW    = barRight - SpeedBarGap - discLeft;
        int cx       = discLeft + discW / 2;
        int cy       = barTop   + barH  / 2;
        int ry       = Math.Max(1, barH  / 2 - 4);
        int rx       = Math.Max(1, (int)(ry / 0.866f));  // foreshortened horizontal axis

        // ── Oval disc (horizontal scanlines) ──────────────────────────────────
        var discColor = new Color(10, 28, 14);
        for (int dy = -ry; dy <= ry; dy++)
        {
            float t     = (float)dy / ry;
            int   xhalf = (int)(rx * MathF.Sqrt(MathF.Max(0f, 1f - t * t)));
            if (xhalf < 1) continue;
            renderer.FillRect(sb, new Rectangle(cx - xhalf, cy + dy, xhalf * 2, 1), discColor);
        }

        // ── Range rings (polyline ellipses) ───────────────────────────────────
        var ringColor = new Color(20, 55, 25);
        for (int ring = 1; ring <= RingCount; ring++)
        {
            float f = (float)ring / (RingCount + 1);
            DrawEllipseRing(sb, renderer, cx, cy, rx * f, ry * f, ringColor);
        }

        // Forward direction tick at top of disc
        renderer.DrawLine(sb,
            new Vector2(cx, cy - ry),
            new Vector2(cx, cy - ry + 7),
            new Color(40, 120, 50));

        // Centre crosshair
        renderer.DrawLine(sb, new Vector2(cx - 4, cy), new Vector2(cx + 4, cy), new Color(30, 80, 40));
        renderer.DrawLine(sb, new Vector2(cx, cy - 4), new Vector2(cx, cy + 4), new Color(30, 80, 40));

        // ── Contacts ──────────────────────────────────────────────────────────
        if (Contacts != null)
        {
            foreach (var c in Contacts)
                DrawContact(sb, renderer, c, cx, cy, rx, ry);
        }

        // ── Left speed bar — closing rate on selected target ──────────────────
        float closingRate = 0f;
        if (SelectedContact.HasValue)
        {
            var sc  = SelectedContact.Value;
            float rlen = sc.RelativePosition.Length();
            if (rlen > 1f)
                closingRate = -Vector3.Dot(sc.RelativeVelocity, sc.RelativePosition / rlen);
        }

        DrawSpeedBar(sb, renderer,
            barLeft, barTop, SpeedBarW, barH,
            Math.Clamp(closingRate / MaxClosingRateMs, 0f, 1f),
            new Color(40, 80, 180), new Color(80, 180, 255));

        // ── Right speed bar — local frame speed ───────────────────────────────
        float speedFraction = MaxSpeedMs > 0f
            ? Math.Clamp(LocalFrameSpeedMs / MaxSpeedMs, 0f, 1f)
            : 0f;

        DrawSpeedBar(sb, renderer,
            barRight, barTop, SpeedBarW, barH,
            speedFraction,
            new Color(30, 110, 40), new Color(200, 180, 50));
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private void DrawContact(SpriteBatch sb, UIRenderer renderer,
        RadarContact contact, int cx, int cy, int rx, int ry)
    {
        // Map: forward=-Z → top; positive X → right; positive Z → bottom
        float dx = contact.RelativePosition.X / MaxRangeMeters * rx;
        float dy = contact.RelativePosition.Z / MaxRangeMeters * ry;

        // Clamp to ellipse edge if out of range
        float en = dx * dx / (float)(rx * rx) + dy * dy / (float)(ry * ry);
        if (en > 1f)
        {
            float s = 1f / MathF.Sqrt(en);
            dx *= s; dy *= s;
        }

        float px = cx + dx;
        float py = cy + dy;

        bool isSelected = SelectedContact.HasValue && SelectedContact.Value.Id == contact.Id;

        Color col = contact.Type switch
        {
            ContactType.Station => new Color(80,  200, 140),
            ContactType.Ship    => new Color(220,  80,  80),
            ContactType.Missile => new Color(255,  60,  60),
            _                   => new Color(140, 140, 140),
        };
        if (isSelected) col = new Color(255, 240, 60);

        switch (contact.Type)
        {
            case ContactType.Station:
                DrawDiamond(sb, renderer, px, py, 5f, col);
                break;
            case ContactType.Ship:
            case ContactType.Missile:
                DrawTriangle(sb, renderer, px, py, 5f, col);
                break;
            default:
                renderer.DrawDot(sb, new Vector2(px, py), 2.5f, col);
                break;
        }
    }

    private static void DrawDiamond(SpriteBatch sb, UIRenderer renderer,
        float cx, float cy, float r, Color col)
    {
        var top   = new Vector2(cx,     cy - r);
        var right = new Vector2(cx + r, cy    );
        var bot   = new Vector2(cx,     cy + r);
        var left  = new Vector2(cx - r, cy    );
        renderer.DrawLine(sb, top,   right, col);
        renderer.DrawLine(sb, right, bot,   col);
        renderer.DrawLine(sb, bot,   left,  col);
        renderer.DrawLine(sb, left,  top,   col);
    }

    private static void DrawTriangle(SpriteBatch sb, UIRenderer renderer,
        float cx, float cy, float r, Color col)
    {
        var tip   = new Vector2(cx,              cy - r);
        var left  = new Vector2(cx - r * 0.8f,  cy + r * 0.7f);
        var right = new Vector2(cx + r * 0.8f,  cy + r * 0.7f);
        renderer.DrawLine(sb, tip,   left,  col);
        renderer.DrawLine(sb, left,  right, col);
        renderer.DrawLine(sb, right, tip,   col);
    }

    private static void DrawEllipseRing(SpriteBatch sb, UIRenderer renderer,
        int cx, int cy, float rx, float ry, Color col)
    {
        Vector2 prev = new(cx + rx * MathF.Sin(0f), cy - ry * MathF.Cos(0f));
        for (int i = 1; i <= RingSegs; i++)
        {
            float    a    = i / (float)RingSegs * MathF.Tau;
            Vector2  curr = new(cx + rx * MathF.Sin(a), cy - ry * MathF.Cos(a));
            renderer.DrawLine(sb, prev, curr, col);
            prev = curr;
        }
    }

    private static void DrawSpeedBar(SpriteBatch sb, UIRenderer renderer,
        int x, int y, int w, int h,
        float fraction, Color dimColor, Color litColor)
    {
        renderer.FillRect(sb, new Rectangle(x, y, w, h), new Color(8, 15, 8));

        int segH = Math.Max(1,
            (h - (SpeedBarSegCount - 1) * SpeedBarSegGap) / SpeedBarSegCount);
        int lit  = (int)(fraction * SpeedBarSegCount);

        for (int i = 0; i < SpeedBarSegCount; i++)
        {
            int  sy    = y + h - segH - i * (segH + SpeedBarSegGap);
            bool isLit = i < lit;
            float t    = (float)i / Math.Max(1, SpeedBarSegCount - 1);
            Color col  = isLit ? Color.Lerp(dimColor, litColor, t) : new Color(15, 25, 15);
            renderer.FillRect(sb, new Rectangle(x, sy, w, segH), col);
        }
    }
}
