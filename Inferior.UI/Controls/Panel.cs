using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.UI.Controls;

/// <summary>
/// A simple container. Draws a background and optional border.
/// Children are positioned relative to Panel's ContentBounds,
/// which accounts for padding.
/// </summary>
public sealed class Panel : Control
{
    public bool DrawBackground { get; set; } = true;
    public bool DrawBorder     { get; set; } = true;

    /// <summary>
    /// Extra inset for children beyond the border.
    /// 0 = children flush with inside of border.
    /// </summary>
    public int ContentPadding { get; set; } = 0;

    public Panel() { }

    public Panel(Rectangle bounds)
    {
        Bounds = bounds;
    }

    // ── Layout ────────────────────────────────────────────────────────────────

    public override Rectangle ContentBounds
    {
        get
        {
            var ab = AbsoluteBounds;
            int inset = ContentPadding;
            return new Rectangle(
                ab.X + inset,
                ab.Y + inset,
                ab.Width  - inset * 2,
                ab.Height - inset * 2);
        }
    }

    // ── Draw ──────────────────────────────────────────────────────────────────

    public override void Draw(SpriteBatch sb, UIRenderer renderer, Theme theme)
    {
        if (!Visible) return;

        if (DrawBackground || DrawBorder)
        {
            Color back   = BackColor   ?? theme.PanelBackground;
            Color border = ForeColor   ?? theme.PanelBorder;
            int   bw     = DrawBorder ? theme.BorderThickness : 0;

            if (DrawBackground) renderer.FillRect(sb, AbsoluteBounds, back);
            if (DrawBorder)     renderer.DrawRect(sb, AbsoluteBounds, border, bw);
        }

        DrawChildren(sb, renderer, theme);
    }
}
