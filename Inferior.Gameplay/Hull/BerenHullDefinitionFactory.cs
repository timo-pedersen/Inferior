using Inferior.Gameplay.Hull.Authoring;

namespace Inferior.Gameplay.Hull;

public static class BerenHullDefinitionFactory
{
    public const string HullId = "beren";
    public const string AssetPath = "Assets/Ships/beren.ship.json";

    public static HullDefinition Create()
        => ShipAuthoringJson.LoadHull(AssetPath).HullDefinition;
}
