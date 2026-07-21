using Inferior.UI;
using Inferior.UI.Controls;
using Microsoft.Xna.Framework;
using Xunit;

namespace Inferior.UI.Test;

public sealed class ClippingHitTestingTests
{
    [Fact]
    public void Single_clipping_parent_intersects_child_absolute_bounds()
    {
        var parent = new Panel(new Rectangle(0, 0, 100, 100)) { Overflow = OverflowMode.Clip };
        var child = new TestLeaf { Bounds = new Rectangle(50, 50, 100, 100) };
        parent.Add(child);

        Assert.Equal(new Rectangle(0, 0, 100, 100), child.EffectiveClipBounds);
        Assert.Same(child, parent.FindAt(new Point(75, 75)));
        Assert.Null(parent.FindAt(new Point(125, 75)));
    }

    [Fact]
    public void Nested_clipping_allows_visible_ancestor_to_pass_through()
    {
        var root = new Panel(new Rectangle(0, 0, 100, 100)) { Overflow = OverflowMode.Clip };
        var visible = new Panel(new Rectangle(10, 10, 120, 120)) { Overflow = OverflowMode.Visible };
        var clipped = new Panel(new Rectangle(20, 20, 60, 60)) { Overflow = OverflowMode.Clip };
        var child = new TestLeaf { Bounds = new Rectangle(40, 40, 60, 60) };
        root.Add(visible);
        visible.Add(clipped);
        clipped.Add(child);

        Assert.Equal(new Rectangle(30, 30, 60, 60), child.EffectiveClipBounds);
        Assert.Same(child, root.FindAt(new Point(75, 75)));
        Assert.Same(visible, root.FindAt(new Point(95, 75)));
    }

    [Fact]
    public void Empty_clip_rejects_pointer_without_affecting_following_sibling()
    {
        var root = new Panel(new Rectangle(0, 0, 300, 100));
        var clipped = new Panel(new Rectangle(0, 0, 50, 50)) { Overflow = OverflowMode.Clip };
        clipped.Add(new TestLeaf { Bounds = new Rectangle(100, 100, 20, 20) });
        var sibling = new TestLeaf { Bounds = new Rectangle(200, 0, 40, 40) };
        root.Add(clipped);
        root.Add(sibling);

        Assert.Null(root.FindAt(new Point(105, 105)));
        Assert.Same(sibling, root.FindAt(new Point(220, 20)));
    }

    [Fact]
    public void Drawing_and_hit_testing_share_effective_clip()
    {
        var parent = new Panel(new Rectangle(0, 0, 100, 100)) { Overflow = OverflowMode.Clip };
        var child = new TestLeaf { Bounds = new Rectangle(50, 50, 100, 100) };
        parent.Add(child);

        Assert.Same(child, parent.FindAt(new Point(75, 75)));
        Assert.Null(parent.FindAt(new Point(125, 75)));
        Assert.Null(parent.FindAt(new Point(180, 180)));
    }

    [Fact]
    public void Overflow_visible_child_can_be_hit_outside_parent_when_no_clip_ancestor_blocks_it()
    {
        var parent = new Panel(new Rectangle(0, 0, 100, 100)) { Overflow = OverflowMode.Visible };
        var child = new TestLeaf { Bounds = new Rectangle(120, 0, 40, 40) };
        parent.Add(child);

        Assert.Same(child, parent.FindAt(new Point(130, 10)));
    }

    private sealed class TestLeaf : Control;
}
