using Inferior.Gameplay.Engines;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.Rendering;

public sealed record EngineGpuMeshPart(
    string PartId,
    EngineVisualMaterial Material,
    VertexBuffer VertexBuffer,
    IndexBuffer IndexBuffer) : IDisposable
{
    public void Dispose()
    {
        VertexBuffer.Dispose();
        IndexBuffer.Dispose();
    }
}

public sealed record EngineGpuMesh(IReadOnlyList<EngineGpuMeshPart> Parts) : IDisposable
{
    public static EngineGpuMesh Create(GraphicsDevice graphicsDevice, EngineCpuMesh cpuMesh)
    {
        var parts = cpuMesh.Parts.Select(part =>
        {
            var vertices = part.Vertices
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
            return new EngineGpuMeshPart(part.PartId, part.Material, vertexBuffer, indexBuffer);
        }).ToArray();

        return new EngineGpuMesh(Array.AsReadOnly(parts));
    }

    public void Dispose()
    {
        foreach (EngineGpuMeshPart part in Parts)
            part.Dispose();
    }
}
