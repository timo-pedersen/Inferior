using Microsoft.Xna.Framework;

namespace Inferior.Rendering;

/// <summary>
/// Generic axis-aligned chamfered (45°-beveled) box geometry: 6 main faces, 12 edge
/// chamfers, and 8 corner triangles, all built from one canonical set of 24 vertices
/// (6 faces × 4 corners). Every face/chamfer/triangle is a list of indices into that
/// vertex array — nothing computes a new position outside the initial 24 — and winding
/// is determined automatically by comparing the naive cross-product normal against the
/// known expected outward direction, rather than hand-derived per case.
///
/// Pure geometry, no GraphicsDevice/mesh-accumulator dependency — same principle
/// ShippingContainerFactory already follows. Scoped to axis-aligned boxes in local
/// space; callers that need it positioned/oriented elsewhere transform the output,
/// same pattern StationModuleMesh.AddOrientedBox uses.
/// </summary>
public static class ChamferedBox
{
    // A face/chamfer/triangle as an ordered list of indices into Result.Vertices,
    // already wound correctly (CW from outside).
    public readonly record struct Face(int[] Indices);

    public readonly struct Result
    {
        public required Vector3[] Vertices        { get; init; }  // 24 total
        public required Face[]    MainFaces        { get; init; }  // 6 quads
        public required Face[]    EdgeChamfers      { get; init; }  // 12 quads (4 long + 8 short)
        public required Face[]    CornerTriangles   { get; init; }  // 8 triangles
    }

    private static readonly int[] Signs = [-1, 1];

    public static Result Build(Vector3 halfExtents, float chamfer)
    {
        float hx = halfExtents.X, hy = halfExtents.Y, hz = halfExtents.Z;
        float c  = chamfer;

        // 24 vertices: 6 faces × 4 corners. Each face keeps its own axis fixed at
        // ±half-extent and shrinks the OTHER two axes inward by `c`.
        // Index scheme: vertex[face][corner], face ∈ {+X,-X,+Y,-Y,+Z,-Z} (0..5),
        // corner ∈ 0..3 ordered by the two free-axis signs as (-,-),(+,-),(+,+),(-,+).
        var v = new Vector3[24];
        // +X face (face 0): fixed x=+hx, free (y,z) shrunk by c
        v[0] = new(+hx, -(hy-c), -(hz-c));
        v[1] = new(+hx, +(hy-c), -(hz-c));
        v[2] = new(+hx, +(hy-c), +(hz-c));
        v[3] = new(+hx, -(hy-c), +(hz-c));
        // -X face (face 1): fixed x=-hx
        v[4] = new(-hx, -(hy-c), -(hz-c));
        v[5] = new(-hx, +(hy-c), -(hz-c));
        v[6] = new(-hx, +(hy-c), +(hz-c));
        v[7] = new(-hx, -(hy-c), +(hz-c));
        // +Y face (face 2): fixed y=+hy, free (x,z) shrunk by c
        v[8]  = new(-(hx-c), +hy, -(hz-c));
        v[9]  = new(+(hx-c), +hy, -(hz-c));
        v[10] = new(+(hx-c), +hy, +(hz-c));
        v[11] = new(-(hx-c), +hy, +(hz-c));
        // -Y face (face 3): fixed y=-hy
        v[12] = new(-(hx-c), -hy, -(hz-c));
        v[13] = new(+(hx-c), -hy, -(hz-c));
        v[14] = new(+(hx-c), -hy, +(hz-c));
        v[15] = new(-(hx-c), -hy, +(hz-c));
        // +Z face (face 4): fixed z=+hz, free (x,y) shrunk by c
        v[16] = new(-(hx-c), -(hy-c), +hz);
        v[17] = new(+(hx-c), -(hy-c), +hz);
        v[18] = new(+(hx-c), +(hy-c), +hz);
        v[19] = new(-(hx-c), +(hy-c), +hz);
        // -Z face (face 5): fixed z=-hz
        v[20] = new(-(hx-c), -(hy-c), -hz);
        v[21] = new(+(hx-c), -(hy-c), -hz);
        v[22] = new(+(hx-c), +(hy-c), -hz);
        v[23] = new(-(hx-c), +(hy-c), -hz);

        Vector3[] faceNormal =
        [
            Vector3.UnitX, -Vector3.UnitX,
            Vector3.UnitY, -Vector3.UnitY,
            Vector3.UnitZ, -Vector3.UnitZ,
        ];

        // Main faces: each face's own 4 vertices, in the order already laid out above
        // (already correctly wound relative to that face's own normal by construction —
        // verify with the same auto-winding check for safety rather than assuming).
        var mainFaces = new Face[6];
        for (int f = 0; f < 6; f++)
            mainFaces[f] = WindFace([f*4, f*4+1, f*4+2, f*4+3], v, faceNormal[f]);

        // Edge chamfers — 12 total, grouped by which axis the edge runs along (the
        // free/varying axis) and which two faces meet there (fixed at ±1 on the other
        // two axes). Each face's 4 corners are addressed via CornerIndex using that
        // face's own (free1, free2) axis order — see the corner-layout comment above.
        var edgeChamfers = new Face[12];
        int e = 0;

        // Z-axis edges (vary Z) — faceX(sx) meets faceY(sy). 4 edges.
        foreach (int sx in Signs)
        foreach (int sy in Signs)
        {
            int xBase = FaceXBase(sx), yBase = FaceYBase(sy);
            int i0 = xBase + CornerIndex(sy, -1); // faceX, z=-1  (free=(Y,Z))
            int i1 = xBase + CornerIndex(sy,  1); // faceX, z=+1
            int i2 = yBase + CornerIndex(sx,  1); // faceY, z=+1  (free=(X,Z))
            int i3 = yBase + CornerIndex(sx, -1); // faceY, z=-1
            edgeChamfers[e++] = WindFace([i0, i1, i2, i3], v, new Vector3(sx, sy, 0));
        }

        // X-axis edges (vary X) — faceY(sy) meets faceZ(sz). 4 edges.
        foreach (int sy in Signs)
        foreach (int sz in Signs)
        {
            int yBase = FaceYBase(sy), zBase = FaceZBase(sz);
            int i0 = yBase + CornerIndex(-1, sz); // faceY, x=-1  (free=(X,Z))
            int i1 = yBase + CornerIndex( 1, sz); // faceY, x=+1
            int i2 = zBase + CornerIndex( 1, sy); // faceZ, x=+1  (free=(X,Y))
            int i3 = zBase + CornerIndex(-1, sy); // faceZ, x=-1
            edgeChamfers[e++] = WindFace([i0, i1, i2, i3], v, new Vector3(0, sy, sz));
        }

        // Y-axis edges (vary Y) — faceX(sx) meets faceZ(sz). 4 edges.
        foreach (int sx in Signs)
        foreach (int sz in Signs)
        {
            int xBase = FaceXBase(sx), zBase = FaceZBase(sz);
            int i0 = xBase + CornerIndex(-1, sz); // faceX, y=-1  (free=(Y,Z))
            int i1 = xBase + CornerIndex( 1, sz); // faceX, y=+1
            int i2 = zBase + CornerIndex(sx,  1); // faceZ, y=+1  (free=(X,Y))
            int i3 = zBase + CornerIndex(sx, -1); // faceZ, y=-1
            edgeChamfers[e++] = WindFace([i0, i1, i2, i3], v, new Vector3(sx, 0, sz));
        }

        // Corner triangles — 8 octants (sx,sy,sz) ∈ {-1,+1}³. Each takes one vertex
        // from faceX(sx), one from faceY(sy), one from faceZ(sz) — the corner on each
        // face that matches the other two axes' signs for this octant.
        var cornerTris = new Face[8];
        int t = 0;
        foreach (int sx in Signs)
        foreach (int sy in Signs)
        foreach (int sz in Signs)
        {
            int vX = FaceXBase(sx) + CornerIndex(sy, sz);
            int vY = FaceYBase(sy) + CornerIndex(sx, sz);
            int vZ = FaceZBase(sz) + CornerIndex(sx, sy);
            cornerTris[t++] = WindFace([vX, vY, vZ], v, new Vector3(sx, sy, sz));
        }

        return new Result { Vertices = v, MainFaces = mainFaces,
                             EdgeChamfers = edgeChamfers, CornerTriangles = cornerTris };
    }

    // Base vertex index for the main face on the given axis with the given sign.
    private static int FaceXBase(int sx) => sx > 0 ? 0  : 4;
    private static int FaceYBase(int sy) => sy > 0 ? 8  : 12;
    private static int FaceZBase(int sz) => sz > 0 ? 16 : 20;

    // Maps a face's two free-axis signs (in that face's own (free1, free2) order) to
    // its corner index, per the (-,-),(+,-),(+,+),(-,+) layout used for all 24 vertices.
    private static int CornerIndex(int s1, int s2) => (s1, s2) switch
    {
        (-1, -1) => 0,
        ( 1, -1) => 1,
        ( 1,  1) => 2,
        (-1,  1) => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(s1)),
    };

    // Computes the naive winding, compares against the known expected outward
    // direction, reverses if they disagree. This replaces all hand-derived sign
    // branching from the old implementation — one generic, always-correct check.
    private static Face WindFace(int[] indices, Vector3[] verts, Vector3 expectedOutward)
    {
        Vector3 a = verts[indices[0]], b = verts[indices[1]], c = verts[indices[2]];
        Vector3 normal = Vector3.Cross(b - a, c - a);
        if (Vector3.Dot(normal, expectedOutward) < 0)
            Array.Reverse(indices);
        return new Face(indices);
    }
}
