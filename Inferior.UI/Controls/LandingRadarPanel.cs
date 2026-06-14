using Inferior.Core.Math;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.UI.Controls;

/// <summary>
/// Top-down approach radar for landing pad alignment.
/// The station is at the radar centre; the ship dot shows relative position.
/// Updated each frame by the owning game state via the public data properties.
/// </summary>
public sealed class LandingRadarPanel : Control
{
    // ── Data — set each frame by the game state ───────────────────────────────

    public string StationName    { get; set; } = "";
    public bool   HasStation     { get; set; }
    public double DistanceMeters { get; set; }
    public float  RelX           { get; set; }   // ship offset from station, metres
    public float  RelZ           { get; set; }   // ship offset from station, metres

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>Fired when the player presses the release (X) button.</summary>
    public event Action? Released;

    // ── Constants ─────────────────────────────────────────────────────────────

    private const float RadarRangeMeters = 5_000f;   // full radar radius represents this distance
    private const float LockRangeMeters  =   500f;   // considered "locked" within this

    // ── Input ─────────────────────────────────────────────────────────────────

    public override bool HandleInput(InputState input)
    {
        if (!Visible || !Enabled || !HasStation) return false;

        if (ReleaseButtonRect().Contains(input.MousePosition) && input.LeftReleased)
        {
            Released?.Invoke();
            return true;
        }

        return base.HandleInput(input);
    }

    // ── Draw ──────────────────────────────────────────────────────────────────

    public override void Draw(SpriteBatch sb, UIRenderer renderer, Theme theme)
    {
        if (!Visible) return;

        var ab = AbsoluteBounds;

        renderer.FillRect(sb, ab, BackColor ?? theme.PanelBackground);
        renderer.DrawRect(sb, ab, ForeColor ?? theme.PanelBorder, 1);

        if (!HasStation)
        {
            renderer.DrawTextCentred(sb, "NO PAD SELECTED", ab,
                theme.Font, theme.SmallScale * 0.8f, theme.TextDisabled);
            return;
        }

        int pad        = 8;
        int radarSize  = Math.Min(ab.Width - pad * 2, (ab.Height - pad * 2) * 2 / 3);
        var radarCentre = new Vector2(ab.X + ab.Width * 0.5f, ab.Y + pad + radarSize * 0.5f);
        float radarR    = radarSize * 0.5f;

        // Radar rings
        var ringDim = new Color(20, 55, 35);
        var ringBrt = new Color(35, 80, 55);
        DrawCircle(sb, renderer, radarCentre, radarR,        ringBrt, 48);
        DrawCircle(sb, renderer, radarCentre, radarR * 0.5f, ringDim, 32);

        // Station dot at centre
        renderer.DrawDot(sb, radarCentre, 5f, new Color(100, 200, 140));

        // Ship dot — scale and clamp to radar disc
        float scale   = radarR / RadarRangeMeters;
        float clampR  = radarR - 3f;
        float sx      = Math.Clamp(RelX * scale, -clampR, clampR);
        float sz      = Math.Clamp(RelZ * scale, -clampR, clampR);
        bool  locked  = DistanceMeters < LockRangeMeters;
        var   shipCol = locked ? new Color(50, 255, 120) : new Color(180, 180, 255);
        renderer.DrawDot(sb, radarCentre + new Vector2(sx, sz), 3.5f, shipCol);

        // Range crosshair lines (subtle)
        var crossCol = ringDim;
        renderer.DrawLine(sb,
            new Vector2(radarCentre.X - radarR, radarCentre.Y),
            new Vector2(radarCentre.X + radarR, radarCentre.Y), crossCol);
        renderer.DrawLine(sb,
            new Vector2(radarCentre.X, radarCentre.Y - radarR),
            new Vector2(radarCentre.X, radarCentre.Y + radarR), crossCol);

        // ── Text below radar ──────────────────────────────────────────────────

        int textY = ab.Y + pad + radarSize + pad;
        float ts  = theme.SmallScale * 0.85f;

        renderer.DrawText(sb, Units.FormatDistance(DistanceMeters),
            new Vector2(ab.X + pad, textY), theme.Font, ts, theme.TextNormal);

        string status;
        Color  statusCol;
        if (locked)
        {
            status    = "LOCKED TO PAD";
            statusCol = new Color(50, 255, 120);
        }
        else if (DistanceMeters < RadarRangeMeters)
        {
            status    = "ACQUIRING...";
            statusCol = new Color(255, 200, 50);
        }
        else
        {
            status    = "OUT OF RANGE";
            statusCol = theme.TextDisabled;
        }
        renderer.DrawText(sb, status, new Vector2(ab.X + pad, textY + 18),
            theme.Font, ts, statusCol);

        // Station name (dimmed, top-left of panel)
        renderer.DrawText(sb, StationName,
            new Vector2(ab.X + pad, ab.Y + pad),
            theme.Font, theme.SmallScale * 0.72f, theme.TextDisabled);

        // Release (X) button
        var xr = ReleaseButtonRect();
        renderer.DrawRect(sb, xr, theme.PanelBorder, 1);
        renderer.DrawTextCentred(sb, "X", xr, theme.Font, theme.SmallScale * 0.8f, theme.TextDisabled);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Rectangle ReleaseButtonRect()
    {
        var ab = AbsoluteBounds;
        return new Rectangle(ab.Right - 28, ab.Y + 4, 24, 24);
    }

    private static void DrawCircle(SpriteBatch sb, UIRenderer renderer,
        Vector2 centre, float radius, Color color, int segments)
    {
        float step = MathF.PI * 2f / segments;
        for (int i = 0; i < segments; i++)
        {
            float a0 = i * step, a1 = (i + 1) * step;
            renderer.DrawLine(sb,
                centre + new Vector2(MathF.Cos(a0), MathF.Sin(a0)) * radius,
                centre + new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * radius,
                color);
        }
    }
}
