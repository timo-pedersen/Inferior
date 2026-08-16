namespace Inferior.Rendering;

/// <summary>
/// Brief B4: live-tunable station brightness parameters — mirrors <see cref="SunTuning"/>'s
/// plain static-property pattern (scene-level renderer/generation state, not a per-instance
/// option). The DefaultXxx fields are the single source of truth both this class's own live
/// values AND the tuning panel's starting values read from — the panel and the defaults can
/// never drift apart (the rule established in Brief B3).
///
/// Brief B5: Timo's tuned values are now baked into these DefaultXxx constants (across red/
/// white/yellow/blue star systems and a deliberately colourful station) — this class's live
/// values start at the tuned appearance with no panel interaction required. The panel itself
/// stays (per the brief: "it earned its place and tuning may continue") — only the defaults
/// it reads from moved.
///
/// Two different consumers read these:
/// - <see cref="DecorationBrightnessMultiplier"/> — read every frame by
///   <c>MeshRenderer.DrawBakedColorLit</c>/<c>DrawBakedColorLitShadowed</c>, a live shader
///   uniform (<c>LitSurface.fx</c>'s <c>DecorationBrightness</c>). Zero regeneration cost —
///   changes apply on the very next frame.
/// - <see cref="VariantValueFloor"/>/<see cref="VariantCompressionStrength"/>/
///   <see cref="SaturationFalloff"/> — read only at STATION PANEL TEXTURE GENERATION time
///   (<c>StationTextureRegistry.OffsetPaletteForVariant</c>). Changing these does NOT
///   retroactively alter textures already baked into a loaded station. Brief B4 shipped this
///   half of the panel with only a manual regenerate action (J); Brief B4a found that wasn't
///   enough — Timo had to leave and re-enter the system to see any effect at all — so the
///   panel now triggers a debounced rebuild of the current station automatically whenever one
///   of these three changes (see SystemSpaceState.StationBrightnessTuning.cs), with J kept as
///   a manual force-regenerate.
///
/// <see cref="Ambient"/> is a live pass-through to <see cref="SceneLighting.Ambient"/>, not a
/// separate store — SceneLighting already owns this value and multiple systems read it every
/// frame (station hull, station decoration, planets via BasicEffect elsewhere); duplicating
/// it here would create two sources of truth for the same number. This class just gives it a
/// slot in the tuning panel's parameter list, get/set forwarding directly.
///
/// Deliberately NOT persisted across restarts, same reasoning as SunTuning — the tuning
/// panel's dump key prints a paste-ready source block instead.
/// </summary>
public static class StationBrightnessTuning
{
    // ── Defaults — also the values ResetToDefaults() restores ──────────────────────────
    // Brief B5: decoration was never the problem — the ~0.50 vertex-colour multiply is real
    // arithmetic but the greeble-invisible complaint traced to the variant tail (a near-black
    // rolled BaseColour), not a globally-dark decoration path. 1.0 is Timo's settled answer,
    // not a placeholder — see StationTextureRegistry's variant floor/compression instead.
    public const float DefaultDecorationBrightnessMultiplier = 1f;
    // Brief B5: 0.09 -> 0.1, "essentially unchanged" — confirmed by testing, not assumed,
    // that ambient was likewise not the real problem. Matches SceneLighting.Ambient's own
    // field-initializer default exactly, since this is a pass-through, not an independent
    // value — its default must match the thing it forwards to.
    public const float DefaultAmbient = 0.1f;
    // Brief B5: 0.15 (Brief P1 Fix A's original pathological-black-variant floor) -> 0.2.
    public const float DefaultVariantValueFloor = 0.2f;
    // Brief B5: 0 -> 1.0, full strength. This is where the real fix lives — lifting the
    // lower tail SMOOTHLY so dark modules (Nova Anchorage's bay: post-offset BaseColour
    // (0,0,0), ~17 mean luminance against a ~130 median) become legible while staying
    // distinguishable from each other, preserving S2b-2's deliberate variance rather than
    // piling everything onto one floor value.
    public const float DefaultVariantCompressionStrength = 1f;
    // Brief B5: 0 -> 1.0, full coupling. Flooring/lifting HSV value alone reads as a large
    // apparent saturation increase (v*s = chroma) — at full strength, lifting v no longer
    // intensifies colour at all, which is what "over-saturated red-star stations improved"
    // needed. See StationTextureRegistry.ApplyHsvOffset for the mechanism.
    public const float DefaultSaturationFalloff = 1f;

    // ── Live values ──────────────────────────────────────────────────────────────────
    public static float DecorationBrightnessMultiplier { get; set; } = DefaultDecorationBrightnessMultiplier;
    public static float VariantValueFloor              { get; set; } = DefaultVariantValueFloor;
    public static float VariantCompressionStrength     { get; set; } = DefaultVariantCompressionStrength;
    public static float SaturationFalloff              { get; set; } = DefaultSaturationFalloff;

    public static float Ambient
    {
        get => SceneLighting.Ambient;
        set => SceneLighting.Ambient = value;
    }

    public static void ResetToDefaults()
    {
        DecorationBrightnessMultiplier = DefaultDecorationBrightnessMultiplier;
        Ambient                        = DefaultAmbient;
        VariantValueFloor              = DefaultVariantValueFloor;
        VariantCompressionStrength     = DefaultVariantCompressionStrength;
        SaturationFalloff              = DefaultSaturationFalloff;
    }
}
