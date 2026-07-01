using Inferior.Core.DataBus;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.UI.Controls.Cockpit;

public enum LedShape    { Round, Square }
public enum LabelAnchor { Top, Bottom, Left, Right }

/// <summary>Maps a value range to a lamp colour.</summary>
public record LedColorRange(double Min, double Max, Color Color)
{
    public bool Contains(double v) => v >= Min && v <= Max;
}

/// <summary>
/// Standalone LED indicator. Subscribes to a DataBus.Instruments topic (double),
/// lights up based on configurable on/blink/colour ranges, and eases brightness
/// transitions to simulate an incandescent lamp warming up and cooling down.
/// Not a Control subclass — drawn directly via Draw(SpriteBatch, Point).
/// </summary>
public sealed class LedIndicator : IDisposable
{
    // ── Subscription ──────────────────────────────────────────────────────────
    public string Topic { get; init; }

    // ── Label ─────────────────────────────────────────────────────────────────
    public string      LabelText      { get; set; } = string.Empty;
    public SpriteFont  LabelFont      { get; set; }
    public LabelAnchor LabelAnchor    { get; set; } = LabelAnchor.Bottom;
    public float       LabelFontScale { get; set; } = 1.0f;

    // ── Lamp appearance ───────────────────────────────────────────────────────
    public LedShape Shape     { get; init; } = LedShape.Square;
    public int      LampSize  { get; init; } = 14;
    public Color    MainColor { get; init; } = Color.Red;

    // ── Colour ranges ─────────────────────────────────────────────────────────
    // Evaluated in order; first match overrides MainColor.
    public List<LedColorRange> ColorRanges { get; init; } = new();

    // ── On range ──────────────────────────────────────────────────────────────
    public double OnRangeMin { get; init; } = 0.5;
    public double OnRangeMax { get; init; } = double.PositiveInfinity;

    // ── Blink ─────────────────────────────────────────────────────────────────
    public double BlinkRangeMin     { get; init; } = double.PositiveInfinity;
    public double BlinkRangeMax     { get; init; } = double.PositiveInfinity;
    public double MinBlinkFrequency { get; init; } = 1.0;
    public double MaxBlinkFrequency { get; init; } = 1.0;

    // ── Easing ────────────────────────────────────────────────────────────────
    // Time constant k: ~95% of transition in 3/k seconds. Default k=60 → ~50 ms.
    public double EasingRate { get; init; } = 60.0;

    // ── Internal state ────────────────────────────────────────────────────────
    private double              _value              = 0.0;
    private double              _currentBrightness  = 0.0;
    private readonly Action<double> _subscription;
    private readonly Bus<double>    _bus;

    // ── Shared textures (all instances, same size) ────────────────────────────
    private static Texture2D? _circleTexCache = null;
    private static int         _circleTexSize  = 0;
    private static Texture2D? _pixel          = null;

    // ── Constructor ───────────────────────────────────────────────────────────

    public LedIndicator(string topic, Bus<double> bus, GraphicsDevice gd, SpriteFont labelFont)
    {
        Topic     = topic;
        LabelFont = labelFont;
        _bus      = bus;
        EnsurePixel(gd);

        _subscription = v => _value = v;
        _bus.Subscribe(topic, _subscription);
    }

    public void Dispose()
        => _bus.Unsubscribe(Topic, _subscription);

    // ── Update ────────────────────────────────────────────────────────────────

    public void Update(double dt)
    {
        double target = ComputeTargetBrightness();
        _currentBrightness += (target - _currentBrightness) * (1.0 - Math.Exp(-dt * EasingRate));
        _currentBrightness  = Math.Clamp(_currentBrightness, 0.0, 1.0);
    }

    private double ComputeTargetBrightness()
    {
        bool inOnRange = _value >= OnRangeMin && _value <= OnRangeMax;
        if (!inOnRange) return 0.0;

        bool inBlinkRange = _value >= BlinkRangeMin && _value <= BlinkRangeMax;
        if (inBlinkRange)
        {
            double freq;
            if (MinBlinkFrequency >= MaxBlinkFrequency)
            {
                freq = MinBlinkFrequency;
            }
            else
            {
                double t = Math.Clamp(
                    (_value - BlinkRangeMin) / (BlinkRangeMax - BlinkRangeMin), 0, 1);
                freq = MinBlinkFrequency + t * (MaxBlinkFrequency - MinBlinkFrequency);
            }
            return BlinkClock.IsOnPhase(freq) ? 1.0 : 0.0;
        }

        return 1.0;
    }

    // ── Draw ──────────────────────────────────────────────────────────────────

    public void Draw(SpriteBatch sb, Point position)
    {
        EnsurePixel(sb.GraphicsDevice);

        Color active = GetActiveColor();
        Color off = new Color(
            (byte)(active.R * 0.08f),
            (byte)(active.G * 0.08f),
            (byte)(active.B * 0.08f));
        Color current = Color.Lerp(off, active, (float)_currentBrightness);

        ComputeOffsets(out Point labelOff, out Point lampOff);

        var lampRect  = new Rectangle(position.X + lampOff.X, position.Y + lampOff.Y, LampSize, LampSize);
        var labelPos  = position + labelOff;

        // Lamp — round: circle only; square: dark border then filled square
        if (Shape == LedShape.Round)
        {
            var circleTex = GetOrCreateCircle(sb.GraphicsDevice, LampSize);
            sb.Draw(circleTex, lampRect, current);
        }
        else
        {
            Color borderColor = new Color(30, 30, 30);
            sb.Draw(_pixel!, new Rectangle(lampRect.X - 2, lampRect.Y - 2,
                lampRect.Width + 4, lampRect.Height + 4), borderColor);
            sb.Draw(_pixel!, lampRect, current);
        }

        // Label
        if (!string.IsNullOrEmpty(LabelText) && LabelFont != null)
        {
            FontHelper.Draw(sb, LabelFont, LabelText,
                new Vector2(labelPos.X, labelPos.Y),
                new Color(180, 180, 175), LabelFontScale);
        }
    }

    /// <summary>Total size in pixels including lamp and label.</summary>
    public Point MeasureSize()
    {
        Vector2 label  = (LabelFont?.MeasureString(LabelText) ?? Vector2.Zero) * LabelFontScale;
        const int gap  = 4;
        return LabelAnchor switch
        {
            LabelAnchor.Top or LabelAnchor.Bottom =>
                new Point(
                    Math.Max(LampSize, (int)label.X) + 4,
                    LampSize + (int)label.Y + gap + 4),
            _ =>
                new Point(
                    LampSize + (int)label.X + gap + 4,
                    Math.Max(LampSize, (int)label.Y) + 4),
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Color GetActiveColor()
    {
        foreach (var range in ColorRanges)
            if (range.Contains(_value))
                return range.Color;
        return MainColor;
    }

    private void ComputeOffsets(out Point labelOff, out Point lampOff)
    {
        Vector2 labelSize = (LabelFont?.MeasureString(LabelText) ?? Vector2.Zero) * LabelFontScale;
        const int gap = 4;

        lampOff  = Point.Zero;
        labelOff = Point.Zero;

        switch (LabelAnchor)
        {
            case LabelAnchor.Bottom:
                labelOff = new Point(
                    (LampSize - (int)labelSize.X) / 2,
                    LampSize + gap);
                break;
            case LabelAnchor.Top:
                labelOff = new Point(
                    (LampSize - (int)labelSize.X) / 2,
                    -(int)labelSize.Y - gap);
                lampOff  = new Point(0, (int)labelSize.Y + gap);
                break;
            case LabelAnchor.Left:
                labelOff = new Point(-(int)labelSize.X - gap,
                    (LampSize - (int)labelSize.Y) / 2);
                lampOff  = new Point((int)labelSize.X + gap, 0);
                break;
            case LabelAnchor.Right:
                labelOff = new Point(LampSize + gap,
                    (LampSize - (int)labelSize.Y) / 2);
                break;
        }
    }

    private static void EnsurePixel(GraphicsDevice gd)
    {
        if (_pixel != null && !_pixel.IsDisposed) return;
        _pixel = new Texture2D(gd, 1, 1);
        _pixel.SetData(new[] { Color.White });
    }

    private static Texture2D GetOrCreateCircle(GraphicsDevice gd, int size)
    {
        if (_circleTexCache != null && !_circleTexCache.IsDisposed && _circleTexSize == size)
            return _circleTexCache;

        _circleTexCache?.Dispose();
        _circleTexSize = size;
        var data = new Color[size * size];
        float r  = size / 2f;
        float cx = size / 2f, cy = size / 2f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx    = x - cx + 0.5f, dy = y - cy + 0.5f;
            float dist  = MathF.Sqrt(dx * dx + dy * dy);
            float alpha = Math.Clamp(r - dist + 0.5f, 0, 1);
            // Premultiplied alpha — required for BlendState.AlphaBlend (Blend.One source).
            // Unpremultiplied (1,1,1,alpha) would add white to corners instead of being transparent.
            data[y * size + x] = new Color(alpha, alpha, alpha, alpha);
        }
        _circleTexCache = new Texture2D(gd, size, size);
        _circleTexCache.SetData(data);
        return _circleTexCache;
    }
}
