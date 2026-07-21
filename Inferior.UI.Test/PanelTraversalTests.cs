using Inferior.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Xunit;

namespace Inferior.UI.Test;

public sealed class PanelTraversalTests
{
    [Theory]
    [InlineData("toolbar,2d,right,status")]
    [InlineData("status,right,2d,toolbar")]
    public void All_visible_siblings_draw_once_without_suppressing_later_siblings(string orderCsv)
    {
        var log = new List<string>();
        var root = new RecordingContainer("root", log) { Bounds = new Rectangle(0, 0, 1000, 600) };
        foreach (string id in orderCsv.Split(','))
            root.Add(new RecordingLeaf(id, log) { Bounds = new Rectangle(0, 0, 100, 30) });

        root.Draw(null!, null!, null!);

        Assert.Equal(orderCsv.Split(',').Select(id => $"draw:{id}"), log);
    }

    [Fact]
    public void Invisible_child_does_not_draw_and_does_not_suppress_following_sibling()
    {
        var log = new List<string>();
        var root = new RecordingContainer("root", log) { Bounds = new Rectangle(0, 0, 200, 100) };
        root.Add(new RecordingLeaf("a", log) { Bounds = new Rectangle(0, 0, 40, 40) });
        root.Add(new RecordingLeaf("b", log) { Bounds = new Rectangle(40, 0, 40, 40), Visible = false });
        root.Add(new RecordingLeaf("c", log) { Bounds = new Rectangle(80, 0, 40, 40) });

        root.Draw(null!, null!, null!);

        Assert.Equal(["draw:a", "draw:c"], log);
    }

    [Fact]
    public void Empty_child_bounds_do_not_suppress_following_sibling()
    {
        var log = new List<string>();
        var root = new RecordingContainer("root", log) { Bounds = new Rectangle(0, 0, 200, 100) };
        root.Add(new RecordingLeaf("zero-width", log) { Bounds = new Rectangle(0, 0, 0, 40) });
        root.Add(new RecordingLeaf("after", log) { Bounds = new Rectangle(40, 0, 40, 40) });

        root.Draw(null!, null!, null!);

        Assert.Equal(["skip-empty:zero-width", "draw:after"], log);
    }

    [Fact]
    public void Nested_panels_draw_depth_first_and_return_to_later_root_siblings()
    {
        var log = new List<string>();
        var root = new RecordingContainer("root", log) { Bounds = new Rectangle(0, 0, 300, 100) };
        var left = new RecordingContainer("left", log) { Bounds = new Rectangle(0, 0, 100, 100) };
        var right = new RecordingContainer("right", log) { Bounds = new Rectangle(100, 0, 100, 100) };
        left.Add(new RecordingLeaf("a", log) { Bounds = new Rectangle(0, 0, 20, 20) });
        left.Add(new RecordingLeaf("b", log) { Bounds = new Rectangle(20, 0, 20, 20) });
        right.Add(new RecordingLeaf("c", log) { Bounds = new Rectangle(0, 0, 20, 20) });
        root.Add(left);
        root.Add(right);
        root.Add(new RecordingLeaf("after-root", log) { Bounds = new Rectangle(200, 0, 20, 20) });

        root.Draw(null!, null!, null!);

        Assert.Equal(["enter:left", "draw:a", "draw:b", "exit:left", "enter:right", "draw:c", "exit:right", "draw:after-root"], log);
    }

    internal sealed class RecordingContainer(string id, List<string> log) : Control
    {
        public override void Draw(SpriteBatch sb, UIRenderer renderer, Theme theme)
        {
            if (!Visible)
                return;
            if (id != "root")
                log.Add($"enter:{id}");
            DrawChildren(sb, renderer, theme);
            if (id != "root")
                log.Add($"exit:{id}");
        }
    }

    internal sealed class RecordingLeaf(string id, List<string> log) : Control
    {
        public override void Draw(SpriteBatch sb, UIRenderer renderer, Theme theme)
        {
            if (!Visible)
                return;
            if (AbsoluteBounds.Width <= 0 || AbsoluteBounds.Height <= 0)
            {
                log.Add($"skip-empty:{id}");
                return;
            }
            log.Add($"draw:{id}");
        }
    }
}
