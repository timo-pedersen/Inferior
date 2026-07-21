using Inferior.UI;
using Inferior.UI.Controls;
using Microsoft.Xna.Framework;
using Xunit;

namespace Inferior.UI.Test;

public sealed class LayoutControlTests
{
    [Fact]
    public void StackPanel_arranges_children_on_primary_axis()
    {
        var stack = new StackPanel
        {
            Bounds = new Rectangle(10, 20, 200, 100),
            Orientation = StackOrientation.Vertical,
            Spacing = 3,
            ContentPadding = 5,
        };
        var a = new FixedControl(40, 12);
        var b = new FixedControl(60, 18);
        stack.Add(a);
        stack.Add(b);

        stack.Update(0);

        Assert.Equal(new Rectangle(0, 0, 190, 12), a.Bounds);
        Assert.Equal(new Rectangle(0, 15, 190, 18), b.Bounds);
        Assert.Equal(new Rectangle(15, 25, 190, 12), a.AbsoluteBounds);
    }

    [Fact]
    public void GridPanel_resolves_fixed_and_star_columns()
    {
        var grid = new GridPanel { Bounds = new Rectangle(0, 0, 300, 100) };
        grid.Columns.Add(GridLength.Fixed(90));
        grid.Columns.Add(GridLength.Star());
        grid.Rows.Add(GridLength.Star());
        var left = new FixedControl(10, 10);
        var right = new FixedControl(10, 10);
        grid.Add(left, 0, 0);
        grid.Add(right, 1, 0);

        grid.Update(0);

        Assert.Equal(new Rectangle(0, 0, 90, 100), left.Bounds);
        Assert.Equal(new Rectangle(90, 0, 210, 100), right.Bounds);
    }

    [Fact]
    public void Clipped_parent_prevents_child_hit_outside_effective_clip()
    {
        var panel = new Panel(new Rectangle(0, 0, 100, 100)) { Overflow = OverflowMode.Clip };
        var child = new FixedControl(80, 80) { Bounds = new Rectangle(70, 70, 80, 80) };
        panel.Add(child);

        Assert.Same(child, panel.FindAt(new Point(90, 90)));
        Assert.Null(panel.FindAt(new Point(120, 90)));
    }

    [Fact]
    public void CollapsiblePanel_hides_child_hit_testing_when_closed()
    {
        var panel = new CollapsiblePanel { Bounds = new Rectangle(0, 0, 100, 100), IsExpanded = false };
        var child = new FixedControl(80, 40) { Bounds = new Rectangle(0, 28, 80, 40) };
        panel.Add(child);

        Assert.Null(panel.FindAt(new Point(10, 40)));
        Assert.Same(panel, panel.FindAt(new Point(10, 10)));
    }

    private sealed class FixedControl(int width, int height) : Control
    {
        public override Point DesiredSize => new(width, height);
    }
}
