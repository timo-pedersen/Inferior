using Inferior.Gameplay.Engines;

namespace Inferior.Game;

internal sealed record EngineDebugConfiguration(
    string? VariantId,
    string Notification);

internal static class EngineDebugConfigurations
{
    private static readonly EngineDebugConfiguration MuleH2 = new(
        MuleEngineDefinitionFactory.H2VariantId,
        "ENGINE CONFIGURATION\nH2 Mule pair installed");

    private static readonly EngineDebugConfiguration NeedleH2 = new(
        NeedleEngineDefinitionFactory.H2VariantId,
        "ENGINE CONFIGURATION\nH2 Needle pair installed");

    private static readonly EngineDebugConfiguration Empty = new(
        null,
        "ENGINE CONFIGURATION\nNo propulsion engines installed");

    public static EngineDebugConfiguration GetNext(IReadOnlyList<EngineMount> mounts)
    {
        string? installedPairVariant = GetInstalledPairVariant(mounts);
        if (string.Equals(
            installedPairVariant,
            MuleEngineDefinitionFactory.H2VariantId,
            StringComparison.Ordinal))
        {
            return NeedleH2;
        }

        if (string.Equals(
            installedPairVariant,
            NeedleEngineDefinitionFactory.H2VariantId,
            StringComparison.Ordinal))
        {
            return Empty;
        }

        return MuleH2;
    }

    private static string? GetInstalledPairVariant(IReadOnlyList<EngineMount> mounts)
    {
        if (mounts.Count != 2)
            return null;

        string? first = mounts[0].InstalledEngine?.Variant.VariantId;
        string? second = mounts[1].InstalledEngine?.Variant.VariantId;
        return first is not null && string.Equals(first, second, StringComparison.Ordinal)
            ? first
            : null;
    }
}
