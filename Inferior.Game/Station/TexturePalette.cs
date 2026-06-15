using Microsoft.Xna.Framework;

namespace Inferior.Game.StationGen;

public enum FontStyle { Stencil, WornStencil, Military, Technical, Corporate }

public sealed class TexturePalette
{
    public Color     BaseColour       { get; init; }
    public Color     AccentColour     { get; init; }
    public Color     GrimeColour      { get; init; }
    public float     NoiseStrength    { get; init; }   // 0.0–1.0 per-pixel random offset
    public float     SubPanelContrast { get; init; }   // 0.0–1.0 inter-panel brightness shift
    public float     GrimeStrength    { get; init; }   // 0.0–1.0 edge darkening intensity
    public FontStyle NameFont         { get; init; }
    public Color     TextColour       { get; init; }

    public static TexturePalette From(StationProfile profile) => profile.Economy switch
    {
        StationEconomy.Industrial => new TexturePalette
        {
            BaseColour       = new Color( 90,  85,  78),
            AccentColour     = new Color(200, 140,  40),
            GrimeColour      = new Color( 28,  22,  15),
            NoiseStrength    = 0.18f,
            SubPanelContrast = 0.16f,
            GrimeStrength    = 0.38f,
            NameFont         = FontStyle.Stencil,
            TextColour       = new Color(220, 180,  60),
        },
        StationEconomy.Military => new TexturePalette
        {
            BaseColour       = new Color( 68,  76,  70),
            AccentColour     = new Color(140, 190, 120),
            GrimeColour      = new Color( 18,  22,  18),
            NoiseStrength    = 0.07f,
            SubPanelContrast = 0.07f,
            GrimeStrength    = 0.15f,
            NameFont         = FontStyle.Military,
            TextColour       = new Color(170, 220, 150),
        },
        StationEconomy.Scientific => new TexturePalette
        {
            BaseColour       = new Color(138, 148, 162),
            AccentColour     = new Color( 90, 170, 220),
            GrimeColour      = new Color( 38,  42,  50),
            NoiseStrength    = 0.06f,
            SubPanelContrast = 0.10f,
            GrimeStrength    = 0.08f,
            NameFont         = FontStyle.Technical,
            TextColour       = new Color(110, 200, 245),
        },
        StationEconomy.Luxury => new TexturePalette
        {
            BaseColour       = new Color(200, 196, 186),
            AccentColour     = new Color(220, 180,  95),
            GrimeColour      = new Color( 60,  54,  48),
            NoiseStrength    = 0.04f,
            SubPanelContrast = 0.06f,
            GrimeStrength    = 0.04f,
            NameFont         = FontStyle.Corporate,
            TextColour       = new Color(240, 200, 120),
        },
        StationEconomy.Commercial => new TexturePalette
        {
            BaseColour       = new Color(158, 156, 150),
            AccentColour     = new Color( 95, 155, 200),
            GrimeColour      = new Color( 38,  36,  32),
            NoiseStrength    = 0.11f,
            SubPanelContrast = 0.13f,
            GrimeStrength    = 0.20f,
            NameFont         = FontStyle.Corporate,
            TextColour       = new Color(130, 185, 220),
        },
        StationEconomy.Agricultural => new TexturePalette
        {
            BaseColour       = new Color(128, 138, 108),
            AccentColour     = new Color(155, 195,  90),
            GrimeColour      = new Color( 38,  44,  28),
            NoiseStrength    = 0.15f,
            SubPanelContrast = 0.16f,
            GrimeStrength    = 0.24f,
            NameFont         = FontStyle.WornStencil,
            TextColour       = new Color(175, 215, 120),
        },
        _ => new TexturePalette  // Independent
        {
            BaseColour       = new Color(108, 106,  98),
            AccentColour     = new Color(178, 158, 118),
            GrimeColour      = new Color( 28,  26,  22),
            NoiseStrength    = 0.16f,
            SubPanelContrast = 0.13f,
            GrimeStrength    = 0.30f,
            NameFont         = FontStyle.WornStencil,
            TextColour       = new Color(198, 175, 135),
        },
    };

    public static Color LerpColor(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new Color(
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t));
    }
}
