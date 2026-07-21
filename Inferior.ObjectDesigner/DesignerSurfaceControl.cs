using Inferior.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.ObjectDesigner;

public enum DesignerSurfaceKind
{
    Orthographic,
    Perspective,
}

public sealed class DesignerSurfaceControl : Control
{
    public DesignerSurfaceKind Kind { get; }
    public string Title { get; set; }
    public Thickness ContentPadding { get; set; } = new(1);
    public int HeaderHeight { get; set; } = 24;
    public Action<UiCustomDrawContext>? DrawCustomContent { get; set; }
    public Action<SpriteBatch, UIRenderer, Rectangle>? DrawContent { get; set; }

    public DesignerSurfaceControl(DesignerSurfaceKind kind, string title)
    {
        Kind = kind;
        Title = title;
        Overflow = OverflowMode.Clip;
    }

    public override Rectangle ContentBounds
    {
        get
        {
            Rectangle bounds = AbsoluteBounds;
            return new Rectangle(
                bounds.X + ContentPadding.Left,
                bounds.Y + HeaderHeight + ContentPadding.Top,
                Math.Max(0, bounds.Width - ContentPadding.Horizontal),
                Math.Max(0, bounds.Height - HeaderHeight - ContentPadding.Vertical));
        }
    }

    public override void Draw(SpriteBatch sb, UIRenderer renderer, Theme theme)
    {
        if (!Visible)
            return;
        Rectangle bounds = AbsoluteBounds;
        renderer.FillRect(sb, bounds, BackColor ?? new Color(9, 12, 13));
        Rectangle content = ContentBounds;
        Rectangle clip = Rectangle.Intersect(content, EffectiveClipBounds);
        System.Diagnostics.Debug.Assert(clip.Width > 0 && clip.Height > 0, $"{Kind} custom clip is empty.");
        System.Diagnostics.Debug.Assert(clip.Left >= content.Left && clip.Top >= content.Top && clip.Right <= content.Right && clip.Bottom <= content.Bottom, $"{Kind} custom clip escaped content bounds.");
        if (DrawCustomContent is not null && clip.Width > 0 && clip.Height > 0)
            renderer.DrawCustomContent(sb, clip, DrawCustomContent);
        if (DrawContent is not null && clip.Width > 0 && clip.Height > 0)
        {
            if (clip == content)
                DrawContent(sb, renderer, content);
            else
                renderer.DrawWithClip(sb, clip, () => DrawContent(sb, renderer, content));
        }

        renderer.DrawRect(sb, bounds, ForeColor ?? theme.WindowBorder, theme.BorderThickness);
        renderer.DrawTextLeft(sb, Title, new Rectangle(bounds.X + 4, bounds.Y + 4, bounds.Width - 8, 22), theme.Font, 0.78f, TextColor ?? theme.TextDisabled, 0);
        DrawChildren(sb, renderer, theme);
    }
}
