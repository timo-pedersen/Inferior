using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.UI;

/// <summary>
/// Safe wrappers around SpriteFont operations that strip unsupported characters
/// before measuring or drawing, preventing ArgumentException crashes at runtime.
/// Use these instead of font.MeasureString / sb.DrawString directly.
/// </summary>
public static class FontHelper
{
    public static string Sanitize(SpriteFont font, string text)
        => new string(text.Where(c => font.Characters.Contains(c)).ToArray());

    public static Vector2 Measure(SpriteFont font, string text, float scale = 1f)
        => font.MeasureString(Sanitize(font, text)) * scale;

    public static void Draw(SpriteBatch sb, SpriteFont font, string text,
        Vector2 pos, Color color, float scale = 1f)
    {
        var safe = Sanitize(font, text);
        if (safe.Length > 0)
            sb.DrawString(font, safe, pos, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
    }

    /// <summary>
    /// Rotated draw — used for sideways tab labels in EdgePanelHost.
    /// Origin is the centre of the sanitized text (for rotation pivot).
    /// </summary>
    public static void DrawRotated(SpriteBatch sb, SpriteFont font, string text,
        Vector2 centre, Color color, float rotation, float scale = 1f)
    {
        var safe = Sanitize(font, text);
        if (safe.Length == 0) return;
        var origin = font.MeasureString(safe) * 0.5f;
        sb.DrawString(font, safe, centre, color, rotation, origin, scale, SpriteEffects.None, 0f);
    }
}
