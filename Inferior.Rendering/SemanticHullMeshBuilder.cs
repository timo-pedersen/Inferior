using Inferior.Core.Math;
using Inferior.Gameplay.Hull;
using Microsoft.Xna.Framework;

namespace Inferior.Rendering;

public enum SemanticHullRenderGroup
{
    StructuralHull,
    CargoDoor,
    CockpitFrame,
    CockpitGlass,
}

public sealed record SemanticHullRenderVertex(
    Vector3 Position,
    Vector3 Normal,
    Color Colour,
    Vector2 TextureCoordinate);

public sealed record RenderedFaceRange(
    string FaceId,
    SemanticHullRenderGroup RenderGroup,
    HullSurfaceRole SurfaceRole,
    int StartIndex,
    int IndexCount,
    string? AssemblyId);

public sealed class SemanticHullMeshPart
{
    public required SemanticHullRenderGroup RenderGroup { get; init; }
    public required string MaterialGroup { get; init; }
    public required Color MaterialColour { get; init; }
    public required IReadOnlyList<SemanticHullRenderVertex> Vertices { get; init; }
    public required IReadOnlyList<int> Indices { get; init; }
    public required IReadOnlyList<RenderedFaceRange> FaceRanges { get; init; }

    public int TriangleCount => Indices.Count / 3;
}

public sealed class SemanticHullCpuMesh
{
    public required IReadOnlyList<SemanticHullMeshPart> Parts { get; init; }

    public int TriangleCount => Parts.Sum(part => part.TriangleCount);
    public IEnumerable<RenderedFaceRange> FaceRanges => Parts.SelectMany(part => part.FaceRanges);
}

public static class SemanticHullMeshBuilder
{
    private const double AreaTolerance = 1e-12;
    public const float MetresPerUvUnit = 2.0f;

    public static SemanticHullCpuMesh Build(SemanticHullGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        var vertexById = geometry.Vertices.ToDictionary(v => v.Id, v => v.Position, StringComparer.Ordinal);
        var builders = new Dictionary<SemanticHullRenderGroup, PartBuilder>();

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
            SemanticHullRenderGroup group = GroupFor(face.Role);
            PartBuilder part = GetPartBuilder(builders, group);
            int startIndex = part.Indices.Count;
            (Vector3 tangent, Vector3 bitangent) = BuildUvBasis(positions, normal, face.Id);

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

                int baseVertex = part.Vertices.Count;
                part.Vertices.Add(new SemanticHullRenderVertex(ToVector3(a), normal, Color.White, ProjectUv(a, tangent, bitangent)));
                part.Vertices.Add(new SemanticHullRenderVertex(ToVector3(b), normal, Color.White, ProjectUv(b, tangent, bitangent)));
                part.Vertices.Add(new SemanticHullRenderVertex(ToVector3(c), normal, Color.White, ProjectUv(c, tangent, bitangent)));

                // Semantic polygons are authored CCW when viewed from outside. The shared
                // lit renderer uses CullCounterClockwise, so indices are emitted clockwise
                // while preserving the outward flat normal on every vertex.
                part.Indices.Add(baseVertex);
                part.Indices.Add(baseVertex + 2);
                part.Indices.Add(baseVertex + 1);
            }

            part.FaceRanges.Add(new RenderedFaceRange(
                face.Id,
                group,
                face.Role,
                startIndex,
                part.Indices.Count - startIndex,
                face.AssemblyId));
        }

        return new SemanticHullCpuMesh
        {
            Parts = builders
                .OrderBy(pair => pair.Key)
                .Select(pair => pair.Value.Build())
                .ToArray(),
        };
    }

    private static Vector3 ToVector3(DVec3 value)
        => new((float)value.X, (float)value.Y, (float)value.Z);

    private static PartBuilder GetPartBuilder(Dictionary<SemanticHullRenderGroup, PartBuilder> builders, SemanticHullRenderGroup group)
    {
        if (builders.TryGetValue(group, out var builder))
            return builder;

        builder = new PartBuilder(group, MaterialGroupName(group), MaterialColour(group));
        builders.Add(group, builder);
        return builder;
    }

    private static SemanticHullRenderGroup GroupFor(HullSurfaceRole role)
        => role switch
        {
            HullSurfaceRole.CargoDoor => SemanticHullRenderGroup.CargoDoor,
            HullSurfaceRole.CockpitFrame => SemanticHullRenderGroup.CockpitFrame,
            HullSurfaceRole.CockpitGlass => SemanticHullRenderGroup.CockpitGlass,
            _ => SemanticHullRenderGroup.StructuralHull,
        };

    private static string MaterialGroupName(SemanticHullRenderGroup group)
        => group switch
        {
            SemanticHullRenderGroup.StructuralHull => "structural-hull",
            SemanticHullRenderGroup.CargoDoor => "cargo-door-structure",
            SemanticHullRenderGroup.CockpitFrame => "cockpit-frame",
            SemanticHullRenderGroup.CockpitGlass => "cockpit-glass",
            _ => throw new ArgumentOutOfRangeException(nameof(group), group, null),
        };

    private static Color MaterialColour(SemanticHullRenderGroup group)
        => group switch
        {
            SemanticHullRenderGroup.StructuralHull => new Color(72, 78, 78),
            SemanticHullRenderGroup.CargoDoor => new Color(64, 70, 66),
            SemanticHullRenderGroup.CockpitFrame => new Color(48, 52, 54),
            SemanticHullRenderGroup.CockpitGlass => new Color(18, 34, 44),
            _ => throw new ArgumentOutOfRangeException(nameof(group), group, null),
        };

    private static (Vector3 tangent, Vector3 bitangent) BuildUvBasis(IReadOnlyList<DVec3> positions, Vector3 normal, string faceId)
    {
        Vector3 origin = ToVector3(positions[0]);
        Vector3 tangent = Vector3.Zero;
        for (int i = 1; i < positions.Count; i++)
        {
            Vector3 edge = ToVector3(positions[i]) - origin;
            edge -= normal * Vector3.Dot(edge, normal);
            if (edge.LengthSquared() > 1e-10f)
            {
                tangent = Vector3.Normalize(edge);
                break;
            }
        }

        if (tangent.LengthSquared() <= 1e-10f)
            throw new InvalidOperationException($"Semantic hull face '{faceId}' cannot build a deterministic UV tangent.");

        Vector3 bitangent = Vector3.Normalize(Vector3.Cross(normal, tangent));
        return (tangent, bitangent);
    }

    private static Vector2 ProjectUv(DVec3 position, Vector3 tangent, Vector3 bitangent)
    {
        var p = ToVector3(position);
        return new Vector2(
            Vector3.Dot(p, tangent) / MetresPerUvUnit,
            Vector3.Dot(p, bitangent) / MetresPerUvUnit);
    }

    private sealed class PartBuilder(
        SemanticHullRenderGroup group,
        string materialGroup,
        Color materialColour)
    {
        public List<SemanticHullRenderVertex> Vertices { get; } = [];
        public List<int> Indices { get; } = [];
        public List<RenderedFaceRange> FaceRanges { get; } = [];

        public SemanticHullMeshPart Build()
            => new()
            {
                RenderGroup = group,
                MaterialGroup = materialGroup,
                MaterialColour = materialColour,
                Vertices = Vertices.ToArray(),
                Indices = Indices.ToArray(),
                FaceRanges = FaceRanges.ToArray(),
            };
    }
}
