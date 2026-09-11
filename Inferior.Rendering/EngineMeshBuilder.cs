using Inferior.Gameplay.Engines;
using Microsoft.Xna.Framework;

namespace Inferior.Rendering;

public sealed record EngineRenderVertex(Vector3 Position, Vector3 Normal);

public sealed record EngineCpuMeshPart(
    string PartId,
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
                sourcePart.PartId,
                sourcePart.Material,
                Array.AsReadOnly(vertices.ToArray()),
                Array.AsReadOnly(indices.ToArray())));
        }

        return new EngineCpuMesh(Array.AsReadOnly(parts.ToArray()));
    }

    // A1 inventory finding: this used to fuse the DVec3->Vector3 narrowing and the
    // mirroredAcrossHullX sign flip into one set of (float) casts - now delegates the
    // conversion to the shared DVec3.ToVector3() and applies the mirror as its own explicit
    // step. Kept public here (rather than folded away) since ShipMeshRenderer and tests
    // call this exact overload for the mirrored case.
    public static Vector3 ToVector3(Inferior.Core.Math.DVec3 value, bool mirroredAcrossHullX)
    {
        Vector3 v = value.ToVector3();
        return mirroredAcrossHullX ? new Vector3(-v.X, v.Y, v.Z) : v;
    }
}
