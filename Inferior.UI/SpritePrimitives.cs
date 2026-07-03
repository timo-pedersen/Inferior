using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.UI;

/// <summary>
/// Shared 2D drawing helpers used by multiple game states / cockpit UI. Callers
/// supply their own font and 1x1 white pixel texture — this class owns neither.
/// </summary>
public static class SpritePrimitives
{
    public static void DrawText(SpriteBatch sb, SpriteFont font, string text, Vector2 pos, Color color, float scale = 1.0f)
        => FontHelper.Draw(sb, font, text, pos, color, scale);

    public static void DrawRect(SpriteBatch sb, Texture2D pixel, Rectangle rect, Color color)
        => sb.Draw(pixel, rect, color);

    public static void DrawRectBorder(SpriteBatch sb, Texture2D pixel, Rectangle rect, Color color, int thickness = 1)
    {
        sb.Draw(pixel, new Rectangle(rect.Left,  rect.Top,              rect.Width, thickness), color);
        sb.Draw(pixel, new Rectangle(rect.Left,  rect.Bottom-thickness, rect.Width, thickness), color);
        sb.Draw(pixel, new Rectangle(rect.Left,  rect.Top,  thickness,  rect.Height),           color);
        sb.Draw(pixel, new Rectangle(rect.Right-thickness, rect.Top, thickness, rect.Height),   color);
    }
}
