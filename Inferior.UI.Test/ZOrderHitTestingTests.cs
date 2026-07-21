using Inferior.UI;
using Inferior.UI.Controls;
using Microsoft.Xna.Framework;
using Xunit;

namespace Inferior.UI.Test;

public sealed class ZOrderHitTestingTests
{
    [Fact]
    public void Later_sibling_is_topmost_for_hit_testing()
    {
        var root = new Panel(new Rectangle(0, 0, 100, 100));
        var lower = new NamedLeaf("lower") { Bounds = new Rectangle(10, 10, 60, 60) };
        var upper = new NamedLeaf("upper") { Bounds = new Rectangle(20, 20, 60, 60) };
        root.Add(lower);
        root.Add(upper);

        Assert.Same(upper, root.FindAt(new Point(30, 30)));
    }

    [Fact]
    public void Non_overlapping_top_sibling_does_not_block_lower_hit()
    {
        var root = new Panel(new Rectangle(0, 0, 200, 100));
        var lower = new NamedLeaf("lower") { Bounds = new Rectangle(10, 10, 60, 60) };
        var upper = new NamedLeaf("upper") { Bounds = new Rectangle(100, 10, 60, 60) };
        root.Add(lower);
        root.Add(upper);

        Assert.Same(lower, root.FindAt(new Point(30, 30)));
    }

    [Fact]
    public void Hidden_or_disabled_top_control_does_not_starve_lower_hit()
    {
        var root = new Panel(new Rectangle(0, 0, 100, 100));
        var lower = new NamedLeaf("lower") { Bounds = new Rectangle(10, 10, 60, 60) };
        var hiddenUpper = new NamedLeaf("hidden") { Bounds = new Rectangle(10, 10, 60, 60), Visible = false };
        var disabledUpper = new NamedLeaf("disabled") { Bounds = new Rectangle(10, 10, 60, 60), Enabled = false };
        root.Add(lower);
        root.Add(hiddenUpper);
        root.Add(disabledUpper);

        Assert.Same(lower, root.FindAt(new Point(30, 30)));
    }

    [Fact]
    public void Nested_overlap_uses_deterministic_root_order()
    {
        var root = new Panel(new Rectangle(0, 0, 200, 200));
        var left = new Panel(new Rectangle(0, 0, 120, 120));
        var right = new Panel(new Rectangle(40, 40, 120, 120));
        var leftChild = new NamedLeaf("left-child") { Bounds = new Rectangle(50, 50, 40, 40) };
        var rightChild = new NamedLeaf("right-child") { Bounds = new Rectangle(20, 20, 40, 40) };
        left.Add(leftChild);
        right.Add(rightChild);
        root.Add(left);
        root.Add(right);

        Assert.Same(rightChild, root.FindAt(new Point(70, 70)));
    }

    private sealed class NamedLeaf(string name) : Control
    {
        public string NameForTest => name;
    }
}
