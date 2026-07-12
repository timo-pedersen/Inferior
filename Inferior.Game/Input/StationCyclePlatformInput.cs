using System.Runtime.InteropServices;

namespace Inferior.Game.Input;

internal static class StationCyclePlatformInput
{
    private const int VkF12 = 0x7B;
    private const int VkLeftControl = 0xA2;
    private const int VkRightControl = 0xA3;
    private const short KeyDownMask = unchecked((short)0x8000);

    public static bool IsCtrlF12Down()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return false;

        bool ctrlDown = IsDown(VkLeftControl) || IsDown(VkRightControl);
        return ctrlDown && IsDown(VkF12);
    }

    private static bool IsDown(int virtualKey)
        => (GetAsyncKeyState(virtualKey) & KeyDownMask) != 0;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
