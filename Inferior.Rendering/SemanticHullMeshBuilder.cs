using Inferior.Core.Math;
using Inferior.Gameplay.Hull;
using Microsoft.Xna.Framework;

namespace Inferior.Rendering;

public sealed record SemanticHullRenderVertex(
    Vector3 Position,
    Vector3 Normal);

public sealed record RenderedFaceRange(
    string FaceId,
    string MaterialGroup,
    HullSurfaceRole SurfaceRole,
    int StartIndex,
    int IndexCount,
    string? AssemblyId);

public sealed class SemanticHullCpuMesh
{
    public required IReadOnlyList<SemanticHullRenderVertex> Vertices { get; init; }
    public required IReadOnlyList<int> Indices { get; init; }
    public required IReadOnlyList<RenderedFaceRange> FaceRanges { get; init; }

    public int TriangleCount => Indices.Count / 3;
}

public static class SemanticHullMeshBuilder
{
    private const double AreaTolerance = 1e-12;

    public static SemanticHullCpuMesh Build(SemanticHullGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        var vertexById = geometry.Vertices.ToDictionary(v => v.Id, v => v.Position, StringComparer.Ordinal);
        var vertices = new List<SemanticHullRenderVertex>();
        var indices = new List<int>();
        var ranges = new List<RenderedFaceRange>();

        foreach (var face in geometry.Faces)
        {
            if (face.VertexIds.Count < 3)
                throw new InvalidOperationException($"Semantic hull face '{face.Id}' has fewer than three vertices.");

            var positions = new DVec3[face.VertexIds.Count];
            for (int i = 0; i < face.VertexIds.Count; i++)
            {
                string vertexId = face.VertexIds[i];
                if (!vertexById.TryGetValue(vertexId, out var position))
                    throw new InvalidOperationException($"Semantic hull face '{face.Id}' references unknown vertex '{vertexId}'.");

                positions[i] = position;
            }

            var normal = ToVector3(face.OutwardNormal.Normalized());
            int startIndex = indices.Count;

            for (int i = 1; i < positions.Length - 1; i++)
            {
                DVec3 a = positions[0];
                DVec3 b = positions[i];
                DVec3 c = positions[i + 1];
                DVec3 cross = DVec3.Cross(b - a, c - a);
                if (cross.LengthSquared <= AreaTolerance)
                    throw new InvalidOperationException($"Semantic hull face '{face.Id}' emitted a near-zero-area triangle.");

                if (DVec3.Dot(cross.Normalized(), face.OutwardNormal.Normalized()) <= 0.0)
                    throw new InvalidOperationException($"Semantic hull face '{face.Id}' winding disagrees with its semantic normal.");

                int baseVertex = vertices.Count;
                vertices.Add(new SemanticHullRenderVertex(ToVector3(a), normal));
                vertices.Add(new SemanticHullRenderVertex(ToVector3(b), normal));
                vertices.Add(new SemanticHullRenderVertex(ToVector3(c), normal));

                // Semantic polygons are authored CCW when viewed from outside. The shared
                // lit renderer uses CullCounterClockwise, so indices are emitted clockwise
                // while preserving the outward flat normal on every vertex.
                indices.Add(baseVertex);
                indices.Add(baseVertex + 2);
                indices.Add(baseVertex + 1);
            }

            ranges.Add(new RenderedFaceRange(
                face.Id,
                face.MaterialGroup,
                face.Role,
                startIndex,
                indices.Count - startIndex,
                face.AssemblyId));
        }

        return new SemanticHullCpuMesh
        {
            Vertices = vertices,
            Indices = indices,
            FaceRanges = ranges,
        };
    }

    private static Vector3 ToVector3(DVec3 value)
        => new((float)value.X, (float)value.Y, (float)value.Z);
}
