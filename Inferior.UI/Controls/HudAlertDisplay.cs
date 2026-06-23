using Inferior.Core.DataBus;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.UI.Controls;

/// <summary>
/// Centre-screen HUD overlay for Warning-and-above system messages.
/// Caller subscribes to the System bus and calls AddMessage(); this control
/// manages display timing and draws a single line of coloured text.
///
/// Display rules:
///   Warning         — 4 s then auto-dismissed
///   ImportantWarning — 6 s then auto-dismissed
///   Critical        — persists until HandleInput() sees any keypress
///
/// If multiple messages arrive, the highest-priority one is always shown.
/// Within equal priority, the most recent is shown.
/// </summary>
public sealed class HudAlertDisplay
{
    private readonly record struct PendingAlert(SystemMessage Message, double RemainingSeconds);

    private readonly List<PendingAlert> _queue = [];

    public void AddMessage(SystemMessage msg)
    {
        if (msg.Priority < SystemMessagePriority.Warning) return;

        double duration = msg.Priority switch
        {
            SystemMessagePriority.Warning          => 4.0,
            SystemMessagePriority.ImportantWarning => 6.0,
            _                                      => double.PositiveInfinity,  // Critical
        };
        _queue.Add(new PendingAlert(msg, duration));
    }

    /// <summary>True when a Critical alert is waiting for acknowledgement.</summary>
    public bool HasCritical => _queue.Exists(a => a.Message.Priority == SystemMessagePriority.Critical);

    /// <summary>Tick timers and drop expired non-Critical alerts.</summary>
    public void Update(double dt)
    {
        for (int i = _queue.Count - 1; i >= 0; i--)
        {
            var a = _queue[i];
            if (double.IsPositiveInfinity(a.RemainingSeconds)) continue;
            double remaining = a.RemainingSeconds - dt;
            if (remaining <= 0)
                _queue.RemoveAt(i);
            else
                _queue[i] = a with { RemainingSeconds = remaining };
        }
    }

    /// <summary>Any keypress or click dismisses the active Critical alert.</summary>
    public void HandleInput(InputState input)
    {
        if (!HasCritical) return;
        if (input.AnyKeyJustPressed || input.LeftPressed || input.RightPressed)
            DismissCritical();
    }

    private void DismissCritical()
    {
        for (int i = _queue.Count - 1; i >= 0; i--)
            if (_queue[i].Message.Priority == SystemMessagePriority.Critical)
                _queue.RemoveAt(i);
    }

    public void Draw(SpriteBatch sb, UIRenderer renderer, SpriteFont font, int viewportWidth, int viewportHeight)
    {
        if (_queue.Count == 0) return;

        var active = SelectActive();
        if (active is null) return;

        var (color, text) = DisplayFor(active.Value.Message);

        const float scale = 0.85f;
        var size = FontHelper.Measure(font, text, scale);
        var pos  = new Vector2(
            (viewportWidth  - size.X) * 0.5f,
            viewportHeight  * 0.55f);

        // Subtle dark backing so the text is legible against any 3D scene colour
        var bg = new Rectangle((int)(pos.X - 8), (int)(pos.Y - 4), (int)(size.X + 16), (int)(size.Y + 8));
        renderer.FillRect(sb, bg, new Color(0, 0, 0, 160));

        renderer.DrawText(sb, text, pos, font, scale, color);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private PendingAlert? SelectActive()
    {
        if (_queue.Count == 0) return null;
        // Highest priority wins; within equal priority, most recently added (last in list)
        PendingAlert best = _queue[0];
        for (int i = 1; i < _queue.Count; i++)
            if (_queue[i].Message.Priority >= best.Message.Priority)
                best = _queue[i];
        return best;
    }

    private static (Color color, string text) DisplayFor(SystemMessage msg)
    {
        var (color, prefix) = msg.Priority switch
        {
            SystemMessagePriority.Warning          => (new Color(220, 180,  40), "! "),
            SystemMessagePriority.ImportantWarning => (new Color(230, 120,  30), "!! "),
            SystemMessagePriority.Critical         => (new Color(220,  60,  60), "! "),
            _                                      => (new Color(160, 175, 195), ""),
        };
        return (color, prefix + msg.Text);
    }
}
