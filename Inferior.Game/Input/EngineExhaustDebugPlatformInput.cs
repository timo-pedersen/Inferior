using Microsoft.Xna.Framework.Input;

namespace Inferior.Game.Input;

internal static class EngineExhaustDebugPlatformInput
{
    public static bool IsCycleJustPressed(KeyboardState current, KeyboardState previous)
    {
        bool altDown = current.IsKeyDown(Keys.LeftAlt) || current.IsKeyDown(Keys.RightAlt);
        bool previousAltDown =
            previous.IsKeyDown(Keys.LeftAlt) || previous.IsKeyDown(Keys.RightAlt);
        bool controlDown =
            current.IsKeyDown(Keys.LeftControl) || current.IsKeyDown(Keys.RightControl);
        bool shiftDown =
            current.IsKeyDown(Keys.LeftShift) || current.IsKeyDown(Keys.RightShift);
        return altDown
            && !controlDown
            && !shiftDown
            && current.IsKeyDown(Keys.F2)
            && !(previousAltDown && previous.IsKeyDown(Keys.F2));
    }
}
