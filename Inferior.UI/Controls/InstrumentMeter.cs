using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.UI.Controls;

/// <summary>
/// Horizontal bar meter for a single instrument value.
/// Does not know about DataBus — the caller subscribes and calls SetValue().
///
/// Layout (example 200×46):
///   ┌──────────────────────────────────────────┐
///   │ HEARTBEAT                                │
///   │ ██████████████░░░░░░░░░░░░  62.3        │
///   └──────────────────────────────────────────┘
/// </summary>
public sealed class InstrumentMeter : Control
{
    public string Label    { get; set; } = "";
    public double MinValue { get; set; } = 0.0;
    public double MaxValue { get; set; } = 100.0;
    public string Format   { get; set; } = "F1";   // value display format

    private double _value;

    /// <summary>Thread-safe enough for display — called from DataBus handler on main thread.</summary>
    public void SetValue(double value) => _value = value;

    public override void Draw(SpriteBatch sb, UIRenderer renderer, Theme theme)
    {
        if (!Visible) return;
        var ab = AbsoluteBounds;

        // Background + border
        renderer.FillRect(sb, ab, BackColor ?? theme.PanelBackground);
        renderer.DrawRect(sb, ab, ForeColor ?? theme.PanelBorder, 1);

        const int pad = 7;
        const int headerH = 16;

        // Header label
        renderer.DrawText(sb, Label,
            new Vector2(ab.X + pad, ab.Y + 5),
            theme.Font, theme.SmallScale * 0.88f,
            theme.TextDisabled);

        // Bar track area
        int barY      = ab.Y + pad + headerH;
        int barH      = ab.Height - barY + ab.Y - pad;
        int valueW    = 44;                               // reserved for value text
        int barTrackW = ab.Width - pad * 2 - valueW - 4;

        double frac = MaxValue > MinValue
            ? System.Math.Clamp((_value - MinValue) / (MaxValue - MinValue), 0.0, 1.0)
            : 0.0;
        int fillW = (int)(barTrackW * frac);

        // Track (empty)
        renderer.FillRect(sb, new Rectangle(ab.X + pad, barY, barTrackW, barH),
            new Color(22, 30, 50));

        // Filled portion
        if (fillW > 0)
            renderer.FillRect(sb, new Rectangle(ab.X + pad, barY, fillW, barH),
                theme.Accent);

        // Value text — right-aligned in reserved area
        string valStr = _value.ToString(Format);
        var valSize   = renderer.MeasureText(valStr, theme.Font, theme.SmallScale);
        renderer.DrawText(sb, valStr,
            new Vector2(ab.Right - pad - valSize.X, barY + (barH - valSize.Y) * 0.5f),
            theme.Font, theme.SmallScale,
            TextColor ?? theme.TextNormal);
    }
}
