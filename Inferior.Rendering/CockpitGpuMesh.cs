using Inferior.Gameplay.Cockpit;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.Rendering;

public sealed record CockpitGpuMeshPart(
    string PartId,
    CockpitVisualMaterial Material,
    VertexBuffer VertexBuffer,
    IndexBuffer IndexBuffer) : IDisposable
{
    public void Dispose()
    {
        VertexBuffer.Dispose();
        IndexBuffer.Dispose();
    }
}

public sealed record CockpitGpuMesh(IReadOnlyList<CockpitGpuMeshPart> Parts) : IDisposable
{
    public static CockpitGpuMesh Create(GraphicsDevice graphicsDevice, CockpitCpuMesh cpuMesh)
    {
        CockpitGpuMeshPart[] parts = cpuMesh.Parts.Select(part =>
        {
            VertexPositionNormalColorTexture[] vertices = part.Vertices
                .Select(vertex => new VertexPositionNormalColorTexture(
                    vertex.Position,
                    vertex.Normal,
                    Color.White,
                    Vector2.Zero))
                .ToArray();
            int[] indices = part.Indices.ToArray();
            (VertexBuffer vertexBuffer, IndexBuffer indexBuffer) = GpuBufferFactory.Create(
                graphicsDevice, vertices, indices);
            return new CockpitGpuMeshPart(
                part.PartId,
                part.Material,
                vertexBuffer,
                indexBuffer);
        }).ToArray();

        return new CockpitGpuMesh(Array.AsReadOnly(parts));
    }

    public void Dispose()
    {
        foreach (CockpitGpuMeshPart part in Parts)
            part.Dispose();
    }
}
