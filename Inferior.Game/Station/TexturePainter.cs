using Microsoft.Xna.Framework;

namespace Inferior.Game.StationGen;

/// <summary>
/// CPU-side pixel painter for drawing text into a Color[] buffer using BitmapFonts.
/// All coordinates are in pixel space; the caller manages buffer layout (width × height).
/// </summary>
public static class TexturePainter
{
    private const int Spacing = 1;   // pixels between characters

    /// Draws text into pixels[] starting at (x, y), scaled by pixelScale.
    /// Characters not in BitmapFonts are silently skipped.
    public static void DrawText(
        Color[] pixels,
        int     bufferWidth,
        int     bufferHeight,
        string  text,
        int     x,
        int     y,
        Color   colour,
        int     pixelScale = 2,
        float   alpha      = 1.0f)
    {
        int cx = x;
        foreach (char ch in text)
        {
            if (!BitmapFonts.HasGlyph(ch))
            {
                cx += (BitmapFonts.CharW + Spacing) * pixelScale;
                continue;
            }

            for (int row = 0; row < BitmapFonts.CharH; row++)
            for (int col = 0; col < BitmapFonts.CharW; col++)
            {
                if (!BitmapFonts.IsLit(ch, col, row)) continue;

                for (int sy = 0; sy < pixelScale; sy++)
                for (int sx = 0; sx < pixelScale; sx++)
                {
                    int px = cx + col * pixelScale + sx;
                    int py = (y + BitmapFonts.CharH * pixelScale) - (row * pixelScale + sy);
                    if ((uint)px < (uint)bufferWidth && (uint)py < (uint)bufferHeight)
                    {
                        int idx = py * bufferWidth + px;
                        pixels[idx] = alpha >= 0.999f
                            ? colour
                            : TexturePalette.LerpColor(pixels[idx], colour, alpha);
                    }
                }
            }

            cx += (BitmapFonts.CharW + Spacing) * pixelScale;
        }
    }

    /// Returns the pixel width of text at the given scale.
    public static int MeasureText(string text, int pixelScale = 2)
    {
        if (text.Length == 0) return 0;
        return text.Length * (BitmapFonts.CharW + Spacing) * pixelScale - Spacing * pixelScale;
    }

    /// Returns the pixel height at the given scale.
    public static int MeasureHeight(int pixelScale = 2) => BitmapFonts.CharH * pixelScale;
}
