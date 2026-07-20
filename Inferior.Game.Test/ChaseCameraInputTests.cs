using Inferior.Game.States;
using Inferior.Gameplay;
using Microsoft.Xna.Framework.Input;
using Xunit;

namespace Inferior.Game.Test;

public sealed class ChaseCameraInputTests
{
    [Fact]
    public void OrbitalKeys_MapToCameraEditAxes()
    {
        var keys = new KeyboardState(Keys.W, Keys.D, Keys.E, Keys.F, Keys.X);

        ChaseCameraEditInput input = SystemSpaceState.ReadChaseCameraEditInput(
            keys,
            new KeyboardState());

        Assert.Equal(1.0, input.Horizontal);
        Assert.Equal(1.0, input.Vertical);
        Assert.Equal(1.0, input.Roll);
        Assert.Equal(1.0, input.Radial);
        Assert.True(input.Reset);
    }

    [Fact]
    public void ConsumedOrbitalKeys_DoNotReachShipAxesOrXStop()
    {
        var flightInput = new PlayerInput(
            1, 1, 1, 1, 1, 1, false,
            FlightAssistToggle: true,
            SlipstreamToggle: true,
            XStopToggle: true,
            XStopToggleSequence: 42,
            GearUp: true,
            AfterburnerToggle: true);

        PlayerInput consumed = SystemSpaceState.ConsumeOrbitalCameraFlightInput(flightInput);

        Assert.Equal(0.0, consumed.ThrustForward);
        Assert.Equal(0.0, consumed.ThrustLateral);
        Assert.Equal(0.0, consumed.ThrustVertical);
        Assert.Equal(0.0, consumed.RollInput);
        Assert.Equal(0.0, consumed.PitchInput);
        Assert.Equal(0.0, consumed.YawInput);
        Assert.False(consumed.XStopToggle);
        Assert.Equal(0, consumed.XStopToggleSequence);
        Assert.True(consumed.FlightAssistToggle);
        Assert.True(consumed.SlipstreamToggle);
        Assert.True(consumed.GearUp);
        Assert.True(consumed.AfterburnerToggle);
    }
}
