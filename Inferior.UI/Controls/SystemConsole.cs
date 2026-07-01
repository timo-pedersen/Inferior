using Inferior.Core.DataBus;
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

    private readonly List<SystemMessage> _messages = [];

    /// <summary>Append a message. Oldest dropped when MaxLines is exceeded.</summary>
    public void AddMessage(SystemMessage message)
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

        // Available width for text (inside padding, excluding widest prefix "!! ")
        float prefixW  = theme.Font.MeasureString("!! ").X * mScale;
        int   availW   = ab.Width - pad * 2 - (int)prefixW;

        int y      = divY + 4;
        int bottom = ab.Bottom - pad;

        foreach (var msg in _messages)
        {
            var (msgColor, prefix) = PriorityStyle(msg.Priority, TextColor);
            bool first = true;
            foreach (var line in VisualLines(msg.Text, theme.Font, mScale, availW))
            {
                if (y + lineH > bottom) goto done;
                string linePrefix = first ? prefix : "  ";
                renderer.DrawText(sb, linePrefix + line,
                    new Vector2(ab.X + pad, y),
                    theme.Font, mScale, msgColor);
                y += lineH;
                first = false;
            }
        }
        done:;
    }

    // ── Priority helpers ──────────────────────────────────────────────────────

    private static (Color color, string prefix) PriorityStyle(SystemMessagePriority p, Color? overrideColor)
        => p switch
        {
            SystemMessagePriority.NB              => (new Color(100, 200, 220), ". "),
            SystemMessagePriority.Warning         => (new Color(220, 180,  40), "! "),
            SystemMessagePriority.ImportantWarning => (new Color(230, 120,  30), "!! "),
            SystemMessagePriority.Critical        => (new Color(220,  60,  60), "!! "),
            _                                     => (overrideColor ?? new Color(160, 175, 195), "> "),
        };

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
        if (FontHelper.Measure(font, text, scale).X <= maxWidth) return text;
        // Walk back until it fits, then append ellipsis
        for (int len = text.Length - 1; len > 0; len--)
            if (FontHelper.Measure(font, text[..len], scale).X <= maxWidth)
                return text[..len] + "...";
        return "";
    }

    private static IEnumerable<string> WordWrap(string text, SpriteFont font, float scale, int maxWidth)
    {
        var words = text.Split(' ');
        var line  = new StringBuilder();

        foreach (var word in words)
        {
            string candidate = line.Length == 0 ? word : line + " " + word;

            if (FontHelper.Measure(font, candidate, scale).X > maxWidth && line.Length > 0)
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
