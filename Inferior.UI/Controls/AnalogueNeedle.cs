using Inferior.Core.DataBus;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.UI.Controls;

/// <summary>
/// Analogue needle gauge with 180° sweep.
/// Left edge = MinValue, right edge = MaxValue, pivot at bottom-centre of the face.
///
/// Layout (example 244×130):
///   ┌──────────────────────────────────────────┐
///   │ SHIELD CONN                              │
///   │      |    |    |    |    |               │
///   │   |           ↑needle        |           │
///   │  |            |               |          │
///   │               ●                          │
///   │            0.125 MW                      │
///   └──────────────────────────────────────────┘
///
/// Tick marks: 20 divisions (9° each). Major marks every 5th (0 %, 25 %, 50 %, 75 %, 100 %).
///
/// Needle and value text both animate with exponential smoothing toward the live value.
/// Set Topic to auto-subscribe to DataBus.ScalarTelemetry — no manual wiring needed.
/// </summary>
public sealed class AnalogueNeedle : Control
{
    public string Label          { get; set; } = "";
    public double MinValue       { get; set; } = 0.0;
    public double MaxValue       { get; set; } = 1.0;
    public string Format         { get; set; } = "F1";
    /// <summary>Multiplied against the raw bus value before display. Defaults to 1.0.</summary>
    public double ScaleFactor    { get; set; } = 1.0;
    /// <summary>
    /// Speed of exponential smoothing toward the target value.
    /// Higher = faster needle response. Default 5.0 gives a snappy analogue feel.
    /// </summary>
    public double AnimationSpeed { get; set; } = 5.0;

    // ── DataBus auto-subscribe ────────────────────────────────────────────────

    private string          _topic        = "";
    private Action<double>? _topicHandler;

    /// <summary>
    /// When set, the gauge subscribes to DataBus.ScalarTelemetry for this topic and
    /// updates automatically each frame. Changing Topic unsubscribes the old one.
    /// Leave empty to drive the gauge manually via SetValue().
    /// </summary>
    public string Topic
    {
        get => _topic;
        set
        {
            if (_topic == value) return;
            if (_topicHandler != null && _topic.Length > 0)
                DataBus.ScalarTelemetry.Unsubscribe(_topic, _topicHandler);
            _topic = value;
            if (_topic.Length > 0)
            {
                _topicHandler = SetValue;
                DataBus.ScalarTelemetry.Subscribe(_topic, _topicHandler);
            }
            else
            {
                _topicHandler = null;
            }
        }
    }

    private double _rawValue;
    private double _displayedValue = double.NaN;  // NaN = not yet received; snaps on first value

    /// <summary>Update the raw bus value. ScaleFactor is applied internally.</summary>
    public void SetValue(double value) => _rawValue = value;

    // ── Animation ─────────────────────────────────────────────────────────────

    public override void Update(double dt)
    {
        double target = _rawValue * ScaleFactor;
        if (double.IsNaN(_displayedValue))
            _displayedValue = target;
        else
            _displayedValue += (target - _displayedValue) * Math.Min(AnimationSpeed * dt, 1.0);
        base.Update(dt);
    }

    // ── Drawing ───────────────────────────────────────────────────────────────

    public override void Draw(SpriteBatch sb, UIRenderer renderer, Theme theme)
    {
        if (!Visible) return;
        var ab = AbsoluteBounds;

        renderer.FillRect(sb, ab, BackColor ?? theme.PanelBackground);
        renderer.DrawRect(sb, ab, ForeColor ?? theme.PanelBorder, 1);

        const int pad        = 7;
        const int labelTopY  = 5;
        const int labelH     = 16;
        const int valueH     = 16;
        const int valuePad   = 5;

        // Label
        renderer.DrawText(sb, Label,
            new Vector2(ab.X + pad, ab.Y + labelTopY),
            theme.Font, theme.SmallScale * 0.88f,
            theme.TextDisabled);

        // Gauge face geometry
        int gaugeTop    = ab.Y + labelTopY + labelH + pad;
        int gaugeBottom = ab.Bottom - valuePad - valueH - pad;
        int gaugeH      = gaugeBottom - gaugeTop;
        if (gaugeH < 10) return;

        int   cx    = ab.X + ab.Width / 2;
        float R     = MathF.Min((ab.Width - pad * 2) * 0.5f, gaugeH) - 2f;
        var   pivot = new Vector2(cx, gaugeBottom);

        // ── Arc ───────────────────────────────────────────────────────────────
        Color arcColor = Color.Lerp(theme.PanelBorder, theme.PanelBackground, 0.3f);
        const int arcSegs = 60;
        for (int i = 0; i < arcSegs; i++)
        {
            float a1 = MathF.PI * (1f - (float)i       / arcSegs);
            float a2 = MathF.PI * (1f - (float)(i + 1) / arcSegs);
            renderer.DrawLine(sb,
                pivot + R * new Vector2(MathF.Cos(a1), -MathF.Sin(a1)),
                pivot + R * new Vector2(MathF.Cos(a2), -MathF.Sin(a2)),
                arcColor, 1.5f);
        }

        // ── Tick marks (20 divisions / 9° each; major every 5th = every 45°) ─
        Color majorColor = theme.TextDisabled;
        Color minorColor = Color.Lerp(theme.PanelBorder, theme.TextDisabled, 0.4f);

        const int divisions = 20;
        for (int i = 0; i <= divisions; i++)
        {
            bool  major = (i % 5 == 0);
            float angle = MathF.PI * (1f - (float)i / divisions);
            float len   = major ? R * 0.16f : R * 0.09f;

            var outer = pivot +  R          * new Vector2(MathF.Cos(angle), -MathF.Sin(angle));
            var inner = pivot + (R - len)   * new Vector2(MathF.Cos(angle), -MathF.Sin(angle));

            renderer.DrawLine(sb, outer, inner,
                major ? majorColor : minorColor,
                major ? 1.5f : 1f);
        }

        // ── Needle ────────────────────────────────────────────────────────────
        double displayVal = double.IsNaN(_displayedValue) ? MinValue : _displayedValue;
        double frac       = MaxValue > MinValue
            ? Math.Clamp((displayVal - MinValue) / (MaxValue - MinValue), 0.0, 1.0)
            : 0.0;

        float needleAngle = MathF.PI * (1f - (float)frac);
        var   needleDir   = new Vector2(MathF.Cos(needleAngle), -MathF.Sin(needleAngle));
        var   tip         = pivot + needleDir * (R - 6f);          // stop short of arc
        var   tail        = pivot - needleDir * (R * 0.14f);       // short counterweight

        renderer.DrawLine(sb, tail, tip, theme.Accent, 2f);

        // Hub dot
        renderer.DrawDot(sb, pivot, 4f, theme.TextNormal);

        // ── Value text (centred at bottom) ────────────────────────────────────
        string valStr  = displayVal.ToString(Format);
        var    valSize = renderer.MeasureText(valStr, theme.Font, theme.SmallScale);
        renderer.DrawText(sb, valStr,
            new Vector2(cx - valSize.X * 0.5f, ab.Bottom - valuePad - valueH),
            theme.Font, theme.SmallScale,
            TextColor ?? theme.TextNormal);
    }
}
