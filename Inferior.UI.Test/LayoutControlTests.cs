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
    public void GridPanel_resolves_fixed_auto_and_weighted_star_columns()
    {
        var grid = new GridPanel { Bounds = new Rectangle(0, 0, 1000, 100) };
        grid.Columns.Add(GridLength.Star(3));
        grid.Columns.Add(GridLength.Fixed(10));
        grid.Columns.Add(GridLength.Star(1));
        grid.Rows.Add(GridLength.Auto());
        var left = new FixedControl(10, 40);
        var fixedGap = new FixedControl(10, 20);
        var right = new FixedControl(10, 30);
        grid.Add(left, 0, 0);
        grid.Add(fixedGap, 1, 0);
        grid.Add(right, 2, 0);

        grid.Update(0);

        Assert.Equal(new Rectangle(0, 0, 742, 40), left.Bounds);
        Assert.Equal(new Rectangle(742, 0, 10, 40), fixedGap.Bounds);
        Assert.Equal(new Rectangle(752, 0, 248, 40), right.Bounds);
    }

    [Fact]
    public void ObjectDesigner_like_layout_allocates_positive_non_overlapping_regions()
    {
        var root = new GridPanel { Bounds = new Rectangle(0, 0, 1500, 920), ContentPadding = 6 };
        root.Columns.Add(GridLength.Star());
        root.Columns.Add(GridLength.Fixed(360));
        root.Rows.Add(GridLength.Fixed(36));
        root.Rows.Add(GridLength.Star());
        root.Rows.Add(GridLength.Fixed(30));
        var toolbar = new FixedControl(100, 30);
        var twoD = new FixedControl(100, 100) { Margin = new Thickness(0, 6, 6, 6) };
        var right = new GridPanel { Margin = new Thickness(0, 6, 0, 6) };
        right.Columns.Add(GridLength.Star());
        right.Rows.Add(GridLength.Star());
        right.Rows.Add(GridLength.Fixed(310));
        var threeD = new FixedControl(100, 100);
        var properties = new FixedControl(100, 100) { Margin = new Thickness(0, 6, 0, 0) };
        right.Add(threeD, 0, 0);
        right.Add(properties, 0, 1);
        var status = new FixedControl(100, 20);
        root.Add(toolbar, 0, 0, 2, 1);
        root.Add(twoD, 0, 1);
        root.Add(right, 1, 1);
        root.Add(status, 0, 2, 2, 1);

        root.Update(0);
        right.Update(0);

        Rectangle[] regions = [toolbar.AbsoluteBounds, twoD.AbsoluteBounds, threeD.AbsoluteBounds, properties.AbsoluteBounds, status.AbsoluteBounds];
        Assert.All(regions, rect =>
        {
            Assert.True(rect.Width > 0);
            Assert.True(rect.Height > 0);
        });
        AssertNoOverlap(toolbar.AbsoluteBounds, twoD.AbsoluteBounds);
        AssertNoOverlap(toolbar.AbsoluteBounds, threeD.AbsoluteBounds);
        AssertNoOverlap(twoD.AbsoluteBounds, threeD.AbsoluteBounds);
        AssertNoOverlap(threeD.AbsoluteBounds, properties.AbsoluteBounds);
        AssertNoOverlap(properties.AbsoluteBounds, status.AbsoluteBounds);
    }

    [Fact]
    public void ObjectDesigner_like_layout_survives_small_resize_with_no_negative_regions()
    {
        var root = new GridPanel { Bounds = new Rectangle(0, 0, 240, 120), ContentPadding = 6 };
        root.Columns.Add(GridLength.Star());
        root.Columns.Add(GridLength.Fixed(360));
        root.Rows.Add(GridLength.Fixed(36));
        root.Rows.Add(GridLength.Star());
        root.Rows.Add(GridLength.Fixed(30));
        var toolbar = new FixedControl(100, 30);
        var workspace = new FixedControl(100, 30);
        var right = new FixedControl(100, 30);
        var status = new FixedControl(100, 20);
        root.Add(toolbar, 0, 0, 2, 1);
        root.Add(workspace, 0, 1);
        root.Add(right, 1, 1);
        root.Add(status, 0, 2, 2, 1);

        root.Update(0);

        Assert.All(root.Children, child =>
        {
            Assert.True(child.Bounds.Width >= 0);
            Assert.True(child.Bounds.Height >= 0);
        });
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
    public void Nested_clipping_intersects_absolute_screen_rectangles()
    {
        var outer = new Panel(new Rectangle(10, 20, 100, 100)) { Overflow = OverflowMode.Clip };
        var inner = new Panel(new Rectangle(30, 10, 80, 80)) { Overflow = OverflowMode.Clip };
        var child = new FixedControl(40, 40) { Bounds = new Rectangle(30, 30, 40, 40) };
        outer.Add(inner);
        inner.Add(child);

        Assert.Equal(new Rectangle(40, 30, 70, 80), inner.EffectiveClipBounds);
        Assert.Equal(new Rectangle(40, 30, 70, 80), child.EffectiveClipBounds);
        Assert.Same(child, outer.FindAt(new Point(75, 65)));
        Assert.Null(outer.FindAt(new Point(115, 65)));
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

    [Fact]
    public void CollapsiblePanel_changes_desired_size_without_hiding_adjacent_panel()
    {
        var stack = new StackPanel { Bounds = new Rectangle(0, 0, 200, 200), Spacing = 4 };
        var collapsible = new CollapsiblePanel { Bounds = new Rectangle(0, 0, 200, 100), HeaderHeight = 20 };
        collapsible.Add(new FixedControl(100, 60));
        var following = new FixedControl(100, 30);
        stack.Add(collapsible);
        stack.Add(following);
        stack.Update(0);
        int expandedFollowingY = following.Bounds.Y;

        collapsible.IsExpanded = false;
        stack.Update(0);

        Assert.True(following.Bounds.Y < expandedFollowingY);
        Assert.True(following.Bounds.Height > 0);
    }

    private static void AssertNoOverlap(Rectangle a, Rectangle b)
    {
        Rectangle intersection = Rectangle.Intersect(a, b);
        Assert.True(intersection.Width == 0 || intersection.Height == 0, $"Unexpected overlap {a} vs {b}");
    }

    [Fact]
    public void ChoiceGroup_keeps_exactly_one_selected_value()
    {
        var projection = new ChoiceGroup<string>("Top");
        ToggleButton top = projection.AddChoice("Top", "Top", new Rectangle(0, 0, 40, 20));
        ToggleButton side = projection.AddChoice("Side", "Side", new Rectangle(0, 0, 40, 20));

        projection.Select("Side");
        projection.Select("Side");

        Assert.False(top.IsOn);
        Assert.True(side.IsOn);
        Assert.Equal("Side", projection.SelectedValue);
    }

    [Fact]
    public void SeparateChoiceGroups_do_not_affect_each_other()
    {
        var projection = new ChoiceGroup<string>("Top");
        var constraint = new ChoiceGroup<string>("Plane");
        projection.AddChoice("Top", "Top", new Rectangle(0, 0, 40, 20));
        projection.AddChoice("Side", "Side", new Rectangle(0, 0, 40, 20));
        constraint.AddChoice("Plane", "Plane", new Rectangle(0, 0, 40, 20));
        constraint.AddChoice("X", "X", new Rectangle(0, 0, 40, 20));

        projection.Select("Side");
        constraint.Select("X");

        Assert.Equal("Side", projection.SelectedValue);
        Assert.Equal("X", constraint.SelectedValue);
    }

    private sealed class FixedControl(int width, int height) : Control
    {
        public override Point DesiredSize => new(width, height);
    }
}
