using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.Game.Platform;

/// <summary>
/// Host-system (OS) concerns that don't belong in game/simulation code — screenshots for
/// now; file dialogs and other IO helpers may join later. Keep all OS-specific code here,
/// nowhere else.
/// </summary>
internal static class HostServices
{
    private const string ScreenshotDirectoryName = "Screenshots";

    /// <summary>
    /// Captures the current backbuffer and saves it as a timestamped PNG in Screenshots/
    /// next to the executable (created if missing). The backbuffer read
    /// (GraphicsDevice.GetBackBufferData) happens synchronously — it must run on the
    /// render path, so call this at the end of Draw(), never from a background task. The
    /// PNG encode and file write are handed off to a background task.
    /// </summary>
    public static void SaveScreenshot(GraphicsDevice gd)
    {
        int width  = gd.PresentationParameters.BackBufferWidth;
        int height = gd.PresentationParameters.BackBufferHeight;

        var pixels = new Color[width * height];
        gd.GetBackBufferData(pixels);

        var texture = new Texture2D(gd, width, height);
        texture.SetData(pixels);

        string directory = Path.Combine(AppContext.BaseDirectory, ScreenshotDirectoryName);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, $"{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");

        Task.Run(() =>
        {
            try
            {
                using var stream = File.Create(path);
                texture.SaveAsPng(stream, width, height);
                Console.WriteLine($"[Screenshot] Saved {path}");
            }
            finally
            {
                texture.Dispose();
            }
        });
    }
}
