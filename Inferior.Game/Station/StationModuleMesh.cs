using Inferior.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.Game.StationGen;

public enum AnimType { Steady, Strobe, Pulse }

// Decoration class tag for shadow-caster composition (Docs/station-lighting-pipeline-spec.md
// Phase C). StationDecorator sets StationModuleMesh.CurrentDecorClass before each
// Generate*/Place* pass; every geometry-appending call records the index range it just
// added under that class (see RecordDecorClassRange). Sub-helpers called from within a
// pass inherit whatever class is already current — nothing resets it mid-pass. See
// StationDecorator.DecorCastingPolicy for the casts/doesn't-cast table and the per-class
// rationale; that table is the executable form of the spec's "documented casting policy."
public enum DecorClass
{
    // Default; never casts — safe fallback for anything not explicitly tagged (there
    // shouldn't be any once Decorate wraps every pass, but this is the fail-safe, not a
    // silently-relied-on default).
    Unclassified,

    // Never casts (documented exclusions) — see DecorCastingPolicy.
    PanelSeams, EdgeTrim, Cables, Lights, Glass, LandingPadMarkings,

    // Native megastation infrastructure. Major carries only silhouette-worthy
    // machinery/tank forms; Minor carries vents and small visible detailing.
    MegastationInfrastructureMajor, MegastationInfrastructureMinor,
    MegastationMegaGreebleMajor, MegastationMegaGreebleMinor,

    // C1 — structural
    Pipes, SurfacePipes, PipeBrackets,

    // C2 — equipment
    Tanks, Containers, Greebles, Chimneys, VentGrilles, SolarPanels,

    // C3 — antennas
    Dishes, Antennas,

    // C4 — windows
    Windows, Hatches,
}

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
// Vertex colour is albedo x AO (+ deliberate tint/wear/interior overrides) only — no
// directional term is ever baked in (Docs/station-lighting-pipeline-spec.md Phase A).
// Vertex alpha carries a self-illumination floor S written by ApplyIlluminationFlags()/
// StationGenerator.BoostAmbientForFaceRange before Build(). Normals travel through to the
// GPU buffer; the sun term (LitSurface.fx, BakedColorLit technique) is computed every
// frame from the real world normal, so a rotating station is lit correctly.
// UV coordinates are generated automatically from face geometry (5 m per tile).
public sealed class StationModuleMesh
{
    private readonly List<VertexPositionNormalColorTexture>  _verts = [];
    private readonly List<int>                          _idx   = [];
    private readonly List<(int vertexBase, int count)>  _faces = [];
    private readonly List<(int indexStart, int indexCount, DecorClass decorClass)> _classRanges = [];
    private bool _breakDecorClassRange;

    public bool           IsEmpty       => _verts.Count == 0;
    public int            VertexCount   => _verts.Count;
    public int            IndexCount    => _idx.Count;
    public List<AnimTag>  AnimTags      { get; } = [];
    public SurfaceTexture Texture       { get; set; } = SurfaceTexture.CleanPanel;

    // Set by StationDecorator before each Generate*/Place* pass; every geometry-appending
    // call below tags the index range it just added with whatever class is current. See
    // the DecorClass enum doc comment and StationDecorator.DecorCastingPolicy.
    public DecorClass CurrentDecorClass { get; set; } = DecorClass.Unclassified;

    // Index ranges recorded as geometry was appended, tagged by CurrentDecorClass at the
    // time. Consumed by the shadow system (SystemSpaceState.Shadows.cs) to compose a
    // per-module caster mesh from whichever classes are currently casting-enabled — see
    // BuildIndexRanges. Exposed alongside Build() rather than through its return value:
    // the ranges are a property of the accumulated mesh itself, not of any one GPU build
    // (the flat and AO-graded snapshots share identical index structure — AO only
    // rewrites vertex colour — so threading ranges through every Build() call would just
    // duplicate the same data under two different dictionary entries for no benefit).
    public IReadOnlyList<(int indexStart, int indexCount, DecorClass decorClass)> DecorClassRanges
        => _classRanges;

    // Starts a fresh diagnostic/ownership range even when the next geometry has the same
    // DecorClass as the preceding geometry. Native megastation builders use this at an
    // instance boundary so per-family caster participation can be enumerated without
    // changing the combined mesh or its draw-call count.
    public void BreakDecorClassRange() => _breakDecorClassRange = true;

    // Appends (or extends, if contiguous with the previous entry and same class) a
    // class-tagged range covering the indices just added. Called once per geometry-adding
    // method (AddQuad/AddTriangle/gradient variants/MergeTransformed) — sub-shapes built
    // from those (AddOrientedBox, AddPrismPipe, etc.) tag correctly for free since they're
    // themselves built from calls to the tagged primitives.
    private void RecordDecorClassRange(int indexStart, int indexCount)
    {
        if (indexCount <= 0) return;
        if (!_breakDecorClassRange && _classRanges.Count > 0)
        {
            var last = _classRanges[^1];
            if (last.decorClass == CurrentDecorClass && last.indexStart + last.indexCount == indexStart)
            {
                _classRanges[^1] = (last.indexStart, last.indexCount + indexCount, last.decorClass);
                return;
            }
        }
        _breakDecorClassRange = false;
        _classRanges.Add((indexStart, indexCount, CurrentDecorClass));
    }

    // Set after base/seam geometry is added and before raised decoration (greebles, pipes).
    // ApplyAmbientOcclusion processes faces 0..BaseFaceCount-1. Brief U1: this mesh never
    // contains hull geometry for either module kind (a MeshFactory module's hull lives in
    // its own separate mesh, PlacedModule.HullMesh, exactly like a box module's
    // StationGenerator.PrepareBoxHullMesh output does) — so this is always simply "how many
    // seam-decoration faces exist so far," no special-casing by module kind.
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
        int idxStart = _idx.Count;
        _idx.AddRange([b, b+2, b+1,  b, b+3, b+2]);
        RecordDecorClassRange(idxStart, _idx.Count - idxStart);
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
        int idxStart = _idx.Count;
        _idx.AddRange([b, b+2, b+1]);
        RecordDecorClassRange(idxStart, _idx.Count - idxStart);
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
        int idxStart = _idx.Count;
        _idx.AddRange([b, b+2, b+1]);
        RecordDecorClassRange(idxStart, _idx.Count - idxStart);
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
        int idxStart = _idx.Count;
        _idx.AddRange([b, b+2, b+1,  b, b+3, b+2]);
        RecordDecorClassRange(idxStart, _idx.Count - idxStart);
        _faces.Add((b, 4));
        return b;
    }

    public int FaceCount => _faces.Count;

    public (VertexBuffer vb, IndexBuffer ib, int triCount)? BuildFaceRange(
        GraphicsDevice gd, int firstFace, int faceCount)
    {
        if (faceCount <= 0 || firstFace < 0 || firstFace >= _faces.Count)
            return null;

        int lastFace = Math.Min(_faces.Count, firstFace + faceCount);
        var verts = new List<VertexPositionNormalColorTexture>();
        var idx = new List<int>();

        for (int face = firstFace; face < lastFace; face++)
        {
            var (vertexBase, count) = _faces[face];
            if (count < 3) continue;

            int dstBase = verts.Count;
            for (int i = 0; i < count; i++)
                verts.Add(_verts[vertexBase + i]);

            if (count == 3)
            {
                idx.AddRange([dstBase, dstBase + 1, dstBase + 2]);
            }
            else
            {
                for (int i = 1; i < count - 1; i++)
                    idx.AddRange([dstBase, dstBase + i + 1, dstBase + i]);
            }
        }

        if (verts.Count == 0 || idx.Count == 0)
            return null;

        var vb = new VertexBuffer(gd, VertexPositionNormalColorTexture.VertexDeclaration,
                                  verts.Count, BufferUsage.WriteOnly);
        vb.SetData(verts.ToArray());
        var ib = new IndexBuffer(gd, IndexElementSize.ThirtyTwoBits, idx.Count, BufferUsage.WriteOnly);
        ib.SetData(idx.ToArray());
        return (vb, ib, idx.Count / 3);
    }

    // Builds a compact GPU mesh (fresh vertex/index buffers, vertices remapped to a
    // dense 0-based range) containing exactly the triangles referenced by the given
    // (indexStart, indexCount) ranges from the accumulated index buffer. Used by the
    // shadow system to compose a per-module caster mesh from whichever DecorClass ranges
    // are currently casting-enabled (see DecorClassRanges and
    // StationDecorator.DecorCastingPolicy) — same remap-and-copy approach as
    // BuildFaceRange, just keyed by raw index position instead of face index, since a
    // class's triangles are scattered non-contiguously through the deco mesh.
    public (VertexBuffer vb, IndexBuffer ib, int triCount)? BuildIndexRanges(
        GraphicsDevice gd, IReadOnlyList<(int indexStart, int indexCount)> ranges)
    {
        StationMeshCpuData? prepared = PrepareIndexRanges(ranges);
        if (prepared == null)
            return null;

        var ivb = new VertexBuffer(gd, VertexPositionNormalColorTexture.VertexDeclaration,
                                  prepared.Vertices.Length, BufferUsage.WriteOnly);
        ivb.SetData(prepared.Vertices);
        var iib = new IndexBuffer(gd, IndexElementSize.ThirtyTwoBits,
                                  prepared.Indices.Length, BufferUsage.WriteOnly);
        iib.SetData(prepared.Indices);
        return (ivb, iib, prepared.Indices.Length / 3);
    }

    public StationMeshCpuData? PrepareIndexRanges(
        IReadOnlyList<(int indexStart, int indexCount)> ranges)
    {
        if (ranges.Count == 0) return null;

        var verts = new List<VertexPositionNormalColorTexture>();
        var idx = new List<int>();
        var remap = new Dictionary<int, int>();

        foreach (var (indexStart, indexCount) in ranges)
        {
            int end = Math.Min(_idx.Count, indexStart + indexCount);
            for (int i = Math.Max(0, indexStart); i < end; i++)
            {
                int srcVertex = _idx[i];
                if (!remap.TryGetValue(srcVertex, out int dstVertex))
                {
                    dstVertex = verts.Count;
                    verts.Add(_verts[srcVertex]);
                    remap[srcVertex] = dstVertex;
                }
                idx.Add(dstVertex);
            }
        }

        return verts.Count == 0 || idx.Count == 0
            ? null
            : new StationMeshCpuData(verts.ToArray(), idx.ToArray());
    }

    // Module-local AABB over a face range — same face-range selection as BuildFaceRange, but
    // just the vertex bounds, no GPU buffers. Used by the shadow system's light-camera fit
    // (Phase C) so a MeshFactory hull (e.g. docking-bay) contributes its real geometry extent
    // instead of the definition's approximate envelope box.
    public (Vector3 min, Vector3 max)? ComputeFaceRangeBounds(int firstFace, int faceCount)
    {
        if (faceCount <= 0 || firstFace < 0 || firstFace >= _faces.Count) return null;
        int lastFace = Math.Min(_faces.Count, firstFace + faceCount);

        Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        bool any = false;
        for (int face = firstFace; face < lastFace; face++)
        {
            var (vertexBase, count) = _faces[face];
            for (int i = 0; i < count; i++)
            {
                Vector3 p = _verts[vertexBase + i].Position;
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
                any = true;
            }
        }
        return any ? (min, max) : null;
    }

    // Module-local AABB over a set of (indexStart, indexCount) ranges — same range selection
    // as BuildIndexRanges, but just the vertex bounds, no GPU buffers. Used by the shadow
    // system's light-camera fit (Phase C) to include whichever DecorClass ranges are
    // currently casting-enabled.
    public (Vector3 min, Vector3 max)? ComputeIndexRangeBounds(
        IReadOnlyList<(int indexStart, int indexCount)> ranges)
    {
        Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        bool any = false;
        foreach (var (indexStart, indexCount) in ranges)
        {
            int end = Math.Min(_idx.Count, indexStart + indexCount);
            for (int i = Math.Max(0, indexStart); i < end; i++)
            {
                Vector3 p = _verts[_idx[i]].Position;
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
                any = true;
            }
        }
        return any ? (min, max) : null;
    }

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

    // Sets the self-illumination floor S (vertex alpha, [0,1]) for every vertex in a face —
    // used by StationGenerator.BoostAmbientForFaceRange for interior-override faces.
    public void SetFaceIllumination(int faceIdx, float s)
    {
        var (vb, count) = _faces[faceIdx];
        byte a = (byte)MathHelper.Clamp(s * 255f, 0f, 255f);
        for (int i = 0; i < count; i++)
        {
            var vtx   = _verts[vb + i];
            var c     = vtx.Color;
            vtx.Color = new Color(c.R, c.G, c.B, a);
            _verts[vb + i] = vtx;
        }
    }

    // Writes the self-illumination floor S into vertex alpha for every vertex in the mesh —
    // the bake-time replacement for the old directional multiply (deleted; the sun term is
    // now computed per frame in LitSurface.fx). S = 0 (fully sun-dependent) for every vertex.
    //
    // Iterates _verts directly, NOT _faces: MergeTransformed-merged geometry (station-placed
    // containers and their text) is never given a _faces entry (see MergeTransformed's own
    // comment), so a _faces-based sweep silently skips it — those vertices would keep
    // whatever alpha they arrived with (255, from ShippingContainerFactory's 3-arg Color
    // constructors), reading as S=1, i.e. fully emissive. Iterating _verts catches every
    // vertex present at bake time regardless of how it was added, including tanks/dishes/
    // antennas (also merged without a _faces entry).
    //
    // Design-note / flagged mismatch: the brief for this migration describes a "current
    // R+G+B > 370 → emissive, S=1" rule "applied where ApplyLighting applies it today."
    // No such rule exists anywhere in this method, in StationGenerator.BakeLighting, or
    // anywhere else in this file's git history on this branch's lineage — the old
    // ApplyLighting multiplied every face unconditionally, with no brightness-based skip.
    // The only actually-emissive station geometry (window/porthole glass) already lives in
    // a separate mesh (PlacedModule.GlassMesh) that ApplyLighting never touched and that
    // stays out of scope per this brief's D5. Implementing a fresh R+G+B>370 classifier
    // here would un-dim previously-dimmed light-housing faces on the shadow side — new
    // visual behaviour, not parity with master. Per the brief's own instruction ("if a
    // real conflict is found, stop and report — do not invent a different encoding
    // silently") this method sets S=0 for every vertex rather than inventing that rule; only
    // the AmbientOverrideFaceRange (interior faces, handled by
    // StationGenerator.BoostAmbientForFaceRange, which runs after this and only touches
    // face-tracked geometry) gets a non-zero S. Flagged for Timo.
    // Must be called after all geometry is added AND merged, and before Build().
    public void ApplyIlluminationFlags()
    {
        for (int i = 0; i < _verts.Count; i++)
        {
            var vtx   = _verts[i];
            var c     = vtx.Color;
            vtx.Color = new Color(c.R, c.G, c.B, (byte)0);
            _verts[i] = vtx;
        }
    }

    // Transforms external geometry into this mesh's local space, keeping each vertex's own
    // normal and colour untouched (no directional bake — Docs/station-lighting-pipeline-spec.md
    // Phase A). No _faces entry is added — matches how other raised decoration (tanks,
    // dishes, antennas) already isn't individually face-addressable after being added.
    // Merged vertices keep whatever alpha they arrive with (typically 255, i.e. S=1/fully
    // emissive) until ApplyIlluminationFlags runs — which is why that method sweeps _verts
    // directly rather than _faces, so this merge does NOT need to pre-zero alpha itself;
    // it must simply run before ApplyIlluminationFlags (StationGenerator.BakeLighting, after
    // all of StationDecorator.Decorate — see call order there).
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
    public void MergeTransformed(
        VertexPositionNormalColorTexture[] verts, short[] indices, Matrix transform)
    {
        if (transform.Determinant() < 0)
        {
            transform.M21 = -transform.M21;
            transform.M22 = -transform.M22;
            transform.M23 = -transform.M23;
        }

        int vbOffset = _verts.Count;
        int idxStart = _idx.Count;

        foreach (var v in verts)
        {
            Vector3 pos = Vector3.Transform(v.Position, transform);
            Vector3 nrm = Vector3.Normalize(Vector3.TransformNormal(v.Normal, transform));
            _verts.Add(new VertexPositionNormalColorTexture(pos, nrm, v.Color, v.TextureCoordinate));
        }

        for (int i = 0; i < indices.Length; i += 3)
            _idx.AddRange([vbOffset + indices[i], vbOffset + indices[i + 1], vbOffset + indices[i + 2]]);

        RecordDecorClassRange(idxStart, _idx.Count - idxStart);
    }

    // Returns raw CPU-side arrays including per-vertex normals — for dynamically lit
    // geometry (containers) and anywhere else a GraphicsDevice isn't available yet.
    public (VertexPositionNormalColorTexture[] verts, short[] indices) ToArrays()
    {
        var verts   = _verts.ToArray();   // already the right type
        var indices = new short[_idx.Count];
        for (int i = 0; i < _idx.Count; i++)
            indices[i] = (short)_idx[i];
        return (verts, indices);
    }

    public (VertexPositionNormalColorTexture[] verts, int[] indices) ToIntArrays()
        => (_verts.ToArray(), _idx.ToArray());

    // Builds GPU buffers from accumulated geometry. Returns null if the mesh is empty.
    public (VertexBuffer vb, IndexBuffer ib, int triCount)? Build(GraphicsDevice gd)
    {
        if (_verts.Count == 0) return null;

        var verts   = _verts.ToArray();   // already the right type — normals travel to the GPU
        var indices = _idx.ToArray();

        var vb = new VertexBuffer(gd, VertexPositionNormalColorTexture.VertexDeclaration,
                                  verts.Length, BufferUsage.WriteOnly);
        vb.SetData(verts);

        var ib = new IndexBuffer(gd, IndexElementSize.ThirtyTwoBits,
                                 indices.Length, BufferUsage.WriteOnly);
        ib.SetData(indices);

        return (vb, ib, indices.Length / 3);
    }
}
