using Inferior.Core.DataBus;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.UI.Controls.Cockpit;

/// <summary>
/// Smoothed filled-area graph for displaying a solar (or other) spectrum.
/// Data points are Catmull-Rom interpolated to a 128-point curve at SetSpectrum time.
///
/// Set Topic to auto-subscribe to DataBus.Spectra; leave empty to drive via SetSpectrum().
///
/// Intended aspect ratio: 1:5 height:width (caller sets Bounds accordingly).
///
/// Layout:
///   ┌─────────────────────────────────────────────────────────────────┐
///   │ SOLAR SPECTRUM                                                  │ ← 14px header
///   │  ▄████████▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄ │ ← coloured area
///   └─────────────────────────────────────────────────────────────────┘
/// Columns are tinted by wavelength: UV = violet, visible = rainbow, IR = dark red.
/// </summary>
public sealed class SpectrumGraph : Control
{
    public string Header { get; set; } = "SOLAR SPECTRUM";

    // ── DataBus auto-subscribe ────────────────────────────────────────────────

    private string            _topic       = "";
    private Action<double[]>? _topicHandler;

    public string Topic
    {
        get => _topic;
        set
        {
            if (_topic == value) return;
            if (_topicHandler != null && _topic.Length > 0)
                DataBus.Spectra.Unsubscribe(_topic, _topicHandler);
            _topic = value;
            if (_topic.Length > 0)
            {
                _topicHandler = SetSpectrum;
                DataBus.Spectra.Subscribe(_topic, _topicHandler);
            }
            else _topicHandler = null;
        }
    }

    // ── Data ──────────────────────────────────────────────────────────────────

    private const int SmoothedPoints = 128;
    private double[]? _smoothed;
    private bool      _hasData;

    /// <summary>Update the displayed spectrum. Values should be normalised 0-1.</summary>
    public void SetSpectrum(double[] values)
    {
        _smoothed = BuildSmoothed(values, SmoothedPoints);
        _hasData  = true;
    }

    // ── Draw ──────────────────────────────────────────────────────────────────

    public override void Draw(SpriteBatch sb, UIRenderer renderer, Theme theme)
    {
        if (!Visible) return;
        var ab = AbsoluteBounds;

        renderer.FillRect(sb, ab, theme.PanelBackground);
        renderer.DrawRect(sb, ab, theme.PanelBorder, 1);

        const int pad     = 6;
        const int headerH = 14;

        renderer.DrawText(sb, Header,
            new Vector2(ab.X + pad, ab.Y + 3),
            theme.Font, theme.SmallScale * 0.85f, theme.TextDisabled);

        int graphX = ab.X  + pad;
        int graphY = ab.Y  + headerH + pad;
        int graphW = ab.Width  - pad * 2;
        int graphH = ab.Bottom - graphY - pad;

        if (graphW < 4 || graphH < 4) return;

        renderer.FillRect(sb, new Rectangle(graphX, graphY, graphW, graphH), new Color(8, 12, 24));

        if (!_hasData || _smoothed == null)
        {
            renderer.DrawText(sb, "NO DATA",
                new Vector2(graphX + 4, graphY + (graphH - 12) / 2),
                theme.Font, theme.SmallScale * 0.75f, new Color(50, 60, 80));
            renderer.DrawRect(sb, new Rectangle(graphX, graphY, graphW, graphH), theme.PanelBorder, 1);
            return;
        }

        // ── Filled spectrum ───────────────────────────────────────────────────
        var sm = _smoothed;
        for (int px = 0; px < graphW; px++)
        {
            float  t   = graphW > 1 ? 1f - (float)px / (graphW - 1) : 1f;
            float  si  = t * (sm.Length - 1);
            int    i0  = (int)si;
            float  f   = si - i0;
            double v0  = sm[i0];
            double v1  = i0 + 1 < sm.Length ? sm[i0 + 1] : v0;
            double val = System.Math.Clamp(v0 + (v1 - v0) * f, 0.0, 1.0);

            int barH = (int)(val * graphH);
            if (barH <= 0) continue;

            Color fill = WavelengthColor(t, 0.55f);
            Color line = WavelengthColor(t, 1.00f);

            if (barH > 1)
                renderer.FillRect(sb, new Rectangle(graphX + px, graphY + graphH - barH, 1, barH - 1), fill);
            // Bright top pixel for the curve outline
            renderer.FillRect(sb, new Rectangle(graphX + px, graphY + graphH - barH, 1, 1), line);
        }

        // Baseline
        renderer.FillRect(sb, new Rectangle(graphX, graphY + graphH - 1, graphW, 1), theme.PanelBorder);
        renderer.DrawRect(sb, new Rectangle(graphX, graphY, graphW, graphH), theme.PanelBorder, 1);
    }

    // ── Catmull-Rom smoothing ─────────────────────────────────────────────────

    private static double[] BuildSmoothed(double[] src, int resolution)
    {
        int n = src.Length;
        if (n < 2) return n == 0 ? [] : (double[])src.Clone();
        var result = new double[resolution];
        for (int i = 0; i < resolution; i++)
        {
            double t  = (double)i / (resolution - 1) * (n - 1);
            int    p1 = (int)t;
            double mu = t - p1;
            int    p0 = System.Math.Max(p1 - 1, 0);
            int    p2 = System.Math.Min(p1 + 1, n - 1);
            int    p3 = System.Math.Min(p1 + 2, n - 1);
            result[i] = System.Math.Clamp(CatmullRom(src[p0], src[p1], src[p2], src[p3], mu), 0.0, 1.0);
        }
        return result;
    }

    private static double CatmullRom(double p0, double p1, double p2, double p3, double t)
    {
        double t2 = t * t, t3 = t2 * t;
        return 0.5 * ((2.0 * p1)
             + (-p0 + p2) * t
             + (2.0 * p0 - 5.0 * p1 + 4.0 * p2 - p3) * t2
             + (-p0 + 3.0 * p1 - 3.0 * p2 + p3) * t3);
    }

    // ── Wavelength colour gradient ────────────────────────────────────────────
    // The 10 data bins are plotted at equal pixel spacing, but their wavelengths
    // are NOT linearly spaced (bins jump from 750 → 900 → 1200 → 2000 nm at the IR end).
    // So we must convert pixel position → actual wavelength in nm, then colour from nm.

    /// <summary>
    /// Wavelengths (nm) corresponding to each data bin, left to right.
    /// Defaults to Environment.SpectrumWavelengthsNm [150…2000 nm].
    /// Set this if you drive the graph with a different sensor's bins.
    /// </summary>
    public float[] WavelengthsNm { get; set; } =
        [150f, 250f, 350f, 450f, 550f, 650f, 750f, 900f, 1200f, 2000f];

    private Color WavelengthColor(float tPixel, float alpha)
    {
        float nm = PixelToNm(tPixel);
        return NmToColor(nm, alpha);
    }

    private float PixelToNm(float tPixel)
    {
        var wl   = WavelengthsNm;
        int last = wl.Length - 1;
        if (last <= 0) return wl[0];
        float idx = tPixel * last;
        int   i0  = System.Math.Clamp((int)idx, 0, last - 1);
        float f   = idx - i0;
        return wl[i0] + (wl[i0 + 1] - wl[i0]) * f;
    }

    private static Color NmToColor(float nm, float alpha)
    {
        Vector3 c;
        if      (nm < 350f) c = Lerp3(new(0.55f, 0.00f, 0.80f), new(0.20f, 0.00f, 0.90f), Unlerp(nm, 150f, 350f));
        else if (nm < 420f) c = Lerp3(new(0.20f, 0.00f, 0.90f), new(0.05f, 0.05f, 1.00f), Unlerp(nm, 350f, 420f));
        else if (nm < 490f) c = Lerp3(new(0.05f, 0.05f, 1.00f), new(0.00f, 0.80f, 0.40f), Unlerp(nm, 420f, 490f));
        else if (nm < 560f) c = Lerp3(new(0.00f, 0.80f, 0.40f), new(0.90f, 0.90f, 0.00f), Unlerp(nm, 490f, 560f));
        else if (nm < 630f) c = Lerp3(new(0.90f, 0.90f, 0.00f), new(1.00f, 0.10f, 0.00f), Unlerp(nm, 560f, 630f));
        else if (nm < 750f) c = Lerp3(new(1.00f, 0.10f, 0.00f), new(0.55f, 0.00f, 0.00f), Unlerp(nm, 630f, 750f));
        else if (nm < 1100f) c = Lerp3(new(0.55f, 0.00f, 0.00f), new(0.20f, 0.00f, 0.00f), Unlerp(nm, 750f, 1100f));
        else                c = Lerp3(new(0.20f, 0.00f, 0.00f), new(0.04f, 0.00f, 0.00f), MathF.Min(Unlerp(nm, 1100f, 2200f), 1f));
        c = Vector3.Clamp(c, Vector3.Zero, Vector3.One);
        return new Color(c * alpha);
    }

    private static float Unlerp(float x, float a, float b) => (x - a) / (b - a);

    private static Vector3 Lerp3(Vector3 a, Vector3 b, float t) => a + (b - a) * t;
}
