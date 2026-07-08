using Inferior.Rendering;
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
// Uses VertexPositionColorTexture — render with LightingEnabled=false,
// VertexColorEnabled=true, TextureEnabled=true.
// Lighting is baked into vertex colours by ApplyLighting() before Build().
// UV coordinates are generated automatically from face geometry (5 m per tile).
public sealed class StationModuleMesh
{
    private readonly List<VertexPositionNormalColorTexture>  _verts = [];
    private readonly List<int>                          _idx   = [];
    private readonly List<(int vertexBase, int count)>  _faces = [];

    public bool           IsEmpty       => _verts.Count == 0;
    public List<AnimTag>  AnimTags      { get; } = [];
    public SurfaceTexture Texture       { get; set; } = SurfaceTexture.CleanPanel;

    // Set after base/seam geometry is added and before raised decoration (greebles, pipes).
    // ApplyAmbientOcclusion only processes faces 0..BaseFaceCount-1.
    public int BaseFaceCount { get; set; } = 0;

    // Optional sub-range of faces wanting a richer ambient treatment than the rest of the mesh
    // during lighting bake — e.g. a hollow module's interior walls, which the sun rarely reaches
    // directly and would otherwise bake near-black. AmbientOverrideFaceCount = 0 (default) means
    // "no override." Set by the module's own MeshFactory; StationGenerator.BakeLighting reads
    // these after the normal whole-mesh ApplyLighting pass (see
    // StationGenerator.BoostAmbientForFaceRange for the per-face formula) — see DockingBayHull
    // for the one current use.
    public int AmbientOverrideFaceStart { get; set; } = -1;
    public int AmbientOverrideFaceCount { get; set; } = 0;

    // Adds a flat quad from four explicit corner vertices (CW from normal side).
    // UV coordinates are projected from the face plane; 1 UV unit = 5 metres.
    // Returns the index of v0 in the vertex array.
    public int AddQuad(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, Color color)
    {
        int b = _verts.Count;

        Vector3 edge0  = v1 - v0;
        Vector3 edge1  = v2 - v0;
        Vector3 normal = Vector3.Cross(edge0, edge1);
        float   nLen   = normal.Length();
        if (nLen > 1e-6f) normal /= nLen;
        Vector3 arb   = MathF.Abs(normal.Y) < 0.85f ? Vector3.UnitY : Vector3.UnitX;
        Vector3 uAxis = Vector3.Normalize(Vector3.Cross(normal, arb));
        Vector3 vAxis = Vector3.Normalize(Vector3.Cross(normal, uAxis));

        _verts.Add(new VertexPositionNormalColorTexture(v0, normal, color, Vector2.Zero));
        _verts.Add(new VertexPositionNormalColorTexture(v1, normal, color, FaceUV(v1 - v0, uAxis, vAxis)));
        _verts.Add(new VertexPositionNormalColorTexture(v2, normal, color, FaceUV(v2 - v0, uAxis, vAxis)));
        _verts.Add(new VertexPositionNormalColorTexture(v3, normal, color, FaceUV(v3 - v0, uAxis, vAxis)));
        _idx.AddRange([b, b+2, b+1,  b, b+3, b+2]);
        _faces.Add((b, 4));
        return b;
    }

    private static Vector2 FaceUV(Vector3 offset, Vector3 uAxis, Vector3 vAxis)
    {
        const float UvScale = 5.0f;
        return new Vector2(Vector3.Dot(offset, uAxis) / UvScale,
                           Vector3.Dot(offset, vAxis) / UvScale);
    }

    // Overload that accepts an explicit face normal (ignored — winding determines normal)
    // and a uvScale (currently hardcoded to 5 m/tile, parameter kept for API parity).
    public int AddQuad(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3,
                       Vector3 _normal, Color color, float _uvScale)
        => AddQuad(v0, v1, v2, v3, color);

    // Triangle overload with explicit normal and uvScale — both are ignored;
    // normal is derived from winding and uvScale is hardcoded to 5 m/tile.
    public void AddTriangle(Vector3 v0, Vector3 v1, Vector3 v2,
                            Vector3 _normal, Color color, float _uvScale = 5.0f)
        => AddTriangle(v0, v1, v2, color);

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

    // Adds an N-sided prism pipe. Each lateral face is stored as an independent quad
    // so ApplyLighting computes a distinct outward normal per face — a hexagonal pipe
    // gets 6 different shading values, simulating roundness with no renderer changes.
    // No end caps by default; most callers have pipes flush against surfaces or
    // continuing off-screen (into other geometry, or the module edge). For a genuinely
    // free-floating exposed end, pass capStart/capEnd — see AddDiscCap below.
    public void AddPrismPipe(Vector3 start, Vector3 end, float radius, int sides, Color color,
        bool capStart = false, bool capEnd = false)
    {
        Vector3 dir    = end - start;
        float   length = dir.Length();
        if (length < 0.01f) return;
        dir = Vector3.Normalize(dir);

        Vector3 arb   = MathF.Abs(dir.Y) < 0.85f ? Vector3.UnitY : Vector3.UnitX;
        Vector3 right = Vector3.Normalize(Vector3.Cross(dir, arb));
        Vector3 up    = Vector3.Normalize(Vector3.Cross(right, dir));

        var startRing = new Vector3[sides];
        var endRing   = new Vector3[sides];
        for (int i = 0; i < sides; i++)
        {
            float   a      = i * MathF.Tau / sides;
            Vector3 offset = right * (MathF.Cos(a) * radius) + up * (MathF.Sin(a) * radius);
            startRing[i] = start + offset;
            endRing[i]   = end   + offset;
        }

        for (int i = 0; i < sides; i++)
        {
            int next = (i + 1) % sides;
            AddQuad(startRing[next], startRing[i], endRing[i], endRing[next], color);
        }

        if (capStart) AddDiscCap(startRing, start, -dir, color);
        if (capEnd)   AddDiscCap(endRing,   end,   dir,  color);
    }

    // Fills a flat disc from ring vertices. Winding is computed and verified against
    // expectedOutward, not hand-derived — same discipline as ChamferedBox.Build earlier
    // tonight, since manually working out cap winding is exactly the kind of thing that's
    // gone wrong more than once already.
    private void AddDiscCap(Vector3[] ring, Vector3 center, Vector3 expectedOutward, Color color)
    {
        int sides = ring.Length;
        for (int i = 0; i < sides; i++)
        {
            int next = (i + 1) % sides;
            Vector3 a = ring[i], b = ring[next];
            Vector3 normal = Vector3.Cross(a - center, b - center);
            if (Vector3.Dot(normal, expectedOutward) < 0)
                AddTriangle(center, b, a, color);
            else
                AddTriangle(center, a, b, color);
        }
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

        Vector3 edge0  = v1 - v0;
        Vector3 edge1  = v2 - v0;
        Vector3 normal = Vector3.Cross(edge0, edge1);
        float   nLen   = normal.Length();
        if (nLen > 1e-6f) normal /= nLen;
        Vector3 arb   = MathF.Abs(normal.Y) < 0.85f ? Vector3.UnitY : Vector3.UnitX;
        Vector3 uAxis = Vector3.Normalize(Vector3.Cross(normal, arb));
        Vector3 vAxis = Vector3.Normalize(Vector3.Cross(normal, uAxis));

        _verts.Add(new VertexPositionNormalColorTexture(v0, normal, color, Vector2.Zero));
        _verts.Add(new VertexPositionNormalColorTexture(v1, normal, color, FaceUV(edge0, uAxis, vAxis)));
        _verts.Add(new VertexPositionNormalColorTexture(v2, normal, color, FaceUV(edge1, uAxis, vAxis)));
        _idx.AddRange([b, b+2, b+1]);
        _faces.Add((b, 3));
    }

    // Per-vertex colour triangle — used for glass gradients.
    public void AddTriangleGradient(Vector3 v0, Color c0, Vector3 v1, Color c1, Vector3 v2, Color c2)
    {
        int b = _verts.Count;

        Vector3 edge0  = v1 - v0;
        Vector3 edge1  = v2 - v0;
        Vector3 normal = Vector3.Cross(edge0, edge1);
        float   nLen   = normal.Length();
        if (nLen > 1e-6f) normal /= nLen;
        Vector3 arb   = MathF.Abs(normal.Y) < 0.85f ? Vector3.UnitY : Vector3.UnitX;
        Vector3 uAxis = Vector3.Normalize(Vector3.Cross(normal, arb));
        Vector3 vAxis = Vector3.Normalize(Vector3.Cross(normal, uAxis));

        _verts.Add(new VertexPositionNormalColorTexture(v0, normal, c0, Vector2.Zero));
        _verts.Add(new VertexPositionNormalColorTexture(v1, normal, c1, FaceUV(edge0, uAxis, vAxis)));
        _verts.Add(new VertexPositionNormalColorTexture(v2, normal, c2, FaceUV(edge1, uAxis, vAxis)));
        _idx.AddRange([b, b+2, b+1]);
        _faces.Add((b, 3));
    }

    // Per-vertex colour quad — used for glass gradients.
    // Vertex order: v0=BL, v1=BR, v2=TR, v3=TL (CW from normal side).
    public int AddQuadGradient(Vector3 v0, Color c0, Vector3 v1, Color c1,
                                Vector3 v2, Color c2, Vector3 v3, Color c3)
    {
        int b = _verts.Count;

        Vector3 edge0  = v1 - v0;
        Vector3 edge1  = v2 - v0;
        Vector3 normal = Vector3.Cross(edge0, edge1);
        float   nLen   = normal.Length();
        if (nLen > 1e-6f) normal /= nLen;
        Vector3 arb   = MathF.Abs(normal.Y) < 0.85f ? Vector3.UnitY : Vector3.UnitX;
        Vector3 uAxis = Vector3.Normalize(Vector3.Cross(normal, arb));
        Vector3 vAxis = Vector3.Normalize(Vector3.Cross(normal, uAxis));

        _verts.Add(new VertexPositionNormalColorTexture(v0, normal, c0, Vector2.Zero));
        _verts.Add(new VertexPositionNormalColorTexture(v1, normal, c1, FaceUV(v1 - v0, uAxis, vAxis)));
        _verts.Add(new VertexPositionNormalColorTexture(v2, normal, c2, FaceUV(v2 - v0, uAxis, vAxis)));
        _verts.Add(new VertexPositionNormalColorTexture(v3, normal, c3, FaceUV(v3 - v0, uAxis, vAxis)));
        _idx.AddRange([b, b+2, b+1,  b, b+3, b+2]);
        _faces.Add((b, 4));
        return b;
    }

    public int FaceCount => _faces.Count;

    // Returns (localCenter, width, height) for a face.
    // For triangle faces (count == 3), returns (centroid, 0, 0).
    public (Vector3 center, float width, float height) GetFaceBounds(int faceIdx)
    {
        var (vb, count) = _faces[faceIdx];
        if (count >= 4)
        {
            Vector3 v0 = _verts[vb].Position;
            Vector3 v1 = _verts[vb + 1].Position;
            Vector3 v2 = _verts[vb + 2].Position;
            Vector3 v3 = _verts[vb + 3].Position;
            return ((v0 + v2) * 0.5f, Vector3.Distance(v0, v1), Vector3.Distance(v0, v3));
        }
        var centroid = (_verts[vb].Position + _verts[vb + 1].Position + _verts[vb + 2].Position) / 3f;
        return (centroid, 0f, 0f);
    }

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
            var vtx   = _verts[vb + i];
            var c     = vtx.Color;
            vtx.Color = new Color(
                (byte)MathF.Min(c.R * factor, 255f),
                (byte)MathF.Min(c.G * factor, 255f),
                (byte)MathF.Min(c.B * factor, 255f),
                c.A);
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
                var vtx   = _verts[vb + i];
                var c     = vtx.Color;
                vtx.Color = new Color(
                    (byte)MathF.Min(c.R * factor * sunColour.X, 255f),
                    (byte)MathF.Min(c.G * factor * sunColour.Y, 255f),
                    (byte)MathF.Min(c.B * factor * sunColour.Z, 255f),
                    c.A);
                _verts[vb + i] = vtx;
            }
        }
    }

    // Transforms external geometry into this mesh's local space and bakes directional
    // lighting into it using each vertex's own normal (more accurate than this mesh's own
    // per-face lighting, since real per-vertex normals are available). No _faces entry is
    // added — matches how other raised decoration (tanks, dishes, antennas) already isn't
    // individually face-addressable after being added.
    //
    // Automatically detects and corrects a mirrored (improper / negative-determinant)
    // transform by negating one basis row, restoring a proper rotation before anything
    // is transformed through it — callers should not need to pre-correct handedness
    // themselves (e.g. PlaceContainer building a frame by swapping which face axis
    // becomes "long" vs "cross", which flips handedness for one of the two orientations).
    //
    // This does NOT just reverse triangle winding. An improper transform doesn't just
    // affect which side a face is visible from — it geometrically mirrors the shape
    // itself. That's invisible for symmetric content (chamfers, box faces: a mirrored
    // box looks identical to the original, just wound backwards), but for asymmetric
    // content — text — a mirrored "R" is a different, wrong shape, not merely a
    // backwards-facing correct one; fixing only the winding leaves it readable as
    // backwards text. Restoring properness at the transform itself, before any vertex
    // is moved, fixes both symmetric and asymmetric content the same way.
    public void MergeTransformedAndLit(
        VertexPositionNormalColorTexture[] verts, short[] indices, Matrix transform,
        Vector3 sunDirection, float ambient, Vector3 sunColour)
    {
        if (transform.Determinant() < 0)
        {
            transform.M21 = -transform.M21;
            transform.M22 = -transform.M22;
            transform.M23 = -transform.M23;
        }

        int vbOffset = _verts.Count;

        foreach (var v in verts)
        {
            Vector3 pos = Vector3.Transform(v.Position, transform);
            Vector3 nrm = Vector3.Normalize(Vector3.TransformNormal(v.Normal, transform));
            float   factor = MathF.Max(Vector3.Dot(nrm, sunDirection), ambient);
            Color   c = v.Color;
            Color lit = new Color(
                (byte)MathF.Min(c.R * factor * sunColour.X, 255f),
                (byte)MathF.Min(c.G * factor * sunColour.Y, 255f),
                (byte)MathF.Min(c.B * factor * sunColour.Z, 255f), c.A);
            _verts.Add(new VertexPositionNormalColorTexture(pos, nrm, lit, v.TextureCoordinate));
        }

        for (int i = 0; i < indices.Length; i += 3)
            _idx.AddRange([vbOffset + indices[i], vbOffset + indices[i + 1], vbOffset + indices[i + 2]]);
    }

    // Returns raw CPU-side arrays without requiring a GraphicsDevice.
    // Indices are converted from int to short (safe up to 32 767 vertices).
    public (VertexPositionColorTexture[] verts, short[] indices) ToArrays()
    {
        var verts = _verts.Select(v => new VertexPositionColorTexture(v.Position, v.Color, v.TextureCoordinate)).ToArray();
        var indices = new short[_idx.Count];
        for (int i = 0; i < _idx.Count; i++)
            indices[i] = (short)_idx[i];
        return (verts, indices);
    }

    // Returns raw CPU-side arrays including per-vertex normals, for dynamically lit
    // geometry (containers) — see VertexPositionNormalColorTexture.
    public (VertexPositionNormalColorTexture[] verts, short[] indices) ToArraysWithNormals()
    {
        var verts   = _verts.ToArray();   // already the right type now
        var indices = new short[_idx.Count];
        for (int i = 0; i < _idx.Count; i++)
            indices[i] = (short)_idx[i];
        return (verts, indices);
    }

    // Builds GPU buffers from accumulated geometry. Returns null if the mesh is empty.
    public (VertexBuffer vb, IndexBuffer ib, int triCount)? Build(GraphicsDevice gd)
    {
        if (_verts.Count == 0) return null;

        var verts   = _verts.Select(v => new VertexPositionColorTexture(v.Position, v.Color, v.TextureCoordinate)).ToArray();
        var indices = _idx.ToArray();

        var vb = new VertexBuffer(gd, VertexPositionColorTexture.VertexDeclaration,
                                  verts.Length, BufferUsage.WriteOnly);
        vb.SetData(verts);

        var ib = new IndexBuffer(gd, IndexElementSize.ThirtyTwoBits,
                                 indices.Length, BufferUsage.WriteOnly);
        ib.SetData(indices);

        return (vb, ib, indices.Length / 3);
    }
}
