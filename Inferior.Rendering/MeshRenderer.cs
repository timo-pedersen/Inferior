using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.Rendering;

/// <summary>
/// Unified 3D draw call with two modes:
///   Baked   — pre-lit vertex-colour geometry (VertexPositionColorTexture); no dynamic lighting.
///   Dynamic — real-time lit geometry (VertexPositionNormalTexture); star as directional light.
///
/// Sets RasterizerState.CullCounterClockwiseFace explicitly — no CullNone workaround needed
/// when geometry is wound correctly by GeometryBuilder.
/// </summary>
public sealed class MeshRenderer : IDisposable
{
    private readonly BasicEffect _effect;

    public MeshRenderer(GraphicsDevice gd)
    {
        _effect = new BasicEffect(gd);
    }

    /// <summary>
    /// Pre-baked vertex-colour geometry (stations, pre-lit meshes).
    /// Ignores dynamic lighting. Vertex buffer must use VertexPositionColorTexture.
    /// </summary>
    public void DrawBaked(
        VertexBuffer vb, IndexBuffer ib,
        Matrix world, Matrix view, Matrix projection)
    {
        _effect.VertexColorEnabled = true;
        _effect.LightingEnabled    = false;
        _effect.TextureEnabled     = false;
        _effect.World              = world;
        _effect.View               = view;
        _effect.Projection         = projection;
        Draw(vb, ib);
    }

    /// <summary>
    /// Dynamically lit geometry (containers, ships).
    /// Vertex buffer must use VertexPositionNormalTexture.
    /// </summary>
    public void DrawDynamic(
        VertexBuffer vb, IndexBuffer ib,
        Matrix world, Matrix view, Matrix projection,
        Color baseColour,
        Vector3 sunDirection, Color sunColour)
    {
        _effect.VertexColorEnabled             = false;
        _effect.LightingEnabled                = true;
        _effect.TextureEnabled                 = false;
        _effect.DirectionalLight0.Enabled      = true;
        _effect.DirectionalLight0.Direction    = -sunDirection;
        _effect.DirectionalLight0.DiffuseColor = sunColour.ToVector3();
        _effect.DirectionalLight1.Enabled      = false;
        _effect.DirectionalLight2.Enabled      = false;
        _effect.AmbientLightColor              = new Vector3(0.09f);
        _effect.DiffuseColor                   = baseColour.ToVector3();
        _effect.Alpha                          = 1f;
        _effect.World                          = world;
        _effect.View                           = view;
        _effect.Projection                     = projection;
        Draw(vb, ib);
    }

    public void Dispose() => _effect.Dispose();

    // ── Private ───────────────────────────────────────────────────────────────

    private void Draw(VertexBuffer vb, IndexBuffer ib)
    {
        var gd = _effect.GraphicsDevice;
        gd.SetVertexBuffer(vb);
        gd.Indices            = ib;
        gd.RasterizerState    = RasterizerState.CullCounterClockwise;
        gd.DepthStencilState  = DepthStencilState.Default;

        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, ib.IndexCount / 3);
        }
    }
}
