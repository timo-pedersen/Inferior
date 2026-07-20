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

    public void DrawDynamicLitRange(
        VertexBuffer vb, IndexBuffer ib,
        int startIndex, int indexCount,
        Matrix world, Matrix view, Matrix projection,
        Color materialColor, Vector3 sunDirection, Color sunColour, float ambient,
        Texture2D? texture = null)
    {
        if (startIndex < 0 || indexCount <= 0 || startIndex + indexCount > ib.IndexCount || indexCount % 3 != 0)
            throw new ArgumentOutOfRangeException(nameof(indexCount), "The indexed triangle range must lie within the index buffer.");

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
        Draw(vb, ib, fx, startIndex, indexCount / 3);
    }

    public void DrawDynamicLitShadowed(
        VertexBuffer vb, IndexBuffer ib,
        Matrix world, Matrix view, Matrix projection,
        Color materialColor, Vector3 sunDirection, Color sunColour, float ambient,
        Texture2D texture, Texture2D shadowMap,
        Matrix moduleToStationLocal, Matrix stationLocalToLightView,
        Vector2 shadowMinXY, Vector2 shadowInvSize,
        float shadowNear, float shadowDepthSpan, Vector2 shadowTexelSize,
        float shadowCorrectionLimit, float shadowBiasDepth,
        bool binaryShadowView, bool deltaShadowView)
    {
        var fx = _litSurfaceEffect;
        fx.CurrentTechnique = fx.Techniques["DynamicLitShadowed"];
        fx.Parameters["World"].SetValue(world);
        fx.Parameters["View"].SetValue(view);
        fx.Parameters["Projection"].SetValue(projection);
        fx.Parameters["SunDirection"].SetValue(sunDirection);
        fx.Parameters["SunColour"].SetValue(sunColour.ToVector3());
        fx.Parameters["Ambient"].SetValue(ambient);
        fx.Parameters["MaterialColor"].SetValue(materialColor.ToVector3());
        fx.Parameters["Texture"].SetValue(texture);
        SetShadowParameters(fx, shadowMap, moduleToStationLocal, stationLocalToLightView,
            shadowMinXY, shadowInvSize, shadowNear, shadowDepthSpan, shadowTexelSize,
            shadowCorrectionLimit, shadowBiasDepth, binaryShadowView, deltaShadowView);
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

    public void DrawBakedColorLitShadowed(
        VertexBuffer vb, IndexBuffer ib,
        Matrix world, Matrix view, Matrix projection,
        Vector3 sunDirection, Color sunColour, float ambient,
        Texture2D texture, Texture2D shadowMap,
        Matrix moduleToStationLocal, Matrix stationLocalToLightView,
        Vector2 shadowMinXY, Vector2 shadowInvSize,
        float shadowNear, float shadowDepthSpan, Vector2 shadowTexelSize,
        float shadowCorrectionLimit, float shadowBiasDepth,
        bool binaryShadowView, bool deltaShadowView)
    {
        var fx = _litSurfaceEffect;
        fx.CurrentTechnique = fx.Techniques["BakedColorLitShadowed"];
        fx.Parameters["World"].SetValue(world);
        fx.Parameters["View"].SetValue(view);
        fx.Parameters["Projection"].SetValue(projection);
        fx.Parameters["SunDirection"].SetValue(sunDirection);
        fx.Parameters["SunColour"].SetValue(sunColour.ToVector3());
        fx.Parameters["Ambient"].SetValue(ambient);
        fx.Parameters["Texture"].SetValue(texture);
        SetShadowParameters(fx, shadowMap, moduleToStationLocal, stationLocalToLightView,
            shadowMinXY, shadowInvSize, shadowNear, shadowDepthSpan, shadowTexelSize,
            shadowCorrectionLimit, shadowBiasDepth, binaryShadowView, deltaShadowView);
        Draw(vb, ib, fx);
    }

    public void Dispose() => _whiteTexture.Dispose();

    // ── Private ───────────────────────────────────────────────────────────────

    private void Draw(
        VertexBuffer vb,
        IndexBuffer ib,
        Effect effect,
        int startIndex = 0,
        int? primitiveCount = null)
    {
        var gd = _gd;
        gd.SetVertexBuffer(vb);
        gd.Indices            = ib;
        gd.RasterizerState    = RasterizerState.CullCounterClockwise;
        gd.DepthStencilState  = DepthStencilState.Default;

        foreach (var pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            gd.DrawIndexedPrimitives(
                PrimitiveType.TriangleList,
                baseVertex: 0,
                startIndex,
                primitiveCount ?? ib.IndexCount / 3);
        }
    }

    private static void SetShadowParameters(
        Effect fx, Texture2D shadowMap,
        Matrix moduleToStationLocal, Matrix stationLocalToLightView,
        Vector2 shadowMinXY, Vector2 shadowInvSize,
        float shadowNear, float shadowDepthSpan, Vector2 shadowTexelSize,
        float shadowCorrectionLimit, float shadowBiasDepth,
        bool binaryShadowView, bool deltaShadowView)
    {
        fx.Parameters["ShadowMap"].SetValue(shadowMap);
        fx.Parameters["ModuleToStationLocal"].SetValue(moduleToStationLocal);
        fx.Parameters["StationLocalToLightView"].SetValue(stationLocalToLightView);
        fx.Parameters["ShadowMinXY"].SetValue(shadowMinXY);
        fx.Parameters["ShadowInvSize"].SetValue(shadowInvSize);
        fx.Parameters["ShadowNear"].SetValue(shadowNear);
        fx.Parameters["ShadowDepthSpan"].SetValue(shadowDepthSpan);
        fx.Parameters["ShadowTexelSize"].SetValue(shadowTexelSize);
        fx.Parameters["ShadowCorrectionLimit"].SetValue(shadowCorrectionLimit);
        // No .fx initializer (project policy since the EclipseFactor incident) — always
        // set explicitly, even though it's computed fresh from the same constant every
        // single draw call right now (SystemSpaceState.Stations.cs).
        fx.Parameters["ShadowBiasDepth"].SetValue(shadowBiasDepth);
        fx.Parameters["ShadowBinaryView"].SetValue(binaryShadowView ? 1.0f : 0.0f);
        fx.Parameters["ShadowDeltaView"].SetValue(deltaShadowView ? 1.0f : 0.0f);
    }
}
