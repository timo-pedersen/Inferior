using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.UI.Controls;

/// <summary>
/// Static text label. Not interactive — TabIndex stays 0.
/// Supports horizontal alignment.
/// </summary>
public sealed class Label : Control
{
    public string Text      { get; set; } = "";
    public HAlign Alignment { get; set; } = HAlign.Left;

    public Label() { }

    public Label(string text, Rectangle bounds)
    {
        Text   = text;
        Bounds = bounds;
    }

    public override void Draw(SpriteBatch sb, UIRenderer renderer, Theme theme)
    {
        if (!Visible || string.IsNullOrEmpty(Text)) return;

        var   font  = EffectiveFont(theme);
        float scale = EffectiveFontScale(theme);
        Color color = TextColor ?? theme.TextNormal;

        Vector2 pos = Alignment switch
        {
            HAlign.Centre => CentreText(Text, font, scale, AbsoluteBounds),
            HAlign.Right  => RightAlignText(Text, font, scale, AbsoluteBounds, theme.Padding),
            _             => LeftAlignText(Text, font, scale, AbsoluteBounds, 0),
        };

        renderer.DrawText(sb, Text, pos, font, scale, color);
    }
}
