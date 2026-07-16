using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
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
    /// Captures the current backbuffer, saves it as a timestamped PNG in Screenshots/ next
    /// to the executable (created if missing), and places it on the Windows clipboard as a
    /// pasteable image. The backbuffer read (GraphicsDevice.GetBackBufferData) happens
    /// synchronously — it must run on the render path, so call this at the end of Draw(),
    /// never from a background task. The PNG encode, file write, and clipboard write are
    /// all handed off to one background task.
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

            // Clipboard is a best-effort convenience on top of the file write above, not a
            // dependency of it — TrySetClipboardImage swallows its own failures.
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                TrySetClipboardImage(pixels, width, height);
        });
    }

    // ── Windows clipboard (CF_DIB) ───────────────────────────────────────────────

    [SupportedOSPlatform("windows")]
    private static void TrySetClipboardImage(Color[] pixelsTopDownRgba, int width, int height)
    {
        try
        {
            byte[] dibPixels = BuildBgraBottomUp(pixelsTopDownRgba, width, height);

            var header = new BITMAPINFOHEADER
            {
                biSize          = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth         = width,
                biHeight        = height,   // positive => bottom-up, per CF_DIB convention
                biPlanes        = 1,
                biBitCount      = 32,
                biCompression   = BI_RGB,
                biSizeImage     = (uint)dibPixels.Length,
                biXPelsPerMeter = 0,
                biYPelsPerMeter = 0,
                biClrUsed       = 0,
                biClrImportant  = 0,
            };

            int headerSize = Marshal.SizeOf<BITMAPINFOHEADER>();
            int totalSize  = headerSize + dibPixels.Length;

            IntPtr hGlobal = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)totalSize);
            if (hGlobal == IntPtr.Zero) return;

            IntPtr dest = GlobalLock(hGlobal);
            if (dest == IntPtr.Zero)
            {
                GlobalFree(hGlobal);
                return;
            }
            try
            {
                Marshal.StructureToPtr(header, dest, false);
                Marshal.Copy(dibPixels, 0, dest + headerSize, dibPixels.Length);
            }
            finally
            {
                GlobalUnlock(hGlobal);
            }

            if (!TryOpenClipboardWithRetry())
            {
                GlobalFree(hGlobal);
                return;
            }

            try
            {
                EmptyClipboard();
                IntPtr result = SetClipboardData(CF_DIB, hGlobal);
                // SetClipboardData transfers ownership of hGlobal to the OS on success —
                // freeing it here would corrupt whatever the clipboard now points at. Only
                // free on failure, when the OS never took ownership.
                if (result == IntPtr.Zero)
                    GlobalFree(hGlobal);
            }
            finally
            {
                CloseClipboard();
            }
        }
        catch
        {
            // Best-effort — never let a clipboard failure surface past the screenshot save.
        }
    }

    // Backbuffer pixels are RGBA, top-down. CF_DIB with a positive biHeight wants BGRA,
    // bottom-up — swap channels and flip rows.
    private static byte[] BuildBgraBottomUp(Color[] pixelsTopDownRgba, int width, int height)
    {
        int rowBytes = width * 4;
        var dib = new byte[rowBytes * height];

        for (int destRow = 0; destRow < height; destRow++)
        {
            int srcRow = height - 1 - destRow;   // dest row 0 (bottom) = source's last row
            int srcRowStart  = srcRow * width;
            int destRowStart = destRow * rowBytes;

            for (int x = 0; x < width; x++)
            {
                Color c = pixelsTopDownRgba[srcRowStart + x];
                int di = destRowStart + x * 4;
                dib[di + 0] = c.B;
                dib[di + 1] = c.G;
                dib[di + 2] = c.R;
                dib[di + 3] = c.A;
            }
        }

        return dib;
    }

    [SupportedOSPlatform("windows")]
    private static bool TryOpenClipboardWithRetry()
    {
        // Another process (clipboard viewer, etc.) can transiently hold the clipboard —
        // retry a few times over ~50ms rather than failing outright.
        for (int attempt = 0; attempt < 5; attempt++)
        {
            if (OpenClipboard(IntPtr.Zero)) return true;
            Thread.Sleep(10);
        }
        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint   biSize;
        public int    biWidth;
        public int    biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint   biCompression;
        public uint   biSizeImage;
        public int    biXPelsPerMeter;
        public int    biYPelsPerMeter;
        public uint   biClrUsed;
        public uint   biClrImportant;
    }

    private const uint CF_DIB        = 8;
    private const uint GMEM_MOVEABLE = 0x0002;
    private const uint BI_RGB        = 0;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr hMem);
}
