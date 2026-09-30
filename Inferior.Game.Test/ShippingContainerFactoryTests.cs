using Inferior.Game.Containers;
using Microsoft.Xna.Framework;
using Xunit;

namespace Inferior.Game.Test;

public sealed class ShippingContainerFactoryTests
{
    [Fact]
    public void ContainerGeometryDoesNotContainOrDependOnManufacturerText()
    {
        ShippingContainer generatedName = ShippingContainerFactory.Generate(
            new Color(110, 84, 52), .42f, 12345);
        ShippingContainer customName = ShippingContainerFactory.Generate(
            new Color(110, 84, 52), .42f, 12345, "INTERSTELLAR FREIGHT CO.");
        ShippingContainer emptyName = ShippingContainerFactory.Generate(
            new Color(110, 84, 52), .42f, 12345, string.Empty);
        ShippingContainerGeometry generatedGeometry = ShippingContainerFactory.GenerateGeometry(generatedName);
        ShippingContainerGeometry customGeometry = ShippingContainerFactory.GenerateGeometry(customName);
        ShippingContainerGeometry emptyGeometry = ShippingContainerFactory.GenerateGeometry(emptyName);

        Assert.NotEqual(generatedName.ManufacturerText, customName.ManufacturerText);
        Assert.Equal(generatedGeometry.Vertices, customGeometry.Vertices);
        Assert.Equal(generatedGeometry.Indices, customGeometry.Indices);
        Assert.Equal(generatedGeometry.Vertices, emptyGeometry.Vertices);
        Assert.Equal(generatedGeometry.Indices, emptyGeometry.Indices);
        Assert.Equal(632, generatedGeometry.Vertices.Length);
        Assert.Equal(936, generatedGeometry.Indices.Length);
    }
}
