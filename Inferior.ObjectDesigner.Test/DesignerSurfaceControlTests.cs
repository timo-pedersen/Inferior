using Inferior.UI;
using Microsoft.Xna.Framework;
using Xunit;

namespace Inferior.ObjectDesigner.Test;

public sealed class DesignerSurfaceControlTests
{
    [Fact]
    public void Content_bounds_are_non_empty_absolute_and_inside_effective_clip()
    {
        var surface = new DesignerSurfaceControl(DesignerSurfaceKind.Orthographic, "2D")
        {
            Bounds = new Rectangle(20, 30, 320, 240),
            Overflow = OverflowMode.Clip,
        };

        Rectangle content = surface.ContentBounds;
        Rectangle clip = surface.EffectiveClipBounds;
        Rectangle intersection = Rectangle.Intersect(content, clip);

        Assert.True(content.Width > 0);
        Assert.True(content.Height > 0);
        Assert.True(clip.Width > 0);
        Assert.True(clip.Height > 0);
        Assert.True(intersection.Width > 0);
        Assert.True(intersection.Height > 0);
        Assert.True(content.Y > surface.AbsoluteBounds.Y);
    }
}
