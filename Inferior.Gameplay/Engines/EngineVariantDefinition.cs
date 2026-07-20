namespace Inferior.Gameplay.Engines;

public static class EngineMountStandardIds
{
    public const string Eriksson = "Eriksson";
    public const string H2 = "H2";
}

/// <summary>A manufactured engine family adapted to one physical mount standard.</summary>
public sealed class EngineVariantDefinition
{
    public EngineVariantDefinition(
        string variantId,
        EngineDefinition engine,
        string mountStandardId)
    {
        if (string.IsNullOrWhiteSpace(variantId))
            throw new ArgumentException("Engine variant id must not be empty.", nameof(variantId));
        if (string.IsNullOrWhiteSpace(mountStandardId))
            throw new ArgumentException("Mount standard id must not be empty.", nameof(mountStandardId));

        VariantId = variantId;
        Engine = engine ?? throw new ArgumentNullException(nameof(engine));
        MountStandardId = mountStandardId;
    }

    public string VariantId { get; }
    public EngineDefinition Engine { get; }
    public string MountStandardId { get; }

    public bool IsCompatibleWith(string mountStandardId)
        => string.Equals(MountStandardId, mountStandardId, StringComparison.Ordinal);
}
