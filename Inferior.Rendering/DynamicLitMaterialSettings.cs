namespace Inferior.Rendering;

public enum DynamicLitSpecularPreset
{
    Off,
    Subtle,
    Default,
    Strong,
    Tight,
}

public readonly record struct DynamicLitMaterialSettings(float SpecularStrength, float SpecularShininess)
{
    public static DynamicLitMaterialSettings Off { get; } = new(0f, 32f);
    public static DynamicLitMaterialSettings Subtle { get; } = new(0.2f, 16f);
    public static DynamicLitMaterialSettings Default { get; } = new(0.4f, 32f);
    public static DynamicLitMaterialSettings Strong { get; } = new(0.7f, 24f);
    public static DynamicLitMaterialSettings Tight { get; } = new(0.5f, 96f);

    public static DynamicLitMaterialSettings ForPreset(DynamicLitSpecularPreset preset) => preset switch
    {
        DynamicLitSpecularPreset.Subtle  => Subtle,
        DynamicLitSpecularPreset.Default => Default,
        DynamicLitSpecularPreset.Strong  => Strong,
        DynamicLitSpecularPreset.Tight   => Tight,
        _                                => Off,
    };
}
