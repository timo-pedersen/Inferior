using Microsoft.Xna.Framework.Graphics;

namespace Inferior.Rendering;

public sealed class SemanticHullGpuMeshPart : IDisposable
{
    public required SemanticHullRenderGroup RenderGroup { get; init; }
    public required string MaterialGroup { get; init; }
    public required Microsoft.Xna.Framework.Color MaterialColour { get; init; }
    public required VertexBuffer VertexBuffer { get; init; }
    public required IndexBuffer IndexBuffer { get; init; }
    public required IReadOnlyList<RenderedFaceRange> FaceRanges { get; init; }

    public int TriangleCount => IndexBuffer.IndexCount / 3;

    public void Dispose()
    {
        VertexBuffer.Dispose();
        IndexBuffer.Dispose();
    }
}

public sealed class SemanticHullGpuMesh : IDisposable
{
    public required IReadOnlyList<SemanticHullGpuMeshPart> Parts { get; init; }

    public int TriangleCount => Parts.Sum(part => part.TriangleCount);
    public IEnumerable<RenderedFaceRange> FaceRanges => Parts.SelectMany(part => part.FaceRanges);

    public static SemanticHullGpuMesh Create(GraphicsDevice graphicsDevice, SemanticHullCpuMesh cpuMesh)
    {
        ArgumentNullException.ThrowIfNull(graphicsDevice);
        ArgumentNullException.ThrowIfNull(cpuMesh);

        var parts = new List<SemanticHullGpuMeshPart>(cpuMesh.Parts.Count);
        foreach (var part in cpuMesh.Parts)
        {
            var vertices = part.Vertices
                .Select(vertex => new VertexPositionNormalColorTexture(
                    vertex.Position,
                    vertex.Normal,
                    vertex.Colour,
                    vertex.TextureCoordinate))
                .ToArray();
            var indices = part.Indices.ToArray();

            if (vertices.Length == 0 || indices.Length == 0)
                continue;

            var vb = new VertexBuffer(
                graphicsDevice,
                VertexPositionNormalColorTexture.VertexDeclaration,
                vertices.Length,
                BufferUsage.WriteOnly);
            vb.SetData(vertices);

            var ib = new IndexBuffer(
                graphicsDevice,
                IndexElementSize.ThirtyTwoBits,
                indices.Length,
                BufferUsage.WriteOnly);
            ib.SetData(indices);

            parts.Add(new SemanticHullGpuMeshPart
            {
                RenderGroup = part.RenderGroup,
                MaterialGroup = part.MaterialGroup,
                MaterialColour = part.MaterialColour,
                VertexBuffer = vb,
                IndexBuffer = ib,
                FaceRanges = part.FaceRanges,
            });
        }

        return new SemanticHullGpuMesh { Parts = parts };
    }

    public void Dispose()
    {
        foreach (var part in Parts)
            part.Dispose();
    }
}
