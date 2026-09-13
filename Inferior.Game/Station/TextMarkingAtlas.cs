using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.Game.StationGen;

/// <summary>
/// Builds a shared RGBA texture from bitmap-font text for use as a projected-surface-marking
/// atlas (see Inferior.Rendering/SurfaceMarkingProjector.cs, SurfaceMarkingGeometry.cs). RGB is
/// glyph colour (white — markings apply their own tint via SurfaceMarkingProjector.Tint/
/// MeshRenderer.DrawDecalLit's materialColor), alpha is glyph coverage: 0 where nothing was
/// drawn, so a marking's transparent regions reveal the receiver surface normally.
///
/// One texture is generated and cached per distinct (text, pixelScale) pair — every marking
/// using the same text/scale shares the same Texture2D instance; no marking or receiving
/// object ever gets its own copy. Simplification appropriate to this first pass: the cache is
/// process-lifetime with no disposal path, matching a handful of small session-scoped shared
/// textures elsewhere (e.g. MeshRenderer's own fallback textures) — fine for one proof marking,
/// revisit if/when a real shared multi-marking atlas (packed regions, many distinct texts)
/// replaces this "whole texture is the one marking" approach.
/// </summary>
public static class TextMarkingAtlas
{
    private static readonly Dictionary<(string Text, int PixelScale), Texture2D> Cache = [];

    public static Texture2D GetOrCreate(GraphicsDevice graphicsDevice, string text, int pixelScale = 4)
    {
        ArgumentNullException.ThrowIfNull(graphicsDevice);
        ArgumentException.ThrowIfNullOrEmpty(text);
        if (pixelScale <= 0)
            throw new ArgumentOutOfRangeException(nameof(pixelScale));

        var key = (text, pixelScale);
        if (Cache.TryGetValue(key, out Texture2D? existing) && !existing.IsDisposed)
            return existing;

        int width = TextPainter.MeasureText(text, pixelScale);
        int height = TextPainter.MeasureHeight(pixelScale);
        if (width <= 0 || height <= 0)
            throw new ArgumentException("Marking text measures to an empty bitmap.", nameof(text));

        var pixels = new Color[width * height];
        Array.Fill(pixels, Color.Transparent);
        // alpha: 1.0f selects TextPainter's hard-overwrite branch (pixels[idx] = colour)
        // rather than a lerp against whatever was already there — exactly "opaque white where
        // lit, left untouched (still fully transparent) elsewhere".
        TextPainter.DrawText(pixels, width, height, text, x: 0, y: 0, Color.White, pixelScale, alpha: 1.0f);

        var texture = new Texture2D(graphicsDevice, width, height, mipmap: false, SurfaceFormat.Color);
        texture.SetData(pixels);
        Cache[key] = texture;
        return texture;
    }
}
