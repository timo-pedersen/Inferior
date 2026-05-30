using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.UI;

/// <summary>
/// Handles all drawing for the UI system.
/// Owns the pixel and circle textures.
/// Controls call renderer methods rather than drawing directly,
/// so swapping a renderer changes the entire visual style.
/// </summary>
public sealed class UIRenderer : IDisposable
{
    private readonly GraphicsDevice _gd;
    private readonly Texture2D      _pixel;
    private readonly Texture2D      _circle;
    private bool                    _disposed;

    public UIRenderer(GraphicsDevice gd)
    {
        _gd    = gd;
        _pixel = CreatePixel(gd);
        _circle = CreateCircle(gd, 64);
    }

    // ── Primitives ────────────────────────────────────────────────────────────

    public void FillRect(SpriteBatch sb, Rectangle rect, Color color)
        => sb.Draw(_pixel, rect, color);

    public void DrawLine(SpriteBatch sb, Vector2 from, Vector2 to, Color color, float thickness = 1f)
    {
        var   delta  = to - from;
        float length = delta.Length();
        if (length < 0.1f) return;

        float angle = MathF.Atan2(delta.Y, delta.X);
        sb.Draw(_pixel,
            new Rectangle((int)from.X, (int)from.Y, (int)length, (int)System.Math.Max(1f, thickness)),
            null, color, angle, Vector2.Zero, SpriteEffects.None, 0f);
    }

    public void DrawRect(SpriteBatch sb, Rectangle rect, Color color, int thickness = 1)
    {
        // Top
        sb.Draw(_pixel, new Rectangle(rect.Left, rect.Top, rect.Width, thickness), color);
        // Bottom
        sb.Draw(_pixel, new Rectangle(rect.Left, rect.Bottom - thickness, rect.Width, thickness), color);
        // Left
        sb.Draw(_pixel, new Rectangle(rect.Left, rect.Top, thickness, rect.Height), color);
        // Right
        sb.Draw(_pixel, new Rectangle(rect.Right - thickness, rect.Top, thickness, rect.Height), color);
    }

    public void DrawDot(SpriteBatch sb, Vector2 centre, float radius, Color color)
    {
        float size = radius * 2f;
        sb.Draw(_circle,
            new Rectangle((int)(centre.X - radius), (int)(centre.Y - radius),
                          (int)size, (int)size),
            color);
    }

    // ── Text ──────────────────────────────────────────────────────────────────

    public void DrawText(SpriteBatch sb, string text, Vector2 pos,
        SpriteFont font, float scale, Color color)
        => sb.DrawString(font, text, pos, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);

    public void DrawTextCentred(SpriteBatch sb, string text, Rectangle bounds,
        SpriteFont font, float scale, Color color)
    {
        var size = font.MeasureString(text) * scale;
        var pos  = new Vector2(
            bounds.X + (bounds.Width  - size.X) * 0.5f,
            bounds.Y + (bounds.Height - size.Y) * 0.5f);
        sb.DrawString(font, text, pos, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
    }

    public void DrawTextLeft(SpriteBatch sb, string text, Rectangle bounds,
        SpriteFont font, float scale, Color color, int padding = 0)
    {
        var size = font.MeasureString(text) * scale;
        var pos  = new Vector2(
            bounds.X + padding,
            bounds.Y + (bounds.Height - size.Y) * 0.5f);
        sb.DrawString(font, text, pos, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
    }

    public Vector2 MeasureText(string text, SpriteFont font, float scale)
        => font.MeasureString(text) * scale;

    // ── Control drawing ───────────────────────────────────────────────────────

    public void DrawPanel(SpriteBatch sb, Rectangle bounds,
        Color background, Color border, int borderThickness)
    {
        FillRect(sb, bounds, background);
        if (borderThickness > 0)
            DrawRect(sb, bounds, border, borderThickness);
    }

    public void DrawButton(SpriteBatch sb, Rectangle bounds,
        string text, Theme theme,
        bool hovered, bool pressed, bool focused, bool enabled,
        Color? backOverride = null, Color? textOverride = null,
        SpriteFont? fontOverride = null, float? scaleOverride = null)
    {
        // Background
        Color back = backOverride ?? (
            !enabled  ? theme.ButtonBackgroundDisabled :
            pressed   ? theme.ButtonBackgroundPressed  :
            hovered   ? theme.ButtonBackgroundHover    :
                        theme.ButtonBackground);

        // Border
        Color border =
            focused   ? theme.ButtonBorderFocus :
            hovered   ? theme.ButtonBorderHover :
                        theme.ButtonBorder;

        int borderW = focused ? 2 : theme.BorderThickness;

        FillRect(sb, bounds, back);
        DrawRect(sb, bounds, border, borderW);

        // Focus accent line on bottom
        if (focused)
        {
            var accentLine = new Rectangle(
                bounds.X + 4, bounds.Bottom - 2,
                bounds.Width - 8, 2);
            FillRect(sb, accentLine, theme.Accent);
        }

        // Text
        if (!string.IsNullOrEmpty(text))
        {
            var   font  = fontOverride  ?? theme.Font;
            float scale = scaleOverride ?? theme.FontScale;
            Color tc    = textOverride  ?? (
                !enabled ? theme.TextDisabled :
                hovered  ? theme.TextHover    :
                           theme.TextNormal);

            DrawTextCentred(sb, text, bounds, font, scale, tc);
        }
    }

    public void DrawWindow(SpriteBatch sb, Rectangle bounds, string title,
        Theme theme, bool hasFocus)
    {
        // Body
        FillRect(sb, bounds, theme.WindowBackground);

        // Title bar
        var titleBar = new Rectangle(bounds.X, bounds.Y, bounds.Width, theme.TitleBarHeight);
        FillRect(sb, titleBar, theme.WindowTitleBar);

        // Border — brighter when focused
        Color border = hasFocus ? theme.WindowBorderFocus : theme.WindowBorder;
        DrawRect(sb, bounds, border, theme.BorderThickness);

        // Title text
        if (!string.IsNullOrEmpty(title))
        {
            DrawTextLeft(sb, title, titleBar, theme.Font, theme.FontScale,
                theme.WindowTitleText, theme.Padding);
        }

        // Close button indicator (X) — drawn by Window control itself
    }

    public void DrawCloseButton(SpriteBatch sb, Rectangle bounds,
        bool hovered, Theme theme)
    {
        if (hovered)
            FillRect(sb, bounds, new Color(180, 40, 40, 200));

        Color col = hovered ? Color.White : theme.TextDisabled;
        float pad = bounds.Width * 0.25f;

        DrawLine(sb,
            new Vector2(bounds.X + pad, bounds.Y + pad),
            new Vector2(bounds.Right - pad, bounds.Bottom - pad),
            col, 1.5f);
        DrawLine(sb,
            new Vector2(bounds.Right - pad, bounds.Y + pad),
            new Vector2(bounds.X + pad, bounds.Bottom - pad),
            col, 1.5f);
    }

    public void DrawFocusRing(SpriteBatch sb, Rectangle bounds, Color accent)
    {
        // Slightly inset dashed/solid accent ring
        var ring = new Rectangle(bounds.X + 2, bounds.Y + 2,
                                 bounds.Width - 4, bounds.Height - 4);
        DrawRect(sb, ring, accent, 1);
    }

    // ── Texture creation ──────────────────────────────────────────────────────

    private static Texture2D CreatePixel(GraphicsDevice gd)
    {
        var t = new Texture2D(gd, 1, 1);
        t.SetData([Color.White]);
        return t;
    }

    private static Texture2D CreateCircle(GraphicsDevice gd, int diameter)
    {
        var   t     = new Texture2D(gd, diameter, diameter);
        var   data  = new Color[diameter * diameter];
        float r     = diameter * 0.5f;
        float cx    = r, cy = r;
        float inner = r * 0.55f;

        for (int y = 0; y < diameter; y++)
        for (int x = 0; x < diameter; x++)
        {
            float dist  = MathF.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
            float alpha = dist <= inner ? 1f
                        : dist <= r    ? 1f - (dist - inner) / (r - inner)
                        : 0f;

            data[y * diameter + x] = Color.White * alpha;
        }

        t.SetData(data);
        return t;
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _pixel.Dispose();
        _circle.Dispose();
        _disposed = true;
    }
}
