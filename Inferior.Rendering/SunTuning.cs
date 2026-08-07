namespace Inferior.Rendering;

/// <summary>
/// Brief B2 Fix 3: live-tunable sun disc/glare parameters — mirrors SceneLighting's plain
/// static-property pattern (this is scene-level renderer state, not a per-instance option).
/// CelestialBodyRenderer reads these every frame instead of its own former hardcoded
/// constants; the DefaultXxx fields are the reference values a fresh session starts at (and
/// what <see cref="ResetToDefaults"/> returns to), so the panel's own baseline and this
/// class's own defaults can never silently drift apart.
///
/// Deliberately NOT persisted across restarts (the brief's own explicit design: "Values need
/// not persist across restarts — the dump key covers that") — SystemSpaceState's tuning-panel
/// dump key prints a paste-ready source block so a tuning session's results go back into
/// these DefaultXxx constants by hand, not through a config file.
/// </summary>
public static class SunTuning
{
    // ── Defaults — also the values ResetToDefaults() restores ──────────────────────────
    public const float DefaultDiscFloorPixels          = 1f;
    public const float DefaultLimbDarkeningStrength     = 0.7f;
    // 0.5 reproduces the pre-B2 sqrt(Luminosity)/distanceAU formula exactly:
    // (Luminosity/distanceAU^2)^0.5 = sqrt(Luminosity)/distanceAU.
    public const float DefaultGlareCompressionExponent  = 0.5f;
    public const float DefaultGlareSizeMultiplier       = 1f;
    public const float DefaultGlareIntensityMultiplier  = 1f;
    public const float DefaultGlareLayer0Alpha          = 0.95f;
    public const float DefaultGlareLayer1Alpha          = 0.80f;
    public const float DefaultGlareLayer2Alpha          = 0.45f;
    public const float DefaultGlareLayer3Alpha          = 0.22f;
    public const float DefaultGlareLayer4Alpha          = 0.06f;

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
