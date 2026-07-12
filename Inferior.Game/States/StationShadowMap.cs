using Inferior.Game.StationGen;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Diagnostics;

namespace Inferior.Game.States;

internal sealed class StationShadowMap : IDisposable
{
    private readonly GraphicsDevice _gd;

    public RenderTarget2D Texture { get; }
    public SurfaceFormat SurfaceFormat => Texture.Format;
    public Matrix LightView { get; private set; }
    public Matrix LightProjection { get; private set; }
    public Matrix LightViewProjection { get; private set; }
    public StationShadowDepthRange DepthRange { get; private set; }
    public StationShadowBounds Bounds { get; private set; }
    public Vector3 StationLocalSunDirection { get; private set; }

    public StationShadowMap(GraphicsDevice gd, int size)
    {
        _gd = gd;
        Texture = CreateDepthTarget(gd, size);
    }

    public void Build(
        Effect effect,
        IReadOnlyList<PlacedModule> modules,
        IReadOnlyDictionary<PlacedModule, (VertexBuffer vb, IndexBuffer ib, int triCount)> hullMeshes,
        IReadOnlyDictionary<PlacedModule, (VertexBuffer vb, IndexBuffer ib, int triCount)> decoMeshes,
        IReadOnlyDictionary<PlacedModule, (VertexBuffer vb, IndexBuffer ib, int triCount)> glassMeshes,
        Vector3 stationLocalSunDirection)
    {
        StationLocalSunDirection = Vector3.Normalize(stationLocalSunDirection);
        Bounds = StationShadowMath.ComputeStationBounds(modules, padding: 0f);
        // SceneLighting.SunDirection points from the station toward the star. Put the
        // shadow camera on that sunward side so nearest depth is the first surface hit by light.
        LightView = StationShadowMath.CreateLightView(StationLocalSunDirection, Bounds);
        LightProjection = StationShadowMath.CreateLightProjection(
            Bounds, LightView, xyPadding: 25f, zPadding: 2f, out StationShadowDepthRange depthRange);
        DepthRange = depthRange;
        LightViewProjection = LightView * LightProjection;
        Debug.WriteLine(
            $"Station shadow target {Texture.Width}x{Texture.Height} format={Texture.Format} " +
            $"near={DepthRange.Near:0.###}m far={DepthRange.Far:0.###}m span={DepthRange.Length:0.###}m");

        RenderTargetBinding[] oldTargets = _gd.GetRenderTargets();
        Viewport oldViewport = _gd.Viewport;
        RasterizerState oldRasterizer = _gd.RasterizerState;
        DepthStencilState oldDepth = _gd.DepthStencilState;
        BlendState oldBlend = _gd.BlendState;

        _gd.SetRenderTarget(Texture);
        _gd.Clear(Color.White);
        _gd.RasterizerState = RasterizerState.CullNone;
        _gd.DepthStencilState = DepthStencilState.Default;
        _gd.BlendState = BlendState.Opaque;

        effect.CurrentTechnique = effect.Techniques["ShadowDepth"];
        effect.Parameters["LightView"]?.SetValue(LightView);
        effect.Parameters["LightViewProjection"]?.SetValue(LightViewProjection);
        effect.Parameters["LightDepthNear"]?.SetValue(DepthRange.Near);
        effect.Parameters["LightDepthFar"]?.SetValue(DepthRange.Far);

        foreach (var mod in modules)
        {
            effect.Parameters["StationLocalWorld"]?.SetValue(mod.Transform);

            if (hullMeshes.TryGetValue(mod, out var hull))
                Draw(effect, hull.vb, hull.ib, hull.triCount);
            if (decoMeshes.TryGetValue(mod, out var deco))
                Draw(effect, deco.vb, deco.ib, deco.triCount);
            if (glassMeshes.TryGetValue(mod, out var glass))
                Draw(effect, glass.vb, glass.ib, glass.triCount);
        }

        _gd.SetRenderTargets(oldTargets);
        _gd.Viewport = oldViewport;
        _gd.RasterizerState = oldRasterizer;
        _gd.DepthStencilState = oldDepth;
        _gd.BlendState = oldBlend;
    }

    public void Dispose() => Texture.Dispose();

    private static RenderTarget2D CreateDepthTarget(GraphicsDevice gd, int size)
    {
        return new RenderTarget2D(gd, size, size, false, SurfaceFormat.Color, DepthFormat.Depth24);
    }

    private void Draw(Effect effect, VertexBuffer vb, IndexBuffer ib, int triCount)
    {
        _gd.SetVertexBuffer(vb);
        _gd.Indices = ib;
        foreach (var pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, triCount);
        }
    }
}
