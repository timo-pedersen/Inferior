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

        Assert.NotEqual(generatedName.ManufacturerText, customName.ManufacturerText);
        Assert.Equal(generatedName.Vertices, customName.Vertices);
        Assert.Equal(generatedName.Indices, customName.Indices);
        Assert.Equal(generatedName.Vertices, emptyName.Vertices);
        Assert.Equal(generatedName.Indices, emptyName.Indices);
        Assert.Equal(632, generatedName.Vertices.Length);
        Assert.Equal(936, generatedName.Indices.Length);
    }
}
