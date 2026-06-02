using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Text;

namespace Inferior.UI.Controls;

public enum LineBreakMode
{
    /// <summary>Truncate long lines at the panel edge with an ellipsis.</summary>
    Clip,
    /// <summary>Word-wrap long lines; continuation lines are indented to align with text.</summary>
    Wrap,
    /// <summary>Draw the full line with no truncation or wrapping — may overflow the panel.</summary>
    Bleed,
}

/// <summary>
/// Scrolling message log for system bus output.
/// Does not know about DataBus — the caller subscribes and calls AddMessage().
/// New messages appear at the bottom; oldest are discarded when MaxLines is reached.
/// </summary>
public sealed class SystemConsole : Control
{
    public string        Header    { get; set; } = "SYSTEM LOG";
    public int           MaxLines  { get; set; } = 6;
    public LineBreakMode LineBreak { get; set; } = LineBreakMode.Clip;

    private readonly List<string> _messages = [];

    /// <summary>Append a message. Oldest dropped when MaxLines is exceeded.</summary>
    public void AddMessage(string message)
    {
        _messages.Add(message);
        if (_messages.Count > MaxLines)
            _messages.RemoveAt(0);
    }

    public new void Clear() => _messages.Clear();

    public override void Draw(SpriteBatch sb, UIRenderer renderer, Theme theme)
    {
        if (!Visible) return;
        var ab = AbsoluteBounds;

        renderer.FillRect(sb, ab, BackColor ?? theme.PanelBackground);
        renderer.DrawRect(sb, ab, ForeColor ?? theme.PanelBorder, 1);

        const int   pad    = 7;
        const float hScale = 0.80f;
        const float mScale = 0.75f;
        const int   lineH  = 17;

        // Header
        renderer.DrawText(sb, Header,
            new Vector2(ab.X + pad, ab.Y + 5),
            theme.Font, hScale, theme.TextDisabled);

        // Divider
        int divY = ab.Y + 22;
        renderer.DrawLine(sb,
            new Vector2(ab.X + pad, divY),
            new Vector2(ab.Right - pad, divY),
            theme.PanelBorder);

        // Available width for text (inside padding, excluding "> " prefix)
        float prefixW  = theme.Font.MeasureString("> ").X * mScale;
        int   availW   = ab.Width - pad * 2 - (int)prefixW;
        var   msgColor = TextColor ?? new Color(160, 175, 195);

        int y      = divY + 4;
        int bottom = ab.Bottom - pad;

        foreach (var msg in _messages)
        {
            bool first = true;
            foreach (var line in VisualLines(msg, theme.Font, mScale, availW))
            {
                if (y + lineH > bottom) goto done;
                string prefix = first ? "> " : "  ";
                renderer.DrawText(sb, prefix + line,
                    new Vector2(ab.X + pad, y),
                    theme.Font, mScale, msgColor);
                y += lineH;
                first = false;
            }
        }
        done:;
    }

    // ── Line-break helpers ────────────────────────────────────────────────────

    private IEnumerable<string> VisualLines(string text, SpriteFont font, float scale, int maxWidth)
        => LineBreak switch
        {
            LineBreakMode.Wrap  => WordWrap(text, font, scale, maxWidth),
            LineBreakMode.Clip  => [Truncate(text, font, scale, maxWidth)],
            _                   => [text],   // Bleed
        };

    private static string Truncate(string text, SpriteFont font, float scale, int maxWidth)
    {
        if (font.MeasureString(text).X * scale <= maxWidth) return text;
        // Walk back until it fits, then append ellipsis
        for (int len = text.Length - 1; len > 0; len--)
            if (font.MeasureString(text[..len]).X * scale <= maxWidth)
                return text[..len] + "…";
        return "";
    }

    private static IEnumerable<string> WordWrap(string text, SpriteFont font, float scale, int maxWidth)
    {
        var words = text.Split(' ');
        var line  = new StringBuilder();

        foreach (var word in words)
        {
            string candidate = line.Length == 0 ? word : line + " " + word;

            if (font.MeasureString(candidate).X * scale > maxWidth && line.Length > 0)
            {
                yield return line.ToString();
                line.Clear();
            }

            if (line.Length > 0) line.Append(' ');
            line.Append(word);
        }

        if (line.Length > 0)
            yield return line.ToString();
    }
}
