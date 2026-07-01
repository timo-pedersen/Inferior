using Inferior.Core.DataBus;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.UI.Controls.Cockpit;

/// <summary>
/// Horizontal bar meter for a single instrument value.
/// Set Topic to auto-subscribe to DataBus.Instruments — no manual wiring needed.
/// SetValue() is still available for one-off updates or custom handlers.
///
/// Layout (example 200×46):
///   ┌──────────────────────────────────────────┐
///   │ HEARTBEAT                                │
///   │ ██████████████░░░░░░░░░░░░  62.3        │
///   └──────────────────────────────────────────┘
/// </summary>
public sealed class InstrumentMeter : Control
{
    public string Label       { get; set; } = "";
    public double MinValue    { get; set; } = 0.0;
    public double MaxValue    { get; set; } = 100.0;
    public string Format      { get; set; } = "F1";   // value display format
    /// <summary>
    /// Multiplied against the raw bus value before display and bar scaling.
    /// Use to convert bus units to display units (e.g. 1e-6 for watts → MW).
    /// Defaults to 1.0 (no conversion).
    /// </summary>
    public double ScaleFactor    { get; set; } = 1.0;
    /// <summary>
    /// Speed of exponential smoothing toward the target value.
    /// Higher = faster response. Default 8.0 settles within ~0.5 s.
    /// </summary>
    public double AnimationSpeed { get; set; } = 8.0;

    // ── DataBus auto-subscribe ────────────────────────────────────────────────

    private string          _topic         = "";
    private Action<double>? _topicHandler;

    /// <summary>
    /// When set, the meter subscribes to DataBus.Instruments for this topic and
    /// updates automatically each frame. Changing Topic unsubscribes the old one.
    /// Leave empty to drive the meter manually via SetValue().
    /// </summary>
    public string Topic
    {
        get => _topic;
        set
        {
            if (_topic == value) return;
            if (_topicHandler != null && _topic.Length > 0)
                DataBus.Instruments.Unsubscribe(_topic, _topicHandler);
            _topic = value;
            if (_topic.Length > 0)
            {
                _topicHandler = SetValue;
                DataBus.Instruments.Subscribe(_topic, _topicHandler);
            }
            else
            {
                _topicHandler = null;
            }
        }
    }

    private double _rawValue;
    private double _displayedValue = double.NaN;  // NaN = not yet received; snaps on first value

    /// <summary>Update the raw bus value. ScaleFactor is applied at draw time.</summary>
    public void SetValue(double value) => _rawValue = value;

    public override void Update(double dt)
    {
        double target = _rawValue * ScaleFactor;
        if (double.IsNaN(_displayedValue))
            _displayedValue = target;
        else
            _displayedValue += (target - _displayedValue) * System.Math.Min(AnimationSpeed * dt, 1.0);
        base.Update(dt);
    }

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
        int valueW    = 44;
        int barTrackW = ab.Width - pad * 2 - valueW - 4;

        double displayValue = double.IsNaN(_displayedValue) ? 0.0 : _displayedValue;
        double frac = MaxValue > MinValue
            ? System.Math.Clamp((displayValue - MinValue) / (MaxValue - MinValue), 0.0, 1.0)
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
        string valStr = displayValue.ToString(Format);
        var valSize   = renderer.MeasureText(valStr, theme.Font, theme.SmallScale);
        renderer.DrawText(sb, valStr,
            new Vector2(ab.Right - pad - valSize.X, barY + (barH - valSize.Y) * 0.5f),
            theme.Font, theme.SmallScale,
            TextColor ?? theme.TextNormal);
    }
}
