using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.Rendering;

/// <summary>
/// Optional receiver-local distance haze for a bounded architectural volume. A zero
/// activation is the exact off state used by every established caller.
/// </summary>
public readonly record struct InteriorHazeParameters(
    float Activation,
    Vector3 VolumeMinimum,
    Vector3 VolumeMaximum,
    Vector3 Colour,
    float StartDistanceMetres,
    float EndDistanceMetres,
    float MaximumBlend)
{
    public static InteriorHazeParameters Disabled => default;
}

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
    public static Color WhiteFallbackColor => Color.White;
    public static Color StationFallbackMaterialColor => new(128, 255, 0, 0);

    private readonly GraphicsDevice _gd;
    private readonly Effect         _litSurfaceEffect;
    private readonly Texture2D      _whiteTexture;   // 1x1 white — stand-in for "no real texture"
    // Brief S2c-1: stand-in for "no real material map" (ships, containers, calibration
    // cube — none of them have per-texel gloss). Height=128 (neutral, S2c-2's channel),
    // gloss=255 (full) so SpecularHighlight's gloss multiply is a no-op — these callers'
    // specular reproduces exactly pre-S2c-1 behaviour, untouched by this brief.
    private readonly Texture2D      _neutralMaterialTexture;
    private readonly Texture2D      _stationFallbackMaterialTexture;

    public Texture2D WhiteFallbackTexture => _whiteTexture;
    public Texture2D StationFallbackMaterialTexture => _stationFallbackMaterialTexture;

    public MeshRenderer(GraphicsDevice gd, Effect litSurfaceEffect)
    {
        _gd               = gd;
        _litSurfaceEffect = litSurfaceEffect;
        _whiteTexture     = new Texture2D(gd, 1, 1);
        _whiteTexture.SetData([WhiteFallbackColor]);
        _neutralMaterialTexture = new Texture2D(gd, 1, 1);
        _neutralMaterialTexture.SetData([new Color(128, 255, 0, 255)]);
        // Station megastations deliberately use reserved B/A = 0. Keep this exact immutable
        // fallback distinct from the general renderer neutral map above, whose alpha is 255.
        _stationFallbackMaterialTexture = new Texture2D(gd, 1, 1);
        _stationFallbackMaterialTexture.SetData([StationFallbackMaterialColor]);

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
        float specularStrength, float specularShininess,
        Texture2D? texture = null, Texture2D? materialMap = null, float bumpStrength = 0f,
        Vector3? eyePositionWorld = null,
        float vertexIlluminationScale = 0f,
        InteriorHazeParameters interiorHaze = default,
        Matrix? moduleToStationLocal = null)
    {
        var fx = _litSurfaceEffect;
        fx.CurrentTechnique = fx.Techniques["DynamicLit"];
        SetCoreParameters(fx, world, view, projection, materialColor, sunDirection, sunColour,
            ambient, texture ?? _whiteTexture, vertexIlluminationScale);
        fx.Parameters["ModuleToStationLocal"].SetValue(moduleToStationLocal ?? Matrix.Identity);
        SetInteriorHazeParameters(fx, interiorHaze);
        SetSpecularParameters(fx, specularStrength, specularShininess, materialMap ?? _neutralMaterialTexture, bumpStrength, eyePositionWorld ?? Vector3.Zero);
        Draw(vb, ib, fx);
    }

    public void DrawDynamicLitRange(
        VertexBuffer vb, IndexBuffer ib,
        int startIndex, int indexCount,
        Matrix world, Matrix view, Matrix projection,
        Color materialColor, Vector3 sunDirection, Color sunColour, float ambient,
        float specularStrength, float specularShininess,
        Texture2D? texture = null, Texture2D? materialMap = null, float bumpStrength = 0f,
        Vector3? eyePositionWorld = null,
        float vertexIlluminationScale = 0f,
        float presentationDepthBias = 0f,
        InteriorHazeParameters interiorHaze = default,
        Matrix? moduleToStationLocal = null)
    {
        if (startIndex < 0 || indexCount <= 0 || startIndex + indexCount > ib.IndexCount || indexCount % 3 != 0)
            throw new ArgumentOutOfRangeException(nameof(indexCount), "The indexed triangle range must lie within the index buffer.");

        var fx = _litSurfaceEffect;
        fx.CurrentTechnique = fx.Techniques["DynamicLit"];
        SetCoreParameters(fx, world, view, projection, materialColor, sunDirection, sunColour,
            ambient, texture ?? _whiteTexture, vertexIlluminationScale);
        fx.Parameters["ModuleToStationLocal"].SetValue(moduleToStationLocal ?? Matrix.Identity);
        SetInteriorHazeParameters(fx, interiorHaze);
        SetSpecularParameters(fx, specularStrength, specularShininess, materialMap ?? _neutralMaterialTexture, bumpStrength, eyePositionWorld ?? Vector3.Zero);
        Draw(vb, ib, fx, startIndex, indexCount / 3, presentationDepthBias);
    }

    public void DrawDebugFlatColorRange(
        VertexBuffer vb,
        IndexBuffer ib,
        int startIndex,
        int indexCount,
        Matrix world,
        Matrix view,
        Matrix projection,
        Color color)
    {
        if (startIndex < 0 || indexCount <= 0 || startIndex + indexCount > ib.IndexCount || indexCount % 3 != 0)
            throw new ArgumentOutOfRangeException(nameof(indexCount), "The indexed triangle range must lie within the index buffer.");

        var fx = _litSurfaceEffect;
        fx.CurrentTechnique = fx.Techniques["DebugFlatColor"];
        fx.Parameters["World"].SetValue(world);
        fx.Parameters["View"].SetValue(view);
        fx.Parameters["Projection"].SetValue(projection);
        fx.Parameters["DebugFlatColor"].SetValue(color.ToVector3());
        Draw(vb, ib, fx, startIndex, indexCount / 3);
    }

    public void DrawDynamicLitShadowed(
        VertexBuffer vb, IndexBuffer ib,
        Matrix world, Matrix view, Matrix projection,
        Color materialColor, Vector3 sunDirection, Color sunColour, float ambient,
        float specularStrength, float specularShininess,
        Texture2D texture, Texture2D shadowMap,
        Matrix moduleToStationLocal, Matrix stationLocalToLightView,
        Vector2 shadowMinXY, Vector2 shadowInvSize,
        float shadowNear, float shadowDepthSpan, Vector2 shadowTexelSize,
        float shadowCorrectionLimit, float shadowBiasDepth,
        bool binaryShadowView, bool deltaShadowView, int shadowKernelRadius,
        Texture2D? materialMap = null, float bumpStrength = 0f,
        Vector3? eyePositionWorld = null,
        float vertexIlluminationScale = 0f,
        InteriorHazeParameters interiorHaze = default)
    {
        var fx = _litSurfaceEffect;
        fx.CurrentTechnique = fx.Techniques["DynamicLitShadowed"];
        SetCoreParameters(fx, world, view, projection, materialColor, sunDirection, sunColour,
            ambient, texture, vertexIlluminationScale);
        SetInteriorHazeParameters(fx, interiorHaze);
        SetSpecularParameters(fx, specularStrength, specularShininess, materialMap ?? _neutralMaterialTexture, bumpStrength, eyePositionWorld ?? Vector3.Zero);
        SetShadowParameters(fx, shadowMap, moduleToStationLocal, stationLocalToLightView,
            shadowMinXY, shadowInvSize, shadowNear, shadowDepthSpan, shadowTexelSize,
            shadowCorrectionLimit, shadowBiasDepth, binaryShadowView, deltaShadowView,
            shadowKernelRadius);
        Draw(vb, ib, fx);
    }

    public void DrawDynamicLitShadowedRange(
        VertexBuffer vb, IndexBuffer ib,
        int startIndex, int indexCount,
        Matrix world, Matrix view, Matrix projection,
        Color materialColor, Vector3 sunDirection, Color sunColour, float ambient,
        float specularStrength, float specularShininess,
        Texture2D texture, Texture2D shadowMap,
        Matrix moduleToStationLocal, Matrix stationLocalToLightView,
        Vector2 shadowMinXY, Vector2 shadowInvSize,
        float shadowNear, float shadowDepthSpan, Vector2 shadowTexelSize,
        float shadowCorrectionLimit, float shadowBiasDepth,
        bool binaryShadowView, bool deltaShadowView, int shadowKernelRadius,
        Texture2D? materialMap = null, float bumpStrength = 0f,
        Vector3? eyePositionWorld = null,
        float vertexIlluminationScale = 0f,
        float presentationDepthBias = 0f,
        InteriorHazeParameters interiorHaze = default)
    {
        if (startIndex < 0 || indexCount <= 0
            || startIndex + indexCount > ib.IndexCount || indexCount % 3 != 0)
            throw new ArgumentOutOfRangeException(nameof(indexCount),
                "The indexed triangle range must lie within the index buffer.");

        var fx = _litSurfaceEffect;
        fx.CurrentTechnique = fx.Techniques["DynamicLitShadowed"];
        SetCoreParameters(fx, world, view, projection, materialColor, sunDirection, sunColour,
            ambient, texture, vertexIlluminationScale);
        SetInteriorHazeParameters(fx, interiorHaze);
        SetSpecularParameters(fx, specularStrength, specularShininess,
            materialMap ?? _neutralMaterialTexture, bumpStrength,
            eyePositionWorld ?? Vector3.Zero);
        SetShadowParameters(fx, shadowMap, moduleToStationLocal, stationLocalToLightView,
            shadowMinXY, shadowInvSize, shadowNear, shadowDepthSpan, shadowTexelSize,
            shadowCorrectionLimit, shadowBiasDepth, binaryShadowView, deltaShadowView,
            shadowKernelRadius);
        Draw(vb, ib, fx, startIndex, indexCount / 3, presentationDepthBias);
    }

    /// <summary>
    /// Pre-baked (albedo x AO) vertex-colour geometry (station decoration). Vertex alpha is
    /// the self-illumination floor S — see StationModuleMesh.ApplyIlluminationFlags.
    /// decorationBrightness is Brief B4's live shader multiplier
    /// (StationBrightnessTuning.DecorationBrightnessMultiplier) — no default here, same "no
    /// .fx initializer" policy as every other tunable: the caller always passes the current
    /// live value explicitly.
    /// </summary>
    public void DrawBakedColorLit(
        VertexBuffer vb, IndexBuffer ib,
        Matrix world, Matrix view, Matrix projection,
        Vector3 sunDirection, Color sunColour, float ambient,
        Texture2D texture, float decorationBrightness)
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
        fx.Parameters["DecorationBrightness"].SetValue(decorationBrightness);
        Draw(vb, ib, fx);
    }

    public void DrawBakedColorLitShadowed(
        VertexBuffer vb, IndexBuffer ib,
        Matrix world, Matrix view, Matrix projection,
        Vector3 sunDirection, Color sunColour, float ambient,
        Texture2D texture, float decorationBrightness, Texture2D shadowMap,
        Matrix moduleToStationLocal, Matrix stationLocalToLightView,
        Vector2 shadowMinXY, Vector2 shadowInvSize,
        float shadowNear, float shadowDepthSpan, Vector2 shadowTexelSize,
        float shadowCorrectionLimit, float shadowBiasDepth,
        bool binaryShadowView, bool deltaShadowView, int shadowKernelRadius)
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
        fx.Parameters["DecorationBrightness"].SetValue(decorationBrightness);
        SetShadowParameters(fx, shadowMap, moduleToStationLocal, stationLocalToLightView,
            shadowMinXY, shadowInvSize, shadowNear, shadowDepthSpan, shadowTexelSize,
            shadowCorrectionLimit, shadowBiasDepth, binaryShadowView, deltaShadowView,
            shadowKernelRadius);
        Draw(vb, ib, fx);
    }

    public void Dispose()
    {
        _whiteTexture.Dispose();
        _neutralMaterialTexture.Dispose();
        _stationFallbackMaterialTexture.Dispose();
    }

    // ── Private ───────────────────────────────────────────────────────────────

    // A1 inventory finding: DrawDynamicLit/DrawDynamicLitRange/DrawDynamicLitShadowed/
    // DrawDynamicLitShadowedRange each restated this same 9-parameter block. Deliberately
    // excludes ModuleToStationLocal (set separately per caller — the non-shadowed variants
    // default it to Identity, the shadowed variants set it via SetShadowParameters instead)
    // and does not cover the BakedColorLit techniques, whose parameter set genuinely
    // differs (DecorationBrightness instead of MaterialColor/VertexIlluminationScale).
    private static void SetCoreParameters(
        Effect fx, Matrix world, Matrix view, Matrix projection,
        Color materialColor, Vector3 sunDirection, Color sunColour, float ambient,
        Texture2D texture, float vertexIlluminationScale)
    {
        fx.Parameters["World"].SetValue(world);
        fx.Parameters["View"].SetValue(view);
        fx.Parameters["Projection"].SetValue(projection);
        fx.Parameters["SunDirection"].SetValue(sunDirection);
        fx.Parameters["SunColour"].SetValue(sunColour.ToVector3());
        fx.Parameters["Ambient"].SetValue(ambient);
        fx.Parameters["MaterialColor"].SetValue(materialColor.ToVector3());
        fx.Parameters["Texture"].SetValue(texture);
        fx.Parameters["VertexIlluminationScale"].SetValue(vertexIlluminationScale);
    }

    private static void SetInteriorHazeParameters(
        Effect effect,
        InteriorHazeParameters haze)
    {
        float distanceRange = haze.EndDistanceMetres - haze.StartDistanceMetres;
        effect.Parameters["InteriorHazeActivation"].SetValue(
            MathHelper.Clamp(haze.Activation, 0f, 1f));
        effect.Parameters["InteriorHazeVolumeMinimum"].SetValue(haze.VolumeMinimum);
        effect.Parameters["InteriorHazeVolumeMaximum"].SetValue(haze.VolumeMaximum);
        effect.Parameters["InteriorHazeColour"].SetValue(haze.Colour);
        effect.Parameters["InteriorHazeStartDistance"].SetValue(
            MathF.Max(haze.StartDistanceMetres, 0f));
        effect.Parameters["InteriorHazeInvDistanceRange"].SetValue(
            distanceRange > 1e-4f ? 1f / distanceRange : 0f);
        effect.Parameters["InteriorHazeMaximumBlend"].SetValue(
            MathHelper.Clamp(haze.MaximumBlend, 0f, 1f));
    }

    private void Draw(
        VertexBuffer vb,
        IndexBuffer ib,
        Effect effect,
        int startIndex = 0,
        int? primitiveCount = null,
        float presentationDepthBias = 0f)
    {
        var gd = _gd;
        gd.SetVertexBuffer(vb);
        gd.Indices            = ib;
        gd.RasterizerState    = RasterizerState.CullCounterClockwise;
        gd.DepthStencilState  = DepthStencilState.Default;
        effect.Parameters["PresentationDepthBias"].SetValue(presentationDepthBias);

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

    // Brief S1: DynamicLit*/station-hulls only (BakedColorLit*/station decoration is out
    // of scope until S2). SystemSpace draws use the origin-shifted Camera3D convention,
    // so their render-space eye remains Vector3.Zero. Offscreen previews may use a local
    // target camera instead; those callers pass the matching render-space eye explicitly.
    private static void SetSpecularParameters(Effect fx, float specularStrength, float specularShininess, Texture2D materialMap, float bumpStrength, Vector3 eyePositionWorld)
    {
        fx.Parameters["EyePositionWorld"].SetValue(eyePositionWorld);
        fx.Parameters["SpecularStrength"].SetValue(specularStrength);
        fx.Parameters["SpecularShininess"].SetValue(specularShininess);
        // Brief S2c-1: bound every DynamicLit*/station-hull draw call, no exceptions —
        // same "no .fx initializer" policy as the shadow parameters below (BakedColorLit*
        // never reads MaterialMap at all, so this is never set there).
        fx.Parameters["MaterialMap"].SetValue(materialMap);
        // Brief S2c-2: same policy — bound on every DynamicLit*/station-hull draw call.
        // Non-station callers pass 0 by default (see the three Draw* overloads above) and
        // are structurally immune to any value anyway (their MaterialMap is a flat 1x1
        // texture — see LitSurface.fx's PerturbNormalFromHeight comment).
        fx.Parameters["BumpStrength"].SetValue(bumpStrength);
    }

    private static void SetShadowParameters(
        Effect fx, Texture2D shadowMap,
        Matrix moduleToStationLocal, Matrix stationLocalToLightView,
        Vector2 shadowMinXY, Vector2 shadowInvSize,
        float shadowNear, float shadowDepthSpan, Vector2 shadowTexelSize,
        float shadowCorrectionLimit, float shadowBiasDepth,
        bool binaryShadowView, bool deltaShadowView, int shadowKernelRadius)
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
        // Step 2 (Brief E1): 0 = Off (1x1, byte-identical to Step 1), 1 = 3x3, 2 = 5x5.
        fx.Parameters["ShadowKernelRadius"].SetValue((float)shadowKernelRadius);
    }
}
