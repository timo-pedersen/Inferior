using Microsoft.Xna.Framework;

namespace Inferior.Game.StationGen;

internal static class StationWindowVisuals
{
    public static readonly Color WarmWhite = new(255, 250, 220);
    public static readonly Color NeutralWhite = new(240, 240, 248);
    public static readonly Color CoolBlue = new(210, 225, 255);
    public static readonly Color DimAmber = new(200, 170, 100);
    public static readonly Color DarkWarm = new(31, 30, 26);

    public static Color GlassTop(Color color) => Color.Lerp(color, Color.White, 0.18f);

    public static Color GlassBottom(Color color) => new(
        (byte)MathF.Min(color.R * 0.72f, 255f),
        (byte)MathF.Min(color.G * 0.72f, 255f),
        (byte)Math.Min((int)(color.B * 0.72f) + 8, 255),
        color.A);
}
