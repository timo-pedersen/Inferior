using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.UI.Controls;

public enum StackOrientation
{
    Horizontal,
    Vertical,
}

public sealed class StackPanel : Control
{
    public StackOrientation Orientation { get; set; } = StackOrientation.Vertical;
    public int Spacing { get; set; } = 4;
    public int ContentPadding { get; set; }
    public bool DrawBackground { get; set; }
    public bool DrawBorder { get; set; }

    public override Rectangle ContentBounds
    {
        get
        {
            Rectangle ab = AbsoluteBounds;
            int pad = ContentPadding;
            return new Rectangle(ab.X + pad, ab.Y + pad, Math.Max(0, ab.Width - pad * 2), Math.Max(0, ab.Height - pad * 2));
        }
    }

    public override Point DesiredSize
    {
        get
        {
            int width = ContentPadding * 2;
            int height = ContentPadding * 2;
            Control[] visible = Children.Where(child => child.Visible).ToArray();
            if (Orientation == StackOrientation.Vertical)
            {
                width += visible.Length == 0 ? 0 : visible.Max(child => child.DesiredSize.X + child.Margin.Horizontal);
                height += visible.Sum(child => child.DesiredSize.Y + child.Margin.Vertical)
                    + Math.Max(0, visible.Length - 1) * Spacing;
            }
            else
            {
                width += visible.Sum(child => child.DesiredSize.X + child.Margin.Horizontal)
                    + Math.Max(0, visible.Length - 1) * Spacing;
                height += visible.Length == 0 ? 0 : visible.Max(child => child.DesiredSize.Y + child.Margin.Vertical);
            }
            return new Point(width, height);
        }
    }

    protected override void OnBoundsChanged() => ArrangeChildren();

    public override void Update(double dt)
    {
        ArrangeChildren();
        base.Update(dt);
    }

    public override void Draw(SpriteBatch sb, UIRenderer renderer, Theme theme)
    {
        if (!Visible) return;
        if (DrawBackground)
            renderer.FillRect(sb, AbsoluteBounds, BackColor ?? theme.PanelBackground);
        if (DrawBorder)
            renderer.DrawRect(sb, AbsoluteBounds, ForeColor ?? theme.PanelBorder, theme.BorderThickness);
        DrawChildren(sb, renderer, theme);
    }

    private void ArrangeChildren()
    {
        Rectangle content = ContentBounds;
        int cursor = Orientation == StackOrientation.Vertical ? 0 : 0;
        foreach (Control child in Children.Where(child => child.Visible))
        {
            Point desired = child.DesiredSize;
            if (Orientation == StackOrientation.Vertical)
            {
                cursor += child.Margin.Top;
                child.Bounds = new Rectangle(
                    child.Margin.Left,
                    cursor,
                    Math.Max(0, content.Width - child.Margin.Horizontal),
                    desired.Y);
                cursor += desired.Y + child.Margin.Bottom + Spacing;
            }
            else
            {
                cursor += child.Margin.Left;
                child.Bounds = new Rectangle(
                    cursor,
                    child.Margin.Top,
                    desired.X,
                    Math.Max(0, content.Height - child.Margin.Vertical));
                cursor += desired.X + child.Margin.Right + Spacing;
            }
        }
    }
}
