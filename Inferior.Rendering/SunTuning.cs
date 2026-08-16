namespace Inferior.Rendering;

/// <summary>
/// Brief B2 Fix 3: live-tunable sun disc/glare parameters — mirrors SceneLighting's plain
/// static-property pattern (this is scene-level renderer state, not a per-instance option).
/// CelestialBodyRenderer reads these every frame instead of its own former hardcoded
/// constants; the DefaultXxx fields are the reference values a fresh session starts at (and
/// what <see cref="ResetToDefaults"/> returns to), so the panel's own baseline and this
/// class's own defaults can never silently drift apart. Brief B3 Fix 1: this is now also the
/// single source of truth the panel reads its PER-PARAMETER reset value from (arrow-left) —
/// nothing outside this block duplicates these literals.
///
/// Deliberately NOT persisted across restarts (the brief's own explicit design: "Values need
/// not persist across restarts — the dump key covers that") — SystemSpaceState's tuning-panel
/// dump key prints a paste-ready source block so a tuning session's results go back into
/// these DefaultXxx constants by hand, not through a config file.
/// </summary>
public static class SunTuning
{
    // ── Defaults — also the values ResetToDefaults() restores ──────────────────────────
    // Brief B3 Fix 1: baked from Timo's own in-engine tuning, confirmed legible from close
    // approach out to 160 AU (measured live at Sinaa, K-class, 141.056 AU: disc 1.43px
    // floor-bound, glare outer 72.15px — matches DefaultDiscFloorPixels exactly, as expected
    // for a floor-bound distance this far out). Superseded B2's placeholder starting values.
    public const float DefaultDiscFloorPixels          = 1.4333f;
    public const float DefaultLimbDarkeningStrength     = 0.3667f;
    // Brief B3: moved from B2's placeholder 0.5 (which exactly reproduced the pre-B2
    // sqrt(Luminosity)/distanceAU formula) to a much flatter 0.1867 — glare size now varies
    // only modestly with distance, which is what buys readability at 160 AU; near-field
    // drama comes mostly from the disc now, not the glare. If close approaches read as
    // underwhelming, this is the dial per Timo's own note in the brief.
    public const float DefaultGlareCompressionExponent  = 0.1867f;
    public const float DefaultGlareSizeMultiplier       = 1.1000f;
    public const float DefaultGlareIntensityMultiplier  = 0.8500f;
    public const float DefaultGlareLayer0Alpha          = 0.9933f;
    public const float DefaultGlareLayer1Alpha          = 0.7533f;
    public const float DefaultGlareLayer2Alpha          = 0.7500f;
    public const float DefaultGlareLayer3Alpha          = 0.3900f;
    public const float DefaultGlareLayer4Alpha          = 0.4200f;

    // ── Live values ──────────────────────────────────────────────────────────────────
    public static float DiscFloorPixels          { get; set; } = DefaultDiscFloorPixels;
    public static float LimbDarkeningStrength    { get; set; } = DefaultLimbDarkeningStrength;
    public static float GlareCompressionExponent { get; set; } = DefaultGlareCompressionExponent;
    public static float GlareSizeMultiplier      { get; set; } = DefaultGlareSizeMultiplier;
    public static float GlareIntensityMultiplier { get; set; } = DefaultGlareIntensityMultiplier;
    public static float GlareLayer0Alpha         { get; set; } = DefaultGlareLayer0Alpha;
    public static float GlareLayer1Alpha         { get; set; } = DefaultGlareLayer1Alpha;
    public static float GlareLayer2Alpha         { get; set; } = DefaultGlareLayer2Alpha;
    public static float GlareLayer3Alpha         { get; set; } = DefaultGlareLayer3Alpha;
    public static float GlareLayer4Alpha         { get; set; } = DefaultGlareLayer4Alpha;

    public static void ResetToDefaults()
    {
        DiscFloorPixels          = DefaultDiscFloorPixels;
        LimbDarkeningStrength    = DefaultLimbDarkeningStrength;
        GlareCompressionExponent = DefaultGlareCompressionExponent;
        GlareSizeMultiplier      = DefaultGlareSizeMultiplier;
        GlareIntensityMultiplier = DefaultGlareIntensityMultiplier;
        GlareLayer0Alpha         = DefaultGlareLayer0Alpha;
        GlareLayer1Alpha         = DefaultGlareLayer1Alpha;
        GlareLayer2Alpha         = DefaultGlareLayer2Alpha;
        GlareLayer3Alpha         = DefaultGlareLayer3Alpha;
        GlareLayer4Alpha         = DefaultGlareLayer4Alpha;
    }
}
