using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.Rendering;

/// <summary>
/// Accumulates triangles for a mesh, corrects winding automatically, then produces
/// GPU buffers via BuildDynamic (normals, dynamic lighting) or BuildBaked (vertex colour).
///
/// Use AddConvexFace for any closed convex mesh centred at its origin — the centroid
/// direction reliably points outward.  Use AddFace(outwardNormal) for non-convex surfaces
/// or any face where the centroid-from-origin heuristic would be wrong.
/// </summary>
public sealed class GeometryBuilder
{
    private readonly List<(Vector3 v0, Vector3 v1, Vector3 v2)> _tris = [];

    // ── Convex faces (closed mesh, origin at centre) ──────────────────────────

    public void AddConvexFace(Vector3 v0, Vector3 v1, Vector3 v2)
        => AddTriangleConvex(v0, v1, v2);

    public void AddConvexFace(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3)
    {
        AddTriangleConvex(v0, v1, v2);
        AddTriangleConvex(v0, v2, v3);
    }

    // ── Explicit normal faces (non-convex / open surfaces) ───────────────────

    public void AddFace(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 outwardNormal)
        => AddTriangle(v0, v1, v2, outwardNormal);

    public void AddFace(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, Vector3 outwardNormal)
    {
        AddTriangle(v0, v1, v2, outwardNormal);
        AddTriangle(v0, v2, v3, outwardNormal);
    }

    // ── Build ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Dynamic (real-time lit) mesh: VertexPositionNormalColorTexture, flat-shaded face normals,
    /// vertex colour baked White (this method's tint is applied per draw call via
    /// MeshRenderer.DrawDynamicLit's materialColor — baseColour is accepted for API parity with
    /// callers but intentionally not baked here, unchanged from before this mesh format migrated).
    /// </summary>
    public (VertexBuffer vb, IndexBuffer ib) BuildDynamic(GraphicsDevice gd, Color baseColour = default)
    {
        int triCount   = _tris.Count;
        int vertCount  = triCount * 3;
        int indexCount = triCount * 3;

        var verts   = new VertexPositionNormalColorTexture[vertCount];
        var indices = new int[indexCount];

        for (int t = 0; t < triCount; t++)
        {
            var (p0, p1, p2) = _tris[t];
            // AddTriangle stores vertices so Cross(p1-p0, p2-p0) points outward (CCW from outside).
            // DirectX/MonoGame CullCounterClockwise culls CCW faces, so emit indices as (0,2,1)
            // to render CW-from-outside front faces while keeping the outward normal correct.
            Vector3 normal = Vector3.Normalize(Vector3.Cross(p1 - p0, p2 - p0));
            int v = t * 3;
            verts[v]     = new VertexPositionNormalColorTexture(p0, normal, Color.White, Vector2.Zero);
            verts[v + 1] = new VertexPositionNormalColorTexture(p1, normal, Color.White, Vector2.Zero);
            verts[v + 2] = new VertexPositionNormalColorTexture(p2, normal, Color.White, Vector2.Zero);
            indices[v]     = v;
            indices[v + 1] = v + 2;  // swap 1↔2 → CW from outside = visible front face
            indices[v + 2] = v + 1;
        }

        var vb = new VertexBuffer(gd, VertexPositionNormalColorTexture.VertexDeclaration, vertCount, BufferUsage.WriteOnly);
        vb.SetData(verts);
        var ib = new IndexBuffer(gd, IndexElementSize.ThirtyTwoBits, indexCount, BufferUsage.WriteOnly);
        ib.SetData(indices);

        return (vb, ib);
    }

    /// <summary>
    /// Baked (pre-lit) mesh: VertexPositionColor with all vertices initialised to White.
    /// Caller applies lighting to the colour array before the GPU upload if needed.
    /// </summary>
    public (VertexBuffer vb, IndexBuffer ib) BuildBaked(GraphicsDevice gd)
    {
        int triCount   = _tris.Count;
        int vertCount  = triCount * 3;
        int indexCount = triCount * 3;

        var verts   = new VertexPositionColor[vertCount];
        var indices = new int[indexCount];

        for (int t = 0; t < triCount; t++)
        {
            var (p0, p1, p2) = _tris[t];
            int v = t * 3;
            verts[v]     = new VertexPositionColor(p0, Color.White);
            verts[v + 1] = new VertexPositionColor(p1, Color.White);
            verts[v + 2] = new VertexPositionColor(p2, Color.White);
            indices[v]     = v;
            indices[v + 1] = v + 2;  // swap 1↔2 → CW from outside = visible front face
            indices[v + 2] = v + 1;
        }

        var vb = new VertexBuffer(gd, VertexPositionColor.VertexDeclaration, vertCount, BufferUsage.WriteOnly);
        vb.SetData(verts);
        var ib = new IndexBuffer(gd, IndexElementSize.ThirtyTwoBits, indexCount, BufferUsage.WriteOnly);
        ib.SetData(indices);

        return (vb, ib);
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    private void AddTriangleConvex(Vector3 v0, Vector3 v1, Vector3 v2)
    {
        // For convex meshes centred at origin, the face centroid IS the outward direction.
        Vector3 centroid = (v0 + v1 + v2) / 3f;
        AddTriangle(v0, v1, v2, centroid);
    }

    private void AddTriangle(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 outwardNormal)
    {
        Vector3 computed = Vector3.Cross(v1 - v0, v2 - v0);
        if (Vector3.Dot(computed, outwardNormal) < 0)
            (v1, v2) = (v2, v1);
        _tris.Add((v0, v1, v2));
    }
}
