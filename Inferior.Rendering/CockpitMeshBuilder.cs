using Inferior.Gameplay.Cockpit;
using Microsoft.Xna.Framework;

namespace Inferior.Rendering;

public sealed record CockpitRenderVertex(Vector3 Position, Vector3 Normal);

public sealed record CockpitCpuMeshPart(
    string PartId,
    CockpitVisualMaterial Material,
    IReadOnlyList<CockpitRenderVertex> Vertices,
    IReadOnlyList<int> Indices);

public sealed record CockpitCpuMesh(IReadOnlyList<CockpitCpuMeshPart> Parts);

public static class CockpitMeshBuilder
{
    public static CockpitCpuMesh Build(CockpitVisualGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        var parts = new List<CockpitCpuMeshPart>(geometry.MeshParts.Count);

        foreach (CockpitVisualMeshPart sourcePart in geometry.MeshParts)
        {
            var vertices = new List<CockpitRenderVertex>(sourcePart.Triangles.Count * 3);
            var indices = new List<int>(sourcePart.Triangles.Count * 3);

            foreach (CockpitVisualTriangle triangle in sourcePart.Triangles)
            {
                Vector3 a = triangle.A.ToVector3();
                Vector3 b = triangle.B.ToVector3();
                Vector3 c = triangle.C.ToVector3();
                Vector3 cross = Vector3.Cross(b - a, c - a);
                if (cross.LengthSquared() <= 1e-12f)
                {
                    throw new InvalidOperationException(
                        $"Cockpit part '{sourcePart.PartId}' contains a near-zero-area triangle.");
                }

                Vector3 normal = Vector3.Normalize(cross);
                int baseVertex = vertices.Count;
                vertices.Add(new CockpitRenderVertex(a, normal));
                vertices.Add(new CockpitRenderVertex(b, normal));
                vertices.Add(new CockpitRenderVertex(c, normal));

                indices.Add(baseVertex);
                indices.Add(baseVertex + 2);
                indices.Add(baseVertex + 1);
            }

            parts.Add(new CockpitCpuMeshPart(
                sourcePart.PartId,
                sourcePart.Material,
                Array.AsReadOnly(vertices.ToArray()),
                Array.AsReadOnly(indices.ToArray())));
        }

        return new CockpitCpuMesh(Array.AsReadOnly(parts.ToArray()));
    }
}
