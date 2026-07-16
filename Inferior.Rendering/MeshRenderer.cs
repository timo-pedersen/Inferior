using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.Rendering;

/// <summary>
/// Unified 3D draw call over the shared LitSurface.fx effect (Docs/station-lighting-pipeline-spec.md).
/// All meshes drawn here use VertexPositionNormalColorTexture. Two techniques:
///   DynamicLit    — real-time ambient + saturate(N.L) model (ships, containers, station hull).
///   BakedColorLit — vertex colour is albedo x AO (+ self-illumination floor in alpha); the sun
///                   term is still computed every frame from the real normal (station deco).
/// Sets RasterizerState.CullCounterClockwiseFace explicitly — no CullNone workaround needed
/// when geometry is wound correctly by GeometryBuilder.
/// </summary>
public sealed class MeshRenderer : IDisposable
{
    private readonly GraphicsDevice _gd;
    private readonly Effect         _litSurfaceEffect;
    private readonly Texture2D      _whiteTexture;   // 1x1 white — stand-in for "no real texture"

    public MeshRenderer(GraphicsDevice gd, Effect litSurfaceEffect)
    {
        _gd               = gd;
        _litSurfaceEffect = litSurfaceEffect;
        _whiteTexture     = new Texture2D(gd, 1, 1);
        _whiteTexture.SetData([Color.White]);

        // LitSurface.fx declares EclipseFactor with a "= 1.0" HLSL initializer, but on
        // DesktopGL/MojoShader that initializer is not reliably applied — the parameter can
        // come up 0, silently zeroing the sun term everywhere (BakedColorLit) or the whole
        // lighting factor (DynamicLit). Project policy: never rely on an .fx initializer
        // default — every parameter a technique reads gets an explicit C# set. EclipseFactor
        // is invariant through Phase A (no eclipse term exists yet), so setting it once here
        // is enough; Phase E will set it per draw call once it varies.
        _litSurfaceEffect.Parameters["EclipseFactor"].SetValue(1.0f);
    }

    /// <summary>
    /// Real-time lit geometry (ships, containers, station hull box). materialColor is the flat
    /// per-draw tint (hull/ship use it, containers leave it White and vary vertex colour
    /// instead); texture defaults to a 1x1 white pixel when the mesh has no real texture.
    /// </summary>
    public void DrawDynamicLit(
        VertexBuffer vb, IndexBuffer ib,
        Matrix world, Matrix view, Matrix projection,
        Color materialColor, Vector3 sunDirection, Color sunColour, float ambient,
        Texture2D? texture = null)
    {
        var fx = _litSurfaceEffect;
        fx.CurrentTechnique = fx.Techniques["DynamicLit"];
        fx.Parameters["World"].SetValue(world);
        fx.Parameters["View"].SetValue(view);
        fx.Parameters["Projection"].SetValue(projection);
        fx.Parameters["SunDirection"].SetValue(sunDirection);
        fx.Parameters["SunColour"].SetValue(sunColour.ToVector3());
        fx.Parameters["Ambient"].SetValue(ambient);
        fx.Parameters["MaterialColor"].SetValue(materialColor.ToVector3());
        fx.Parameters["Texture"].SetValue(texture ?? _whiteTexture);
        Draw(vb, ib, fx);
    }

    /// <summary>
    /// Pre-baked (albedo x AO) vertex-colour geometry (station decoration). Vertex alpha is
    /// the self-illumination floor S — see StationModuleMesh.ApplyIlluminationFlags.
    /// </summary>
    public void DrawBakedColorLit(
        VertexBuffer vb, IndexBuffer ib,
        Matrix world, Matrix view, Matrix projection,
        Vector3 sunDirection, Color sunColour, float ambient,
        Texture2D texture)
    {
        var fx = _litSurfaceEffect;
        fx.CurrentTechnique = fx.Techniques["BakedColorLit"];
        fx.Parameters["World"].SetValue(world);
        fx.Parameters["View"].SetValue(view);
        fx.Parameters["Projection"].SetValue(projection);
        fx.Parameters["SunDirection"].SetValue(sunDirection);
        fx.Parameters["SunColour"].SetValue(sunColour.ToVector3());
        fx.Parameters["Ambient"].SetValue(ambient);
        fx.Parameters["Texture"].SetValue(texture);
        Draw(vb, ib, fx);
    }

    public void Dispose() => _whiteTexture.Dispose();

    // ── Private ───────────────────────────────────────────────────────────────

    private void Draw(VertexBuffer vb, IndexBuffer ib, Effect effect)
    {
        var gd = _gd;
        gd.SetVertexBuffer(vb);
        gd.Indices            = ib;
        gd.RasterizerState    = RasterizerState.CullCounterClockwise;
        gd.DepthStencilState  = DepthStencilState.Default;

        foreach (var pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, ib.IndexCount / 3);
        }
    }
}
