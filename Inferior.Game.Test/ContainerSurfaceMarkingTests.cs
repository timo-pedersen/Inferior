using Inferior.Game.Containers;
using Microsoft.Xna.Framework;
using Xunit;

namespace Inferior.Game.Test;

/// <summary>
/// Container-specific proof-case coverage for the projected surface marking system (see
/// SurfaceMarkingGeometryTests.cs for the generic engine's own synthetic-fixture coverage).
/// Exercises the real ShippingContainerFactory-generated Z+ face across several seeds, so
/// different seeded inset row/col counts (1-8 rows, 1-4 cols) are all represented.
/// </summary>
public sealed class ContainerSurfaceMarkingTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(100)]
    [InlineData(12345)]
    public void DecalCrossesBothTheSurfaceLevelAndAnInsetFloor(int sidePatternSeed)
    {
        ShippingContainer container = ShippingContainerFactory.Generate(
            new Color(110, 84, 52), wear: 0.2f, sidePatternSeed);
        ShippingContainerGeometry geometry = ShippingContainerFactory.GenerateGeometry(container);

        var (verts, indices) = ContainerSurfaceMarking.BuildDecal(geometry);

        Assert.NotEmpty(indices);

        float minZ = verts.Min(v => v.Position.Z);
        float maxZ = verts.Max(v => v.Position.Z);

        // The Z+ face plane sits at z=1.25; the seeded inset depth is 3-5cm, so a marking
        // genuinely crossing an inset floor spans at least ~2cm of Z — this is the concrete
        // "changes in surface depth" claim from the design brief, not just a flat overlay.
        Assert.True(maxZ - minZ > 0.02f,
            $"seed {sidePatternSeed}: expected the marking to cross a real depth change, " +
            $"got minZ={minZ}, maxZ={maxZ}");

        // Every vertex still belongs to the Z+ face's physical envelope (surface at z=1.25,
        // down to at most the marking's own depth bound).
        Assert.True(maxZ <= 1.25f + 1e-3f);
        Assert.True(minZ >= 1.25f - 0.08f - 1e-3f);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(12345)]
    public void DecalGeometryIsDeterministic(int sidePatternSeed)
    {
        ShippingContainer containerA = ShippingContainerFactory.Generate(
            new Color(110, 84, 52), wear: 0.2f, sidePatternSeed);
        ShippingContainer containerB = ShippingContainerFactory.Generate(
            new Color(110, 84, 52), wear: 0.2f, sidePatternSeed);

        var (vertsA, indicesA) = ContainerSurfaceMarking.BuildDecal(
            ShippingContainerFactory.GenerateGeometry(containerA));
        var (vertsB, indicesB) = ContainerSurfaceMarking.BuildDecal(
            ShippingContainerFactory.GenerateGeometry(containerB));

        Assert.Equal(indicesA, indicesB);
        for (int i = 0; i < vertsA.Length; i++)
        {
            Assert.Equal(vertsA[i].Position, vertsB[i].Position);
            Assert.Equal(vertsA[i].TextureCoordinate, vertsB[i].TextureCoordinate);
        }
    }

    [Fact]
    public void DecalDoesNotModifyTheContainerMesh()
    {
        ShippingContainer container = ShippingContainerFactory.Generate(
            new Color(110, 84, 52), wear: 0.2f, sidePatternSeed: 777);
        ShippingContainerGeometry geometry = ShippingContainerFactory.GenerateGeometry(container);
        var originalVerts = (Inferior.Rendering.VertexPositionNormalColorTexture[])geometry.Vertices.Clone();
        var originalIndices = (short[])geometry.Indices.Clone();

        ContainerSurfaceMarking.BuildDecal(geometry);

        Assert.Equal(originalIndices, geometry.Indices);
        for (int i = 0; i < geometry.Vertices.Length; i++)
            Assert.Equal(originalVerts[i].Position, geometry.Vertices[i].Position);
    }
}
