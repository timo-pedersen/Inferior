using Inferior.Gameplay.Hull;

namespace Inferior.Game.Ships;

public static class PlayerShipCycleCatalog
{
    public static IReadOnlyList<string> HullTypeIds { get; } =
    [
        AriesHullDefinitionFactory.HullId,
        AsteriskHullDefinitionFactory.HullId,
        BerenHullDefinitionFactory.HullId,
    ];

    public static string GetNext(string currentHullTypeId)
    {
        int index = -1;
        for (int i = 0; i < HullTypeIds.Count; i++)
        {
            if (string.Equals(
                    HullTypeIds[i],
                    currentHullTypeId,
                    StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                break;
            }
        }

        return HullTypeIds[(index + 1) % HullTypeIds.Count];
    }
}
