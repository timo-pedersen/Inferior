using Inferior.UI;
using Inferior.UI.Controls;
using Microsoft.Xna.Framework;
using Xunit;

namespace Inferior.ObjectDesigner.Test;

public sealed class ObjectDesignerCompositionFixtureTests
{
    [Fact]
    public void ObjectDesigner_like_tree_lays_out_all_regions_with_non_empty_clips()
    {
        ObjectDesignerFixture fixture = ObjectDesignerFixture.Build(1500, 920);

        AssertPositive("toolbar", fixture.Toolbar.AbsoluteBounds);
        AssertPositive("2d", fixture.TwoD.ContentBounds);
        AssertPositive("3d", fixture.ThreeD.ContentBounds);
        AssertPositive("3d footer", fixture.ThreeDFooter.AbsoluteBounds);
        AssertPositive("properties", fixture.Properties.AbsoluteBounds);
        AssertPositive("diagnostics", fixture.Diagnostics.AbsoluteBounds);
        AssertPositive("status", fixture.Status.AbsoluteBounds);
        AssertNonEmptyClip("2d", fixture.TwoD);
        AssertNonEmptyClip("3d", fixture.ThreeD);
        AssertNoTotalOverlap(fixture.Toolbar.AbsoluteBounds, fixture.TwoD.ContentBounds);
        AssertNoTotalOverlap(fixture.Toolbar.AbsoluteBounds, fixture.ThreeD.ContentBounds);
        Assert.True(fixture.ThreeD.ContentBounds.Bottom <= fixture.ThreeDFooter.AbsoluteBounds.Top, "3D render viewport overlapped the footer.");
        AssertNoTotalOverlap(fixture.ThreeDFooter.AbsoluteBounds, fixture.Properties.AbsoluteBounds);
        AssertNoTotalOverlap(fixture.TwoD.ContentBounds, fixture.Properties.AbsoluteBounds);
        AssertNoTotalOverlap(fixture.ThreeD.ContentBounds, fixture.Status.AbsoluteBounds);
    }

    [Fact]
    public void ObjectDesigner_like_draw_order_keeps_popup_last()
    {
        List<string> draw = ["toolbar", "2d-host", "2d-custom", "2d-foreground", "3d-host", "3d-custom", "3d-foreground", "properties", "diagnostics", "status", "popup"];

        Assert.True(draw.IndexOf("2d-custom") > draw.IndexOf("2d-host"));
        Assert.True(draw.IndexOf("3d-custom") > draw.IndexOf("3d-host"));
        Assert.True(draw.IndexOf("properties") > draw.IndexOf("3d-foreground"));
        Assert.True(draw.IndexOf("status") > draw.IndexOf("properties"));
        Assert.Equal("popup", draw[^1]);
    }

    private static void AssertPositive(string id, Rectangle rect)
    {
        Assert.True(rect.Width > 0, $"{id} width was not positive: {rect}");
        Assert.True(rect.Height > 0, $"{id} height was not positive: {rect}");
    }

    private static void AssertNonEmptyClip(string id, Control control)
    {
        Rectangle clip = control.EffectiveClipBounds;
        Assert.True(clip.Width > 0, $"{id} clip width was not positive: {clip}");
        Assert.True(clip.Height > 0, $"{id} clip height was not positive: {clip}");
    }

    private static void AssertNoTotalOverlap(Rectangle a, Rectangle b)
    {
        Rectangle intersection = Rectangle.Intersect(a, b);
        Assert.False(intersection == a || intersection == b, $"One region fully covered another: {a} vs {b}");
    }

    private sealed record ObjectDesignerFixture(
        StackPanel Toolbar,
        DesignerSurfaceControl TwoD,
        DesignerSurfaceControl ThreeD,
        Panel ThreeDFooter,
        Button DisplayModeButton,
        Label PreviewStateLabel,
        CollapsiblePanel Properties,
        TextBlock Diagnostics,
        Label Status)
    {
        public static ObjectDesignerFixture Build(int width, int height)
        {
            var root = new GridPanel
            {
                Bounds = new Rectangle(0, 0, width, height),
                ContentPadding = 6,
                Overflow = OverflowMode.Clip,
            };
            root.Columns.Add(GridLength.Star());
            root.Columns.Add(GridLength.Fixed(360));
            root.Rows.Add(GridLength.Fixed(36));
            root.Rows.Add(GridLength.Star());
            root.Rows.Add(GridLength.Fixed(30));

            var toolbar = new StackPanel { Orientation = StackOrientation.Horizontal, ContentPadding = 3 };
            toolbar.Add(new Button("File", new Rectangle(0, 0, 58, 28)));
            toolbar.Add(new Button("Top", new Rectangle(0, 0, 58, 28)));
            root.Add(toolbar, 0, 0, 2, 1);

            var twoD = new DesignerSurfaceControl(DesignerSurfaceKind.Orthographic, "2D editor")
            {
                Margin = new Thickness(0, 6, 6, 6),
            };
            root.Add(twoD, 0, 1);

            var right = new GridPanel { Margin = new Thickness(0, 6, 0, 6), Overflow = OverflowMode.Clip };
            right.Columns.Add(GridLength.Star());
            right.Rows.Add(GridLength.Star());
            right.Rows.Add(GridLength.Fixed(310));
            root.Add(right, 1, 1);

            var perspectivePane = new GridPanel { Overflow = OverflowMode.Clip };
            perspectivePane.Columns.Add(GridLength.Star());
            perspectivePane.Rows.Add(GridLength.Star());
            perspectivePane.Rows.Add(GridLength.Fixed(34));
            right.Add(perspectivePane, 0, 0);

            var threeD = new DesignerSurfaceControl(DesignerSurfaceKind.Perspective, "3D preview");
            perspectivePane.Add(threeD, 0, 0);

            var footer = new Panel { ContentPadding = 3 };
            var displayButton = new Button("DISPLAY: SOLID", new Rectangle(0, 0, 150, 28));
            var previewLabel = new Label("CURRENT", new Rectangle(170, 2, 160, 24));
            footer.Add(displayButton);
            footer.Add(previewLabel);
            perspectivePane.Add(footer, 0, 1);

            var properties = new CollapsiblePanel { Header = "Properties", Margin = new Thickness(0, 6, 0, 0) };
            var panel = new Panel { Bounds = new Rectangle(0, 0, 330, 270), ContentPadding = 8 };
            var diagnostics = new TextBlock { Bounds = new Rectangle(0, 200, 320, 70), Text = "No validation errors." };
            panel.Add(new TextBox { Bounds = new Rectangle(24, 90, 112, 28) });
            panel.Add(diagnostics);
            properties.Add(panel);
            right.Add(properties, 0, 1);

            var status = new Label("Ready.", new Rectangle(0, 0, 1000, 24));
            root.Add(status, 0, 2, 2, 1);

            root.Update(0);
            right.Update(0);
            perspectivePane.Update(0);
            properties.Update(0);
            panel.Update(0);
            return new ObjectDesignerFixture(toolbar, twoD, threeD, footer, displayButton, previewLabel, properties, diagnostics, status);
        }
    }
}
