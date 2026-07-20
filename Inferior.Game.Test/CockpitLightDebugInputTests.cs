using Inferior.Game.Input;
using Microsoft.Xna.Framework.Input;
using Xunit;

namespace Inferior.Game.Test;

public sealed class CockpitLightDebugInputTests
{
    [Fact]
    public void Read_MapsPlainF1AndControlF1ToCanopyLights()
    {
        var released = new KeyboardState();

        Assert.Equal(
            CockpitLightDebugAction.ToggleCanopy,
            CockpitLightDebugInput.Read(new KeyboardState(Keys.F1), released));
        Assert.Equal(
            CockpitLightDebugAction.ToggleCanopy,
            CockpitLightDebugInput.Read(
                new KeyboardState(Keys.LeftControl, Keys.F1),
                released));
    }

    [Theory]
    [InlineData(Keys.LeftShift)]
    [InlineData(Keys.RightShift)]
    public void Read_MapsShiftF1ToInternalLights(Keys shift)
    {
        Assert.Equal(
            CockpitLightDebugAction.ToggleInternal,
            CockpitLightDebugInput.Read(
                new KeyboardState(shift, Keys.F1),
                new KeyboardState()));
    }

    [Fact]
    public void Read_OnlyFiresOnF1RisingEdge()
    {
        var pressed = new KeyboardState(Keys.F1);

        Assert.Equal(
            CockpitLightDebugAction.None,
            CockpitLightDebugInput.Read(pressed, pressed));
        Assert.Equal(
            CockpitLightDebugAction.None,
            CockpitLightDebugInput.Read(
                new KeyboardState(Keys.LeftShift),
                new KeyboardState()));
    }
}
