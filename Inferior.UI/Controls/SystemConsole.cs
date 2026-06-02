using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.UI.Controls;

/// <summary>
/// Scrolling message log for system bus output.
/// Does not know about DataBus — the caller subscribes and calls AddMessage().
/// New messages appear at the bottom; oldest are discarded when MaxLines is reached.
///
/// Layout (example 200×130):
///   ┌──────────────────────────────────────────┐
///   │ SYSTEM LOG                               │
///   │ > Entered Tau Ceti                       │
///   │ > T+8s nominal                           │
///   │ > T+16s nominal                          │
///   │ > Heartbeat threshold exceeded           │
///   └──────────────────────────────────────────┘
/// </summary>
public sealed class SystemConsole : Control
{
    public string Header   { get; set; } = "SYSTEM LOG";
    public int    MaxLines { get; set; } = 6;

    private readonly List<string> _messages = [];

    /// <summary>Append a message. Oldest message dropped when MaxLines is exceeded.</summary>
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

        // Background + border
        renderer.FillRect(sb, ab, BackColor ?? theme.PanelBackground);
        renderer.DrawRect(sb, ab, ForeColor ?? theme.PanelBorder, 1);

        const int    pad    = 7;
        const float  hScale = 0.80f;  // header scale
        const float  mScale = 0.75f;  // message scale
        const int    lineH  = 17;

        // Header
        renderer.DrawText(sb, Header,
            new Vector2(ab.X + pad, ab.Y + 5),
            theme.Font, hScale,
            theme.TextDisabled);

        // Divider line below header
        int divY = ab.Y + 22;
        renderer.DrawLine(sb,
            new Vector2(ab.X + pad, divY),
            new Vector2(ab.Right - pad, divY),
            theme.PanelBorder);

        // Messages — most recent at bottom, draw from top of message area
        int msgAreaY = divY + 4;
        for (int i = 0; i < _messages.Count; i++)
        {
            renderer.DrawText(sb,
                "> " + _messages[i],
                new Vector2(ab.X + pad, msgAreaY + i * lineH),
                theme.Font, mScale,
                TextColor ?? new Color(160, 175, 195));
        }
    }
}
