using Inferior.Gameplay.Engines;
using Microsoft.Xna.Framework;

namespace Inferior.Rendering;

public sealed record EngineRenderVertex(Vector3 Position, Vector3 Normal);

public sealed record EngineCpuMeshPart(
    EngineVisualMaterial Material,
    IReadOnlyList<EngineRenderVertex> Vertices,
    IReadOnlyList<int> Indices);

public sealed record EngineCpuMesh(IReadOnlyList<EngineCpuMeshPart> Parts);

public static class EngineMeshBuilder
{
    public static EngineCpuMesh Build(EngineVisualGeometry geometry, bool mirroredAcrossHullX)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        var parts = new List<EngineCpuMeshPart>(geometry.MeshParts.Count);

        foreach (EngineVisualMeshPart sourcePart in geometry.MeshParts)
        {
            var vertices = new List<EngineRenderVertex>(sourcePart.Triangles.Count * 3);
            var indices = new List<int>(sourcePart.Triangles.Count * 3);

            foreach (EngineVisualTriangle sourceTriangle in sourcePart.Triangles)
            {
                Vector3 a = ToVector3(sourceTriangle.A, mirroredAcrossHullX);
                Vector3 b = ToVector3(sourceTriangle.B, mirroredAcrossHullX);
                Vector3 c = ToVector3(sourceTriangle.C, mirroredAcrossHullX);
                if (mirroredAcrossHullX)
                    (b, c) = (c, b);

                Vector3 normal = Vector3.Normalize(Vector3.Cross(b - a, c - a));
                int baseVertex = vertices.Count;
                vertices.Add(new EngineRenderVertex(a, normal));
                vertices.Add(new EngineRenderVertex(b, normal));
                vertices.Add(new EngineRenderVertex(c, normal));
                indices.Add(baseVertex);
                indices.Add(baseVertex + 2);
                indices.Add(baseVertex + 1);
            }

            parts.Add(new EngineCpuMeshPart(
                sourcePart.Material,
                Array.AsReadOnly(vertices.ToArray()),
                Array.AsReadOnly(indices.ToArray())));
        }

        return new EngineCpuMesh(Array.AsReadOnly(parts.ToArray()));
    }

    public static Vector3 ToVector3(Inferior.Core.Math.DVec3 value, bool mirroredAcrossHullX)
        => new(
            (float)(mirroredAcrossHullX ? -value.X : value.X),
            (float)value.Y,
            (float)value.Z);
}
