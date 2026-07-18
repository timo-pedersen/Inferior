namespace Inferior.Gameplay.Engines;

public static class EngineDefinitionLibrary
{
    private static readonly IReadOnlyDictionary<string, EngineVariantDefinition> Variants =
        new Dictionary<string, EngineVariantDefinition>(StringComparer.Ordinal)
        {
            [MuleEngineDefinitionFactory.H2VariantId] = MuleEngineDefinitionFactory.CreateH2Variant(),
        };

    public static EngineVariantDefinition GetVariant(string variantId)
        => Variants.TryGetValue(variantId, out var variant)
            ? variant
            : throw new KeyNotFoundException($"Unknown engine variant '{variantId}'.");
}
