using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.Game.StationGen;

public enum AnimType { Steady, Strobe, Pulse }

// Links a range of decoration vertices to an animation type so the renderer
// can drive colour changes at runtime without re-uploading geometry.
public sealed class AnimTag
{
    public required AnimType Type       { get; init; }
    public required int      VertexBase { get; init; }  // index into mesh vertex array
    public required Color    OnColor    { get; init; }
    public required Color    OffColor   { get; init; }
    public          float    Period     { get; init; } = 1f;   // seconds per cycle
    public          float    Phase      { get; init; } = 0f;   // [0,1] offset
}

// CPU-side mesh accumulator for per-module decoration geometry.
// Vertices are in local module space (metres). Call Build() to produce GPU buffers.
// Uses VertexPositionColor — render with LightingEnabled=false, VertexColorEnabled=true.
// Lighting is baked into vertex colours by ApplyLighting() before Build().
public sealed class StationModuleMesh
{
    private readonly List<VertexPositionColor> _verts = [];
    private readonly List<int>                 _idx   = [];
    // Each entry covers one quad (4 consecutive vertices starting at vertexBase).
    private readonly List<(int vertexBase, int count)> _faces = [];

    public bool           IsEmpty  => _verts.Count == 0;
    public List<AnimTag>  AnimTags { get; } = [];

    // Adds a flat quad from four explicit corner vertices (CW from normal side).
    // Returns the index of v0 in the vertex array.
    public int AddQuad(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, Color color)
    {
        int b = _verts.Count;
        _verts.Add(new VertexPositionColor(v0, color));
        _verts.Add(new VertexPositionColor(v1, color));
        _verts.Add(new VertexPositionColor(v2, color));
        _verts.Add(new VertexPositionColor(v3, color));
        _idx.AddRange([b, b+2, b+1,  b, b+3, b+2]);
        _faces.Add((b, 4));
        return b;
    }

    // Adds a flat quad centred at `center`. `up` must be perpendicular to `normal`.
    // Visible from the `normal` side with CW winding. Returns vertex base index.
    public int AddQuad(Vector3 center, Vector3 normal, Vector3 up, float width, float height, Color color)
    {
        Vector3 right = Vector3.Normalize(Vector3.Cross(up, normal));
        Vector3 hw    = right * (width  * 0.5f);
        Vector3 hh    = up    * (height * 0.5f);
        return AddQuad(
            center - hw - hh,  // BL
            center + hw - hh,  // BR
            center + hw + hh,  // TR
            center - hw + hh,  // TL
            color);
    }

    // Adds an axis-aligned box at `center` in local module space.
    // Returns the index of the first vertex added.
    public int AddBox(Vector3 center, Vector3 size, Color color)
        => AddOrientedBox(Matrix.CreateTranslation(center), size, color);

    // Adds a box at an arbitrary transform (orientation + translation in local space).
    // size is the full extents before transform. Returns index of first vertex.
    // Uses 24 vertices (4 per face, unshared) so each face can be independently lit.
    public int AddOrientedBox(Matrix transform, Vector3 size, Color color)
    {
        Vector3 h = size * 0.5f;

        Span<Vector3> c = stackalloc Vector3[8]
        {
            new(-h.X, -h.Y, -h.Z), // 0 BL-back
            new(+h.X, -h.Y, -h.Z), // 1 BR-back
            new(+h.X, +h.Y, -h.Z), // 2 TR-back
            new(-h.X, +h.Y, -h.Z), // 3 TL-back
            new(-h.X, -h.Y, +h.Z), // 4 BL-front
            new(+h.X, -h.Y, +h.Z), // 5 BR-front
            new(+h.X, +h.Y, +h.Z), // 6 TR-front
            new(-h.X, +h.Y, +h.Z), // 7 TL-front
        };
        for (int i = 0; i < 8; i++)
            c[i] = Vector3.Transform(c[i], transform);

        int firstBase = _verts.Count;

        // Six faces — each is an independent quad (4 unique vertices).
        // Vertex ordering chosen so that cross(v1-v0, v2-v0) gives the outward normal,
        // and the triangles (v0,v2,v1) and (v0,v3,v2) are CW from outside (not culled).
        AddQuad(c[4], c[5], c[6], c[7], color); // +Z
        AddQuad(c[1], c[0], c[3], c[2], color); // -Z
        AddQuad(c[0], c[4], c[7], c[3], color); // -X
        AddQuad(c[5], c[1], c[2], c[6], color); // +X
        AddQuad(c[7], c[6], c[2], c[3], color); // +Y
        AddQuad(c[0], c[1], c[5], c[4], color); // -Y

        return firstBase;
    }

    // Adds a box whose Z axis aligns with `longAxis`.
    // length = extent along longAxis; width/depth = cross-section.
    // Returns index of first vertex.
    public int AddOrientedBox(Vector3 center, Vector3 longAxis, float length,
                               float width, float depth, Color color)
    {
        longAxis = Vector3.Normalize(longAxis);
        Vector3 hint  = MathF.Abs(longAxis.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
        Vector3 right = Vector3.Normalize(Vector3.Cross(hint, longAxis));
        Vector3 up    = Vector3.Normalize(Vector3.Cross(longAxis, right));

        var transform = new Matrix(
            right.X,    right.Y,    right.Z,    0,
            up.X,       up.Y,       up.Z,       0,
            longAxis.X, longAxis.Y, longAxis.Z, 0,
            center.X,   center.Y,   center.Z,   1
        );
        return AddOrientedBox(transform, new Vector3(width, depth, length), color);
    }

    // Adds a thin spike from `basePos` extending in `direction`.
    public void AddSpike(Vector3 basePos, Vector3 direction, float length, float radius, Color color)
    {
        direction = Vector3.Normalize(direction);
        Vector3 mid = basePos + direction * (length * 0.5f);
        AddOrientedBox(mid, direction, length, radius * 2, radius * 2, color);
    }

    // Adds a single triangle (CW from front). Index [b,b+2,b+1] = CCW in right-handed math → CW in DirectX Y-down.
    public void AddTriangle(Vector3 v0, Vector3 v1, Vector3 v2, Color color)
    {
        int b = _verts.Count;
        _verts.Add(new VertexPositionColor(v0, color));
        _verts.Add(new VertexPositionColor(v1, color));
        _verts.Add(new VertexPositionColor(v2, color));
        _idx.AddRange([b, b+2, b+1]);
        _faces.Add((b, 3));
    }

    public int FaceCount => _faces.Count;

    // Returns the normalised local-space outward normal for the given face index.
    public Vector3 LocalFaceNormal(int faceIdx)
    {
        var (vb, _) = _faces[faceIdx];
        Vector3 n = Vector3.Cross(
            _verts[vb + 1].Position - _verts[vb].Position,
            _verts[vb + 2].Position - _verts[vb].Position);
        float len = n.Length();
        return len < 1e-6f ? Vector3.Zero : n / len;
    }

    // Multiplies the RGB of every vertex in a face by `factor` (clamped to [0,255]).
    public void MultiplyFaceColor(int faceIdx, float factor)
    {
        var (vb, count) = _faces[faceIdx];
        for (int i = 0; i < count; i++)
        {
            var vtx = _verts[vb + i];
            vtx.Color = new Color(
                (byte)MathF.Min(vtx.Color.R * factor, 255f),
                (byte)MathF.Min(vtx.Color.G * factor, 255f),
                (byte)MathF.Min(vtx.Color.B * factor, 255f),
                vtx.Color.A);
            _verts[vb + i] = vtx;
        }
    }

    // Bakes directional lighting into vertex colours.
    // worldRotation: rotation-only part of the module's world transform (no scale/translate).
    // Emissive faces (R+G+B > 370) are skipped — their colours stay at full brightness.
    // Must be called after all geometry is added and before Build().
    public void ApplyLighting(Matrix worldRotation, Vector3 sunDirection, float ambient, Vector3 sunColour)
    {
        foreach (var (vb, count) in _faces)
        {
            Color orig = _verts[vb].Color;
            // Emissive heuristic: bright-enough colours are self-lit (windows, light lenses)
            if ((int)orig.R + orig.G + orig.B > 370) continue;

            // Compute face normal from first three vertices (cross product → outward normal)
            Vector3 localN = Vector3.Cross(
                _verts[vb + 1].Position - _verts[vb].Position,
                _verts[vb + 2].Position - _verts[vb].Position);
            float len = localN.Length();
            if (len < 1e-6f) continue;
            localN /= len;

            // Transform local normal to world space for the N·L calculation
            Vector3 worldN = Vector3.Normalize(Vector3.TransformNormal(localN, worldRotation));
            float   factor = MathF.Max(Vector3.Dot(worldN, sunDirection), ambient);

            for (int i = 0; i < count; i++)
            {
                var vtx = _verts[vb + i];
                vtx.Color = new Color(
                    (byte)MathF.Min(vtx.Color.R * factor * sunColour.X, 255f),
                    (byte)MathF.Min(vtx.Color.G * factor * sunColour.Y, 255f),
                    (byte)MathF.Min(vtx.Color.B * factor * sunColour.Z, 255f),
                    vtx.Color.A);
                _verts[vb + i] = vtx;
            }
        }
    }

    // Builds GPU buffers from accumulated geometry. Returns null if the mesh is empty.
    public (VertexBuffer vb, IndexBuffer ib, int triCount)? Build(GraphicsDevice gd)
    {
        if (_verts.Count == 0) return null;

        var verts   = _verts.ToArray();
        var indices = _idx.ToArray();

        var vb = new VertexBuffer(gd, VertexPositionColor.VertexDeclaration,
                                  verts.Length, BufferUsage.WriteOnly);
        vb.SetData(verts);

        var ib = new IndexBuffer(gd, IndexElementSize.ThirtyTwoBits,
                                 indices.Length, BufferUsage.WriteOnly);
        ib.SetData(indices);

        return (vb, ib, indices.Length / 3);
    }
}
