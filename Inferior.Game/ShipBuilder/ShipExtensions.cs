using Inferior.Gameplay.Ship;
using Inferior.Persistence.Data;

namespace Inferior.Game.Ships;

public static class ShipExtensions
{
    public static ShipRecord ToRecord(this Ship ship) => new()
    {
        SchemaVersion = ShipRecord.CurrentVersion,
        Id            = ship.Id,
        HullTypeId    = ship.HullTypeId,
        Name          = ship.Name,
        CreatedDate   = ship.CreatedDate,
        Components    = [],                    // stub — not yet mapped
        HullElements  = [],                    // stub — hull elements not yet implemented
        PanelLayout   = new CockpitLayoutRecord(),
        Consumables   = new ConsumablesRecord(),
    };
}
