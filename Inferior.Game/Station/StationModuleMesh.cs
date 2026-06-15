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
public sealed class StationModuleMesh
{
    private readonly List<VertexPositionColor> _verts = [];
    private readonly List<int>                 _idx   = [];

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
    public int AddOrientedBox(Matrix transform, Vector3 size, Color color)
    {
        Vector3 h = size * 0.5f;

        Span<Vector3> c = stackalloc Vector3[8]
        {
            new(-h.X, -h.Y, -h.Z), // 0
            new(+h.X, -h.Y, -h.Z), // 1
            new(+h.X, +h.Y, -h.Z), // 2
            new(-h.X, +h.Y, -h.Z), // 3
            new(-h.X, -h.Y, +h.Z), // 4
            new(+h.X, -h.Y, +h.Z), // 5
            new(+h.X, +h.Y, +h.Z), // 6
            new(-h.X, +h.Y, +h.Z), // 7
        };
        for (int i = 0; i < 8; i++)
            c[i] = Vector3.Transform(c[i], transform);

        int b = _verts.Count;
        for (int i = 0; i < 8; i++)
            _verts.Add(new VertexPositionColor(c[i], color));

        ReadOnlySpan<int> faces =
        [
            b+4, b+6, b+5,  b+4, b+7, b+6,  // +Z
            b+1, b+3, b+0,  b+1, b+2, b+3,  // -Z
            b+0, b+7, b+4,  b+0, b+3, b+7,  // -X
            b+5, b+2, b+1,  b+5, b+6, b+2,  // +X
            b+7, b+2, b+6,  b+7, b+3, b+2,  // +Y
            b+0, b+5, b+1,  b+0, b+4, b+5,  // -Y
        ];
        _idx.AddRange(faces);
        return b;
    }

    // Adds a box whose Z axis aligns with `longAxis`.
    // length = extent along longAxis; width/depth = cross-section.
    // Returns index of first vertex.
    public int AddOrientedBox(Vector3 center, Vector3 longAxis, float length,
                               float width, float depth, Color color)
    {
        longAxis = Vector3.Normalize(longAxis);
        Vector3 hint  = MathF.Abs(longAxis.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
        Vector3 right = Vector3.Normalize(Vector3.Cross(longAxis, hint));
        Vector3 up    = Vector3.Normalize(Vector3.Cross(right, longAxis));

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
