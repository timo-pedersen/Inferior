using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.Game.StationGen.Megastations;

public sealed record BoundaryMeshValidationReport(
    int NonFiniteVertexCount,
    int DegenerateTriangleCount,
    int DuplicateTriangleCount,
    int NonManifoldEdgeCount,
    int TriangleCount,
    int VertexCount)
{
    public bool IsValid => NonFiniteVertexCount == 0
        && DegenerateTriangleCount == 0
        && DuplicateTriangleCount == 0
        && NonManifoldEdgeCount == 0;

    public string Summary =>
        $"nonFinite={NonFiniteVertexCount} degenerate={DegenerateTriangleCount} duplicate={DuplicateTriangleCount} nonManifoldEdges={NonManifoldEdgeCount} triangles={TriangleCount} vertices={VertexCount}";
}

public static class BoundaryMeshValidator
{
    private const float AreaEpsilon = 1e-5f;
    private const float PositionQuantum = 0.0001f;

    public static BoundaryMeshValidationReport Validate(StationModuleMesh mesh)
    {
        var (verts, indices) = mesh.ToIntArrays();
        int nonFinite = verts.Count(v => !Finite(v.Position) || !Finite(v.Normal));
        int degenerate = 0;
        var triangleKeys = new HashSet<(PKey, PKey, PKey)>();
        int duplicateTriangles = 0;
        var edgeCounts = new Dictionary<(PKey, PKey), int>();

        for (int i = 0; i < indices.Length; i += 3)
        {
            Vector3 a = verts[indices[i]].Position;
            Vector3 b = verts[indices[i + 1]].Position;
            Vector3 c = verts[indices[i + 2]].Position;
            if (Vector3.Cross(b - a, c - a).Length() <= AreaEpsilon)
                degenerate++;

            PKey pa = Quantize(a), pb = Quantize(b), pc = Quantize(c);
            var ordered = new[] { pa, pb, pc }.Order().ToArray();
            if (!triangleKeys.Add((ordered[0], ordered[1], ordered[2])))
                duplicateTriangles++;

            AddEdge(edgeCounts, pa, pb);
            AddEdge(edgeCounts, pb, pc);
            AddEdge(edgeCounts, pc, pa);
        }

        int nonManifold = edgeCounts.Count(kv => kv.Value != 2);
        return new BoundaryMeshValidationReport(nonFinite, degenerate, duplicateTriangles, nonManifold, indices.Length / 3, verts.Length);
    }

    private static void AddEdge(Dictionary<(PKey, PKey), int> edgeCounts, PKey a, PKey b)
    {
        var key = a.CompareTo(b) <= 0 ? (a, b) : (b, a);
        edgeCounts[key] = edgeCounts.GetValueOrDefault(key) + 1;
    }

    private static bool Finite(Vector3 value)
        => !float.IsNaN(value.X) && !float.IsInfinity(value.X)
        && !float.IsNaN(value.Y) && !float.IsInfinity(value.Y)
        && !float.IsNaN(value.Z) && !float.IsInfinity(value.Z);

    private static PKey Quantize(Vector3 value)
        => new(
            (int)MathF.Round(value.X / PositionQuantum),
            (int)MathF.Round(value.Y / PositionQuantum),
            (int)MathF.Round(value.Z / PositionQuantum));

    private readonly record struct PKey(int X, int Y, int Z) : IComparable<PKey>
    {
        public int CompareTo(PKey other)
        {
            int c = X.CompareTo(other.X);
            if (c != 0) return c;
            c = Y.CompareTo(other.Y);
            return c != 0 ? c : Z.CompareTo(other.Z);
        }
    }
}
