using Inferior.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.Game.StationGen.Megastations;

public sealed record BoundaryMeshValidationReport(
    int NonFiniteVertexCount,
    int OutOfBoundsVertexCount,
    int DegenerateTriangleCount,
    int DuplicateTriangleCount,
    int OpenEdgeCount,
    int NonManifoldEdgeCount,
    int TJunctionCount,
    int SliverComponentCount,
    int TriangleCount,
    int VertexCount)
{
    public bool IsValid => NonFiniteVertexCount == 0
        && OutOfBoundsVertexCount == 0
        && DegenerateTriangleCount == 0
        && DuplicateTriangleCount == 0
        && OpenEdgeCount == 0
        && NonManifoldEdgeCount == 0
        && TJunctionCount == 0
        && SliverComponentCount == 0;

    public string Summary =>
        $"nonFinite={NonFiniteVertexCount} outOfBounds={OutOfBoundsVertexCount} degenerate={DegenerateTriangleCount} duplicate={DuplicateTriangleCount} openEdges={OpenEdgeCount} nonManifoldEdges={NonManifoldEdgeCount} tJunctions={TJunctionCount} sliverComponents={SliverComponentCount} triangles={TriangleCount} vertices={VertexCount}";
}

public static class BoundaryMeshValidator
{
    private const float AreaEpsilon = 1e-5f;
    private const float PositionQuantum = 0.0001f;

    public static BoundaryMeshValidationReport Validate(StationModuleMesh mesh, Vector3? boundsMin = null, Vector3? boundsMax = null)
    {
        var (verts, indices) = mesh.ToIntArrays();
        int nonFinite = verts.Count(v => !Finite(v.Position) || !Finite(v.Normal));
        int outOfBounds = boundsMin.HasValue && boundsMax.HasValue
            ? verts.Count(v => Outside(v.Position, boundsMin.Value, boundsMax.Value))
            : 0;
        int degenerate = 0;
        var triangleKeys = new HashSet<(PKey, PKey, PKey)>();
        int duplicateTriangles = 0;
        var edgeCounts = new Dictionary<(PKey, PKey), int>();
        var triangleEdges = new List<(PKey A, PKey B)>(indices.Length);
        var vertexKeys = new HashSet<PKey>();

        for (int i = 0; i < indices.Length; i += 3)
        {
            Vector3 a = verts[indices[i]].Position;
            Vector3 b = verts[indices[i + 1]].Position;
            Vector3 c = verts[indices[i + 2]].Position;
            if (Vector3.Cross(b - a, c - a).Length() <= AreaEpsilon)
                degenerate++;

            PKey pa = Quantize(a), pb = Quantize(b), pc = Quantize(c);
            vertexKeys.Add(pa);
            vertexKeys.Add(pb);
            vertexKeys.Add(pc);
            var ordered = new[] { pa, pb, pc }.Order().ToArray();
            if (!triangleKeys.Add((ordered[0], ordered[1], ordered[2])))
                duplicateTriangles++;

            AddEdge(edgeCounts, triangleEdges, pa, pb);
            AddEdge(edgeCounts, triangleEdges, pb, pc);
            AddEdge(edgeCounts, triangleEdges, pc, pa);
        }

        int openEdges = edgeCounts.Count(kv => kv.Value == 1);
        int nonManifold = edgeCounts.Count(kv => kv.Value > 2);
        int tJunctions = CountAxisAlignedTJunctions(triangleEdges, vertexKeys);
        int sliverComponents = CountSliverComponents(indices, verts);
        return new BoundaryMeshValidationReport(nonFinite, outOfBounds, degenerate, duplicateTriangles, openEdges, nonManifold, tJunctions, sliverComponents, indices.Length / 3, verts.Length);
    }

    private static void AddEdge(Dictionary<(PKey, PKey), int> edgeCounts, List<(PKey A, PKey B)> edges, PKey a, PKey b)
    {
        var key = a.CompareTo(b) <= 0 ? (a, b) : (b, a);
        edgeCounts[key] = edgeCounts.GetValueOrDefault(key) + 1;
        edges.Add(key);
    }

    private static int CountAxisAlignedTJunctions(IReadOnlyList<(PKey A, PKey B)> edges, IReadOnlySet<PKey> vertices)
    {
        var xLines = new Dictionary<(int X, int Y), SortedSet<int>>();
        var yLines = new Dictionary<(int X, int Z), SortedSet<int>>();
        var zLines = new Dictionary<(int Y, int Z), SortedSet<int>>();
        foreach (PKey vertex in vertices)
        {
            AddLineCoordinate(xLines, (vertex.X, vertex.Y), vertex.Z);
            AddLineCoordinate(yLines, (vertex.X, vertex.Z), vertex.Y);
            AddLineCoordinate(zLines, (vertex.Y, vertex.Z), vertex.X);
        }

        int count = 0;
        foreach (var (a, b) in edges.Distinct())
        {
            if (a.X == b.X && a.Y == b.Y)
                count += CountInteriorCoordinates(xLines[(a.X, a.Y)], a.Z, b.Z);
            else if (a.X == b.X && a.Z == b.Z)
                count += CountInteriorCoordinates(yLines[(a.X, a.Z)], a.Y, b.Y);
            else if (a.Y == b.Y && a.Z == b.Z)
                count += CountInteriorCoordinates(zLines[(a.Y, a.Z)], a.X, b.X);
        }
        return count;
    }

    private static void AddLineCoordinate<TKey>(Dictionary<TKey, SortedSet<int>> lines, TKey key, int coordinate)
        where TKey : notnull
    {
        if (!lines.TryGetValue(key, out var coordinates))
        {
            coordinates = [];
            lines[key] = coordinates;
        }
        coordinates.Add(coordinate);
    }

    private static int CountInteriorCoordinates(SortedSet<int> coordinates, int a, int b)
    {
        int min = Math.Min(a, b);
        int max = Math.Max(a, b);
        if (max - min <= 1) return 0;
        int count = 0;
        foreach (int coordinate in coordinates.GetViewBetween(min + 1, max - 1))
            count++;
        return count;
    }

    private static int CountSliverComponents(int[] indices, VertexPositionNormalColorTexture[] verts)
    {
        var vertexToTriangles = new Dictionary<PKey, List<int>>();
        int triangleCount = indices.Length / 3;
        for (int tri = 0; tri < triangleCount; tri++)
        {
            for (int i = 0; i < 3; i++)
            {
                PKey key = Quantize(verts[indices[tri * 3 + i]].Position);
                if (!vertexToTriangles.TryGetValue(key, out var list))
                {
                    list = [];
                    vertexToTriangles[key] = list;
                }
                list.Add(tri);
            }
        }

        var seen = new bool[triangleCount];
        int sliver = 0;
        for (int tri = 0; tri < triangleCount; tri++)
        {
            if (seen[tri]) continue;
            int count = 0;
            var q = new Queue<int>();
            q.Enqueue(tri);
            seen[tri] = true;
            while (q.Count > 0)
            {
                int current = q.Dequeue();
                count++;
                for (int i = 0; i < 3; i++)
                {
                    PKey key = Quantize(verts[indices[current * 3 + i]].Position);
                    foreach (int next in vertexToTriangles[key])
                    {
                        if (seen[next]) continue;
                        seen[next] = true;
                        q.Enqueue(next);
                    }
                }
            }
            if (count < 4)
                sliver++;
        }
        return sliver;
    }

    private static bool Finite(Vector3 value)
        => !float.IsNaN(value.X) && !float.IsInfinity(value.X)
        && !float.IsNaN(value.Y) && !float.IsInfinity(value.Y)
        && !float.IsNaN(value.Z) && !float.IsInfinity(value.Z);

    private static bool Outside(Vector3 value, Vector3 min, Vector3 max)
        => value.X < min.X - 0.001f || value.X > max.X + 0.001f
        || value.Y < min.Y - 0.001f || value.Y > max.Y + 0.001f
        || value.Z < min.Z - 0.001f || value.Z > max.Z + 0.001f;

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
