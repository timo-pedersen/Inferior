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
            var vertexBuffer = new VertexBuffer(
                graphicsDevice,
                VertexPositionNormalColorTexture.VertexDeclaration,
                vertices.Length,
                BufferUsage.WriteOnly);
            vertexBuffer.SetData(vertices);

            int[] indices = part.Indices.ToArray();
            var indexBuffer = new IndexBuffer(
                graphicsDevice,
                IndexElementSize.ThirtyTwoBits,
                indices.Length,
                BufferUsage.WriteOnly);
            indexBuffer.SetData(indices);
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
