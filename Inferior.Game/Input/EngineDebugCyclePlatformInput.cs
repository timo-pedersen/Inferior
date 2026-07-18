using Microsoft.Xna.Framework.Input;

namespace Inferior.Game.Input;

internal static class EngineDebugCyclePlatformInput
{
    public static bool IsCycleJustPressed(KeyboardState current, KeyboardState previous)
    {
        bool controlDown =
            current.IsKeyDown(Keys.LeftControl) || current.IsKeyDown(Keys.RightControl);
        bool previousControlDown =
            previous.IsKeyDown(Keys.LeftControl) || previous.IsKeyDown(Keys.RightControl);
        return controlDown
            && current.IsKeyDown(Keys.F2)
            && !(previousControlDown && previous.IsKeyDown(Keys.F2));
    }
}
