using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.Rendering;

/// <summary>
/// Generates reusable 3D meshes uploaded to the GPU.
/// Create once per mesh type; reuse every frame by changing BasicEffect.World.
/// </summary>
public static class MeshFactory
{
    // ── Sphere ────────────────────────────────────────────────────────────────

    /// <summary>
    /// UV sphere of radius 1.0 centred at origin.
    /// Scale via <c>BasicEffect.World = Matrix.CreateScale(radius) * ...</c>
    /// Uses VertexPositionNormalTexture so BasicEffect lighting works.
    /// Texture coords are generated but ignored — use DiffuseColor instead.
    /// </summary>
    public static (VertexBuffer vb, IndexBuffer ib) CreateSphere(
        GraphicsDevice gd, int rings = 24, int segments = 24)
    {
        int vertCount  = (rings + 1) * (segments + 1);
        int indexCount = rings * segments * 6;

        var verts   = new VertexPositionNormalTexture[vertCount];
        var indices = new int[indexCount];

        int v = 0;
        for (int ring = 0; ring <= rings; ring++)
        {
            float phi = MathF.PI * ring / rings;

            for (int seg = 0; seg <= segments; seg++)
            {
                float theta = MathF.PI * 2f * seg / segments;

                float x      = MathF.Sin(phi) * MathF.Cos(theta);
                float y      = MathF.Cos(phi);
                float z      = MathF.Sin(phi) * MathF.Sin(theta);
                var   normal = new Vector3(x, y, z);

                verts[v++] = new VertexPositionNormalTexture(
                    normal, normal,
                    new Vector2((float)seg / segments, (float)ring / rings));
            }
        }

        int idx = 0;
        for (int ring = 0; ring < rings; ring++)
        {
            for (int seg = 0; seg < segments; seg++)
            {
                int a = ring       * (segments + 1) + seg;
                int b = (ring + 1) * (segments + 1) + seg;
                int c = (ring + 1) * (segments + 1) + seg + 1;
                int d = ring       * (segments + 1) + seg + 1;

                indices[idx++] = a; indices[idx++] = b; indices[idx++] = c;
                indices[idx++] = a; indices[idx++] = c; indices[idx++] = d;
            }
        }

        var vb = new VertexBuffer(gd, VertexPositionNormalTexture.VertexDeclaration,
                                  vertCount, BufferUsage.WriteOnly);
        vb.SetData(verts);

        var ib = new IndexBuffer(gd, IndexElementSize.ThirtyTwoBits,
                                 indexCount, BufferUsage.WriteOnly);
        ib.SetData(indices);

        return (vb, ib);
    }

    // ── Ring (orbit path) ─────────────────────────────────────────────────────

    /// <summary>
    /// Flat ring of line vertices in the XZ plane, radius 1.0.
    /// Draw with <c>PrimitiveType.LineStrip</c>; last vertex closes the loop.
    /// Color is set per-draw by overwriting the Color field on each vertex.
    /// </summary>
    public static VertexPositionColor[] CreateRingVertices(int segments = 128)
    {
        var verts = new VertexPositionColor[segments + 1];
        for (int i = 0; i <= segments; i++)
        {
            float angle = MathF.PI * 2f * i / segments;
            verts[i] = new VertexPositionColor(
                new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle)),
                Color.White);
        }
        return verts;
    }

    // ── Billboard quad ────────────────────────────────────────────────────────

    /// <summary>
    /// Unit quad in the XY plane centred at origin.
    /// Placeholder for 3D billboard rendering — use SpriteBatch for screen-space glows.
    /// </summary>
    public static VertexPositionNormalTexture[] CreateQuad() =>
    [
        new(new Vector3(-0.5f,  0.5f, 0), Vector3.UnitZ, new Vector2(0, 0)),
        new(new Vector3( 0.5f,  0.5f, 0), Vector3.UnitZ, new Vector2(1, 0)),
        new(new Vector3(-0.5f, -0.5f, 0), Vector3.UnitZ, new Vector2(0, 1)),
        new(new Vector3( 0.5f, -0.5f, 0), Vector3.UnitZ, new Vector2(1, 1)),
    ];
}
