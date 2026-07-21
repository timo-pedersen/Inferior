using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Inferior.UI.Controls;

public sealed class CollapsiblePanel : Control
{
    public string Header { get; set; } = "";
    public bool IsExpanded { get; set; } = true;
    public int HeaderHeight { get; set; } = 28;
    public Thickness ContentPadding { get; set; } = new(8);
    public int Spacing { get; set; } = 6;

    public override Rectangle ContentBounds
    {
        get
        {
            Rectangle bounds = AbsoluteBounds;
            if (!IsExpanded)
                return Rectangle.Empty;
            return new Rectangle(
                bounds.X + ContentPadding.Left,
                bounds.Y + HeaderHeight + ContentPadding.Top,
                Math.Max(0, bounds.Width - ContentPadding.Horizontal),
                Math.Max(0, bounds.Height - HeaderHeight - ContentPadding.Vertical));
        }
    }

    public override Point DesiredSize
    {
        get
        {
            if (!IsExpanded || Children.Count == 0)
                return new Point(Bounds.Width, HeaderHeight);
            int width = 0;
            int height = HeaderHeight + ContentPadding.Vertical;
            for (int i = 0; i < Children.Count; i++)
            {
                Point size = Children[i].DesiredSize;
                width = Math.Max(width, size.X);
                height += size.Y + (i == 0 ? 0 : Spacing);
            }
            return new Point(Math.Max(Bounds.Width, width + ContentPadding.Horizontal), height);
        }
    }

    public override void Update(double dt)
    {
        ArrangeChildren();
        base.Update(dt);
    }

    public override bool HandleInput(InputState input)
    {
        if (!Visible || !Enabled)
            return false;
        Rectangle header = HeaderBounds;
        if (input.LeftReleased && header.Contains(input.MousePosition))
        {
            IsExpanded = !IsExpanded;
            return true;
        }
        if (!IsExpanded)
            return false;
        return base.HandleInput(input);
    }

    public override Control? FindAt(Point screenPos)
    {
        if (!HitTest(screenPos))
            return null;
        if (HeaderBounds.Contains(screenPos))
            return this;
        return IsExpanded ? base.FindAt(screenPos) : null;
    }

    public override void Draw(SpriteBatch sb, UIRenderer renderer, Theme theme)
    {
        if (!Visible)
            return;
        Rectangle bounds = AbsoluteBounds;
        Color border = ForeColor ?? theme.WindowBorder;
        renderer.FillRect(sb, HeaderBounds, theme.WindowTitleBar);
        renderer.DrawRect(sb, bounds, border, theme.BorderThickness);
        renderer.DrawTextLeft(sb, (IsExpanded ? "v " : "> ") + Header, HeaderBounds, theme.Font, theme.FontScale, TextColor ?? theme.WindowTitleText, theme.Padding);
        if (IsExpanded)
            DrawChildren(sb, renderer, theme);
    }

    protected override void OnBoundsChanged() => ArrangeChildren();

    private Rectangle HeaderBounds => new(AbsoluteBounds.X, AbsoluteBounds.Y, AbsoluteBounds.Width, HeaderHeight);

    private void ArrangeChildren()
    {
        if (!IsExpanded)
            return;
        int y = 0;
        foreach (Control child in _children)
        {
            Point desired = child.DesiredSize;
            child.Bounds = new Rectangle(0, y, ContentBounds.Width, desired.Y);
            y += desired.Y + Spacing;
        }
    }
}
