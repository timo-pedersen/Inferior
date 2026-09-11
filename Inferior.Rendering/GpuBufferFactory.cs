using Microsoft.Xna.Framework.Graphics;

namespace Inferior.Rendering;

/// <summary>
/// Shared GPU buffer allocation for the CPU-mesh-part uploaders (SemanticHullGpuMesh,
/// CockpitGpuMesh, EngineGpuMesh). Building a VertexBuffer + IndexBuffer from an
/// already-projected vertex array and an index array was three independent,
/// byte-for-byte-identical copies (A1 inventory finding) - the per-mesh-type part
/// mapping and record shape stay separate, only this allocate/SetData step is shared.
/// </summary>
internal static class GpuBufferFactory
{
    public static (VertexBuffer VertexBuffer, IndexBuffer IndexBuffer) Create(
        GraphicsDevice graphicsDevice,
        VertexPositionNormalColorTexture[] vertices,
        int[] indices)
    {
        var vertexBuffer = new VertexBuffer(
            graphicsDevice,
            VertexPositionNormalColorTexture.VertexDeclaration,
            vertices.Length,
            BufferUsage.WriteOnly);
        vertexBuffer.SetData(vertices);

        var indexBuffer = new IndexBuffer(
            graphicsDevice,
            IndexElementSize.ThirtyTwoBits,
            indices.Length,
            BufferUsage.WriteOnly);
        indexBuffer.SetData(indices);

        return (vertexBuffer, indexBuffer);
    }
}
