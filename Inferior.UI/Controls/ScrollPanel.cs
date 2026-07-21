using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.UI.Controls;

public sealed class ScrollPanel : Control
{
    public Thickness ContentPadding { get; set; } = new(6);
    public int ScrollY { get; set; }
    public int ContentHeight { get; private set; }

    public ScrollPanel()
    {
        Overflow = OverflowMode.Clip;
    }

    public override Rectangle ContentBounds
    {
        get
        {
            Rectangle bounds = AbsoluteBounds;
            return new Rectangle(
                bounds.X + ContentPadding.Left,
                bounds.Y + ContentPadding.Top - ScrollY,
                Math.Max(0, bounds.Width - ContentPadding.Horizontal - 10),
                Math.Max(0, bounds.Height - ContentPadding.Vertical));
        }
    }

    public override bool HandleInput(InputState input)
    {
        if (!Visible || !Enabled)
            return false;
        bool inside = AbsoluteBounds.Contains(input.MousePosition);
        if (inside && input.ScrollDelta != 0)
        {
            ScrollY = Math.Clamp(ScrollY - input.ScrollDelta / 3, 0, MaxScroll);
            return true;
        }
        return base.HandleInput(input);
    }

    public override void Update(double dt)
    {
        ArrangeChildren();
        base.Update(dt);
    }

    public override void Draw(SpriteBatch sb, UIRenderer renderer, Theme theme)
    {
        if (!Visible)
            return;
        renderer.DrawRect(sb, AbsoluteBounds, ForeColor ?? theme.WindowBorder, theme.BorderThickness);
        DrawChildren(sb, renderer, theme);
        if (ContentHeight > AbsoluteBounds.Height)
        {
            Rectangle track = new(AbsoluteBounds.Right - 8, AbsoluteBounds.Y + 2, 4, AbsoluteBounds.Height - 4);
            int thumbHeight = Math.Max(18, track.Height * AbsoluteBounds.Height / Math.Max(1, ContentHeight));
            int thumbY = track.Y + (track.Height - thumbHeight) * ScrollY / Math.Max(1, MaxScroll);
            renderer.FillRect(sb, track, theme.TextBoxScrollbar);
            renderer.FillRect(sb, new Rectangle(track.X, thumbY, track.Width, thumbHeight), theme.TextBoxScrollbarThumb);
        }
    }

    protected override void OnBoundsChanged() => ArrangeChildren();

    private int MaxScroll => Math.Max(0, ContentHeight - Math.Max(1, AbsoluteBounds.Height));

    private void ArrangeChildren()
    {
        int y = 0;
        foreach (Control child in _children)
        {
            Point desired = child.DesiredSize;
            child.Bounds = new Rectangle(0, y, ContentBounds.Width, desired.Y);
            y += desired.Y + 4;
        }
        ContentHeight = Math.Max(0, y + ContentPadding.Vertical);
        ScrollY = Math.Clamp(ScrollY, 0, MaxScroll);
    }
}
