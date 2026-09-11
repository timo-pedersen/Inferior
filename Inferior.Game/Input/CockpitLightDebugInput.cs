using Microsoft.Xna.Framework.Input;

namespace Inferior.Game.Input;

internal enum CockpitLightDebugAction
{
    None,
    ToggleCanopy,
    ToggleInternal
}

internal static class CockpitLightDebugInput
{
    public static CockpitLightDebugAction Read(
        KeyboardState current,
        KeyboardState previous)
    {
        bool f1JustPressed =
            current.IsKeyDown(Keys.F1) && !previous.IsKeyDown(Keys.F1);
        if (!f1JustPressed)
            return CockpitLightDebugAction.None;

        bool shiftDown =
            current.IsKeyDown(Keys.LeftShift) || current.IsKeyDown(Keys.RightShift);
        bool ctrlDown =
            current.IsKeyDown(Keys.LeftControl) || current.IsKeyDown(Keys.RightControl);
        if (!shiftDown && !ctrlDown)
            return CockpitLightDebugAction.None;

        return shiftDown
            ? CockpitLightDebugAction.ToggleInternal
            : CockpitLightDebugAction.ToggleCanopy;
    }
}
