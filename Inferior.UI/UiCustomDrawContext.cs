using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.UI;

public sealed class UiCustomDrawContext
{
    private readonly RenderTargetBinding[] _uiRenderTargets;
    private readonly Viewport _uiViewport;

    internal UiCustomDrawContext(GraphicsDevice graphicsDevice, Rectangle clipBounds, RenderTargetBinding[] uiRenderTargets, Viewport uiViewport)
    {
        GraphicsDevice = graphicsDevice;
        ClipBounds = clipBounds;
        _uiRenderTargets = uiRenderTargets;
        _uiViewport = uiViewport;
    }

    public GraphicsDevice GraphicsDevice { get; }
    public Rectangle ClipBounds { get; }

    public void RestoreUiRenderTarget()
    {
        if (_uiRenderTargets.Length == 0)
            GraphicsDevice.SetRenderTarget(null);
        else
            GraphicsDevice.SetRenderTargets(_uiRenderTargets);
        GraphicsDevice.Viewport = _uiViewport;
    }
}
