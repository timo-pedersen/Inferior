using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.UI.Controls;

public sealed class TextBlock : Control
{
    public string Text { get; set; } = "";
    public bool Wrap { get; set; } = true;
    public int Padding { get; set; }

    public override Point DesiredSize
    {
        get
        {
            SpriteFont? font = Font;
            if (font is null)
                return new Point(Bounds.Width, Bounds.Height);
            float scale = FontScale ?? 1f;
            int width = Bounds.Width > 0 ? Bounds.Width : 240;
            string[] lines = BuildLines(font, scale, Math.Max(1, width - Padding * 2)).ToArray();
            int lineHeight = (int)MathF.Ceiling(font.MeasureString("A").Y * scale);
            return new Point(width, Padding * 2 + lineHeight * Math.Max(1, lines.Length));
        }
    }

    public override void Draw(SpriteBatch sb, UIRenderer renderer, Theme theme)
    {
        if (!Visible || string.IsNullOrEmpty(Text)) return;
        SpriteFont font = EffectiveFont(theme);
        float scale = EffectiveFontScale(theme);
        int lineHeight = (int)MathF.Ceiling(font.MeasureString("A").Y * scale);
        Rectangle area = AbsoluteBounds;
        IEnumerable<string> lines = BuildLines(font, scale, Math.Max(1, area.Width - Padding * 2));
        renderer.DrawWithClip(sb, EffectiveClipBoundsForSelf(), () =>
        {
            int y = area.Y + Padding;
            foreach (string line in lines)
            {
                if (y > area.Bottom)
                    break;
                renderer.DrawText(sb, line, new Vector2(area.X + Padding, y), font, scale, TextColor ?? theme.TextNormal);
                y += lineHeight;
            }
        });
    }

    private Rectangle EffectiveClipBoundsForSelf()
        => Rectangle.Intersect(AbsoluteBounds, EffectiveClipBounds);

    private IEnumerable<string> BuildLines(SpriteFont font, float scale, int maxWidth)
    {
        foreach (string paragraph in (Text ?? "").Replace("\r\n", "\n").Split('\n'))
        {
            if (!Wrap)
            {
                yield return paragraph;
                continue;
            }

            string current = "";
            foreach (string word in paragraph.Split(' ', StringSplitOptions.None))
            {
                string candidate = current.Length == 0 ? word : current + " " + word;
                if (FontHelper.Measure(font, candidate, scale).X <= maxWidth || current.Length == 0)
                {
                    current = candidate;
                }
                else
                {
                    yield return current;
                    current = word;
                }
            }
            yield return current;
        }
    }
}
