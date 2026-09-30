using Inferior.Rendering;

namespace Inferior.Game.Containers;

/// <summary>Main-thread render representation generated from immutable container data.</summary>
public sealed record ShippingContainerGeometry(
    VertexPositionNormalColorTexture[] Vertices,
    short[] Indices);
