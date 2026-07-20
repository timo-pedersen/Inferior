using Inferior.Gameplay;
using Inferior.Gameplay.Ship;
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
        // Thrust: keyboard axes, -1..1
        // W/S = fwd/back  A/D = strafe  R/F = up/down  Q/E = yaw
        double fwd  = (keys.IsKeyDown(Keys.W) ? 1.0 : 0.0) - (keys.IsKeyDown(Keys.S) ? 1.0 : 0.0);
        double lat  = (keys.IsKeyDown(Keys.D) ? 1.0 : 0.0) - (keys.IsKeyDown(Keys.A) ? 1.0 : 0.0);
        double vert = (keys.IsKeyDown(Keys.R) || keys.IsKeyDown(Keys.Space) ? 1.0 : 0.0)
                    - (keys.IsKeyDown(Keys.F) ? 1.0 : 0.0);
        double keyboardYaw = (keys.IsKeyDown(Keys.Q) ? 1.0 : 0.0)
                           - (keys.IsKeyDown(Keys.E) ? 1.0 : 0.0);

        // V = Flight Assist toggle, G = Slipstream/mode toggle, X = X-Stop, Z = Afterburner.
        // All rising-edge sent to sim; sim owns the actual enabled/disabled state.
        bool faToggle          = keys.IsKeyDown(Keys.V) && !prevKeys.IsKeyDown(Keys.V);
        bool slipstreamToggle  = keys.IsKeyDown(Keys.G) && !prevKeys.IsKeyDown(Keys.G);
        bool xStopToggle       = keys.IsKeyDown(Keys.X) && !prevKeys.IsKeyDown(Keys.X);
        bool afterburnerToggle = keys.IsKeyDown(Keys.Z) && !prevKeys.IsKeyDown(Keys.Z);

        // Scroll wheel -> one gear shift per tick (forwarded to sim; debug cam handles its own scroll)
        int  scroll   = mouse.ScrollWheelValue - prevMouse.ScrollWheelValue;
        bool gearUp   = scroll > 0;
        bool gearDown = scroll < 0;

        double pitchMaximum = lookInput.PitchInput >= 0.0
            ? FlightConstants.MaximumAssistedPitchUpRateRadPerSec
            : FlightConstants.MaximumAssistedPitchDownRateRadPerSec;
        double mousePitch = ShipRotation.NormalizeLegacyMouseInput(
            lookInput.PitchInput,
            pitchMaximum);
        double mouseRoll = ShipRotation.NormalizeLegacyMouseInput(
            -lookInput.HorizontalInput,
            FlightConstants.MaximumAssistedRollRateRadPerSec);
        RotationCommand rotation = RotationCommand.Clamp(mousePitch, keyboardYaw, mouseRoll);

        return new PlayerInput(fwd, lat, vert, rotation.Roll, rotation.Pitch, rotation.Yaw, false,
            FlightAssistToggle: faToggle,
            SlipstreamToggle:   slipstreamToggle,
            XStopToggle:        xStopToggle,
            GearUp:             gearUp,
            GearDown:           gearDown,
            AfterburnerToggle:  afterburnerToggle);
    }
}
