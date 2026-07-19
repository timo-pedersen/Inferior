using Inferior.Core.Math;
using Microsoft.Xna.Framework;

namespace Inferior.Gameplay.Cockpit;

public static class CockpitDefinitionLibrary
{
    public const string AriesCivilianCanopyId = "aries-civilian-canopy-cockpit";

    private static readonly Dictionary<string, CockpitModuleDefinition> Definitions =
        new(StringComparer.OrdinalIgnoreCase);

    static CockpitDefinitionLibrary()
    {
        Register(new CockpitModuleDefinition
        {
            DefinitionId = AriesCivilianCanopyId,
            DisplayName = "Aries Civilian Canopy Cockpit",
            RequiredMountClass = CockpitMountClass.C2,
            PilotLocalPosition = new DVec3(0.0, -0.55, 0.25),
            PilotLocalOrientation = Quaternion.Identity,
            CameraLocalPosition = DVec3.Zero,
            CameraLocalOrientation = Quaternion.Identity,
            CanopyLocalPosition = new DVec3(0.0, 0.35, 0.0),
            CanopyLocalOrientation = Quaternion.Identity,
            PreferredFacing = MountFacing.Up,
            HasCanopyLights = true,
            HasCockpitLights = true,
            VisualGeometry = AriesCivilianCockpitGeometryFactory.Create(),
        });
    }

    public static CockpitModuleDefinition Get(string definitionId)
    {
        if (Definitions.TryGetValue(definitionId, out CockpitModuleDefinition? definition))
            return definition;

        throw new KeyNotFoundException($"No cockpit definition found for '{definitionId}'.");
    }

    public static bool TryGet(string definitionId, out CockpitModuleDefinition? definition)
        => Definitions.TryGetValue(definitionId, out definition);

    public static IReadOnlyCollection<CockpitModuleDefinition> All => Definitions.Values;

    private static void Register(CockpitModuleDefinition definition)
    {
        if (!Definitions.TryAdd(definition.DefinitionId, definition))
        {
            throw new InvalidOperationException(
                $"Duplicate cockpit definition '{definition.DefinitionId}'.");
        }
    }
}
