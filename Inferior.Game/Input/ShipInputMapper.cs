using Inferior.Gameplay;
using Microsoft.Xna.Framework.Input;

namespace Inferior.Game.Input;

internal static class ShipInputMapper
{
    public static PlayerInput Build(
        KeyboardState keys,
        KeyboardState prevKeys,
        MouseState mouse,
        MouseState prevMouse,
        MouseLookInput lookInput)
    {
        int scroll = mouse.ScrollWheelValue - prevMouse.ScrollWheelValue;
        int gearChangeSteps = scroll / 120;
        if (gearChangeSteps == 0 && scroll != 0)
            gearChangeSteps = scroll > 0 ? 1 : -1;

        return Build(
            keys,
            prevKeys,
            lookInput,
            gearChangeSequence: 0,
            gearChangeSteps: gearChangeSteps,
            xStopToggleSequence: 0);
    }

    public static PlayerInput Build(
        KeyboardState keys,
        KeyboardState prevKeys,
        MouseLookInput lookInput,
        long gearChangeSequence,
        int gearChangeSteps,
        long xStopToggleSequence)
    {
        // Thrust: keyboard axes, -1..1
        // W/S = fwd/back  A/D = strafe  R/F = up/down  Q/E = roll
        double fwd  = (keys.IsKeyDown(Keys.W) ? 1.0 : 0.0) - (keys.IsKeyDown(Keys.S) ? 1.0 : 0.0);
        double lat  = (keys.IsKeyDown(Keys.D) ? 1.0 : 0.0) - (keys.IsKeyDown(Keys.A) ? 1.0 : 0.0);
        double vert = (keys.IsKeyDown(Keys.R) ? 1.0 : 0.0) - (keys.IsKeyDown(Keys.F) ? 1.0 : 0.0);
        double roll = (keys.IsKeyDown(Keys.E) ? 1.0 : 0.0) - (keys.IsKeyDown(Keys.Q) ? 1.0 : 0.0);

        // V = Flight Assist toggle, G = Slipstream/mode toggle, X = X-Stop, Z = Afterburner.
        // All rising-edge sent to sim; sim owns the actual enabled/disabled state.
        bool faToggle          = keys.IsKeyDown(Keys.V) && !prevKeys.IsKeyDown(Keys.V);
        bool slipstreamToggle  = keys.IsKeyDown(Keys.G) && !prevKeys.IsKeyDown(Keys.G);
        bool xStopToggle       = keys.IsKeyDown(Keys.X) && !prevKeys.IsKeyDown(Keys.X);
        bool afterburnerToggle = keys.IsKeyDown(Keys.Z) && !prevKeys.IsKeyDown(Keys.Z);

        bool gearUp = gearChangeSteps > 0;
        bool gearDown = gearChangeSteps < 0;

        return new PlayerInput(fwd, lat, vert, roll, lookInput.PitchInput, lookInput.YawInput, false,
            FlightAssistToggle:  faToggle,
            SlipstreamToggle:    slipstreamToggle,
            XStopToggle:         xStopToggle,
            XStopToggleSequence: xStopToggle ? xStopToggleSequence : 0,
            GearUp:              gearUp,
            GearDown:            gearDown,
            AfterburnerToggle:   afterburnerToggle,
            GearChangeSequence:  gearChangeSteps != 0 ? gearChangeSequence : 0,
            GearChangeSteps:     gearChangeSteps);
    }
}
