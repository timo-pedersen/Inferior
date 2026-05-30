using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.Game;

/// <summary>
/// Generates reusable 3D meshes.
/// All meshes live on the GPU (VertexBuffer / IndexBuffer).
/// Create once, reuse for every planet/moon by changing BasicEffect.World.
/// </summary>
public static class MeshFactory
{
    // ── Sphere ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Create a UV sphere of radius 1.0 centred at origin.
    /// Scale via BasicEffect.World = Matrix.CreateScale(radius) * ...
    ///
    /// Uses VertexPositionNormalTexture so BasicEffect lighting works.
    /// Texture coords are generated but you can ignore them — just use DiffuseColor.
    /// </summary>
    public static (VertexBuffer vb, IndexBuffer ib) CreateSphere(
        GraphicsDevice gd, int rings = 24, int segments = 24)
    {
        int vertCount  = (rings + 1) * (segments + 1);
        int indexCount = rings * segments * 6;

        var verts   = new VertexPositionNormalTexture[vertCount];
        var indices = new int[indexCount];

        // Generate vertices — lat/lon grid
        int v = 0;
        for (int ring = 0; ring <= rings; ring++)
        {
            float phi = MathF.PI * ring / rings; // 0 = top, π = bottom

            for (int seg = 0; seg <= segments; seg++)
            {
                float theta = MathF.PI * 2f * seg / segments;

                float x = MathF.Sin(phi) * MathF.Cos(theta);
                float y = MathF.Cos(phi);
                float z = MathF.Sin(phi) * MathF.Sin(theta);

                var normal = new Vector3(x, y, z);

                verts[v++] = new VertexPositionNormalTexture(
                    normal,           // position = normal for unit sphere
                    normal,           // normal = same
                    new Vector2((float)seg / segments, (float)ring / rings));
            }
        }

        // Generate indices — two triangles per quad
        int idx = 0;
        for (int ring = 0; ring < rings; ring++)
        {
            for (int seg = 0; seg < segments; seg++)
            {
                int a = ring       * (segments + 1) + seg;
                int b = (ring + 1) * (segments + 1) + seg;
                int c = (ring + 1) * (segments + 1) + seg + 1;
                int d = ring       * (segments + 1) + seg + 1;

                // Triangle 1
                indices[idx++] = a;
                indices[idx++] = b;
                indices[idx++] = c;

                // Triangle 2
                indices[idx++] = a;
                indices[idx++] = c;
                indices[idx++] = d;
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
    /// Build a flat ring of line vertices in the XZ plane, radius 1.0.
    /// Scale via multiplication before drawing.
    ///
    /// Returns VertexPositionColor[] — draw with PrimitiveType.LineStrip,
    /// passing segments+1 vertices (last = first to close the loop).
    /// </summary>
    public static VertexPositionColor[] CreateRingVertices(int segments = 128)
    {
        var verts = new VertexPositionColor[segments + 1];
        for (int i = 0; i <= segments; i++)
        {
            float angle = MathF.PI * 2f * i / segments;
            verts[i] = new VertexPositionColor(
                new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle)),
                Color.White); // color set per-draw
        }
        return verts;
    }

    // ── Billboard quad ────────────────────────────────────────────────────────

    /// <summary>
    /// A unit quad in the XY plane, centred at origin.
    /// Used for star glow sprites.
    /// Draw with SpriteBatch in screen space instead for simplicity —
    /// this is here if you ever want a proper 3D billboard.
    /// </summary>
    public static VertexPositionNormalTexture[] CreateQuad()
    {
        return
        [
            new(new Vector3(-0.5f,  0.5f, 0), Vector3.UnitZ, new Vector2(0, 0)),
            new(new Vector3( 0.5f,  0.5f, 0), Vector3.UnitZ, new Vector2(1, 0)),
            new(new Vector3(-0.5f, -0.5f, 0), Vector3.UnitZ, new Vector2(0, 1)),
            new(new Vector3( 0.5f, -0.5f, 0), Vector3.UnitZ, new Vector2(1, 1)),
        ];
    }
}
