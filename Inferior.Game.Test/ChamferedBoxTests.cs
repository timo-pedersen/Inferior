using Inferior.Rendering;
using Microsoft.Xna.Framework;
using Xunit;

namespace Inferior.Game.Test;

public class ChamferedBoxTests
{
    private static readonly ChamferedBox.Result Box =
        ChamferedBox.Build(new Vector3(3.0f, 1.25f, 1.25f), 0.20f);

    [Fact]
    public void Build_ProducesExpectedFaceCounts()
    {
        Assert.Equal(24, Box.Vertices.Length);
        Assert.Equal(6, Box.MainFaces.Length);
        Assert.Equal(12, Box.EdgeChamfers.Length);
        Assert.Equal(8, Box.CornerTriangles.Length);
    }

    [Fact]
    public void MainFaces_AreQuadsWoundOutward()
    {
        foreach (var face in Box.MainFaces)
            AssertWoundOutward(face, expectedIndexCount: 4);
    }

    [Fact]
    public void EdgeChamfers_AreQuadsWoundOutward()
    {
        foreach (var face in Box.EdgeChamfers)
            AssertWoundOutward(face, expectedIndexCount: 4);
    }

    [Fact]
    public void CornerTriangles_AreTrianglesWoundOutward()
    {
        foreach (var face in Box.CornerTriangles)
            AssertWoundOutward(face, expectedIndexCount: 3);
    }

    [Fact]
    public void AllFaces_OnlyReferenceTheCanonical24Vertices()
    {
        var allFaces = Box.MainFaces.Concat(Box.EdgeChamfers).Concat(Box.CornerTriangles);
        foreach (var face in allFaces)
            foreach (int i in face.Indices)
                Assert.InRange(i, 0, 23);
    }

    [Fact]
    public void EveryEdgeIsSharedByExactlyTwoFaces()
    {
        // A closed, gap-free, non-overlapping mesh has each undirected vertex-pair
        // edge appear in exactly two faces (once per adjacent face winding it CW).
        var allFaces = Box.MainFaces.Concat(Box.EdgeChamfers).Concat(Box.CornerTriangles).ToList();
        var edgeCounts = new Dictionary<(int, int), int>();

        foreach (var face in allFaces)
        {
            int n = face.Indices.Length;
            for (int i = 0; i < n; i++)
            {
                int a = face.Indices[i];
                int b = face.Indices[(i + 1) % n];
                var key = a < b ? (a, b) : (b, a);
                edgeCounts[key] = edgeCounts.GetValueOrDefault(key) + 1;
            }
        }

        var badEdges = edgeCounts.Where(kv => kv.Value != 2).ToList();
        Assert.True(badEdges.Count == 0,
            "Edges not shared by exactly 2 faces (gap or overlap): " +
            string.Join(", ", badEdges.Select(kv => $"{kv.Key}={kv.Value}")));
    }

    private static void AssertWoundOutward(ChamferedBox.Face face, int expectedIndexCount)
    {
        Assert.Equal(expectedIndexCount, face.Indices.Length);

        Vector3 a = Box.Vertices[face.Indices[0]];
        Vector3 b = Box.Vertices[face.Indices[1]];
        Vector3 c = Box.Vertices[face.Indices[2]];
        Vector3 normal = Vector3.Cross(b - a, c - a);

        // Box is centred at the origin, so the face centroid direction from the
        // origin is a reliable proxy for "outward" on a convex shape like this.
        Vector3 centroid = Vector3.Zero;
        foreach (int i in face.Indices) centroid += Box.Vertices[i];
        centroid /= face.Indices.Length;

        Assert.True(Vector3.Dot(normal, centroid) > 0,
            $"Face {{{string.Join(",", face.Indices)}}} winds inward (normal={normal}, centroid={centroid})");
    }
}
