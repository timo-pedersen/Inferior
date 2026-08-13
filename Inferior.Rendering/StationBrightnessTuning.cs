namespace Inferior.Rendering;

/// <summary>
/// Brief B4: live-tunable station brightness parameters — mirrors <see cref="SunTuning"/>'s
/// plain static-property pattern (scene-level renderer/generation state, not a per-instance
/// option). The DefaultXxx fields are the single source of truth both this class's own live
/// values AND the tuning panel's starting values read from — the panel and the defaults can
/// never drift apart (the rule established in Brief B3).
///
/// Two different consumers read these:
/// - <see cref="DecorationBrightnessMultiplier"/> — read every frame by
///   <c>MeshRenderer.DrawBakedColorLit</c>/<c>DrawBakedColorLitShadowed</c>, a live shader
///   uniform (<c>LitSurface.fx</c>'s <c>DecorationBrightness</c>). Zero regeneration cost —
///   changes apply on the very next frame.
/// - <see cref="VariantValueFloor"/>/<see cref="VariantCompressionStrength"/> — read only at
///   STATION PANEL TEXTURE GENERATION time (<c>StationTextureRegistry.OffsetPaletteForVariant</c>).
///   Changing these does NOT retroactively alter textures already baked into a loaded
///   station; the tuning panel's regenerate action re-runs texture generation for the
///   nearest station to preview a change (see SystemSpaceState.StationBrightnessTuning.cs).
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
    public const float DefaultDecorationBrightnessMultiplier = 1f;
    // Matches SceneLighting.Ambient's own field-initializer default exactly — this is a
    // pass-through, not an independent value, so its "default" must match the thing it
    // forwards to.
    public const float DefaultAmbient = 0.09f;
    // Matches the pre-B4 fixed MinVariantBaseValue constant (Brief P1 Fix A) — baking this
    // brief's mechanism in at the exact value already shipped keeps the neutral-default
    // rendering identical until Timo actually tunes something.
    public const float DefaultVariantValueFloor = 0.15f;
    // 0 = no compression, i.e. exactly the pre-B4 behaviour (a hard floor at
    // VariantValueFloor, nothing else remapped) — neutral by construction.
    public const float DefaultVariantCompressionStrength = 0f;

    // ── Live values ──────────────────────────────────────────────────────────────────
    public static float DecorationBrightnessMultiplier { get; set; } = DefaultDecorationBrightnessMultiplier;
    public static float VariantValueFloor              { get; set; } = DefaultVariantValueFloor;
    public static float VariantCompressionStrength     { get; set; } = DefaultVariantCompressionStrength;

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
    }
}
