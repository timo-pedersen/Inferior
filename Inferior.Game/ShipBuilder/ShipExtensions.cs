using Inferior.Gameplay.Cockpit;
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
        Cockpit = ship.Cockpit is null
            ? null
            : new InstalledCockpitRecord
            {
                MountId = ship.Cockpit.MountId,
                DefinitionId = ship.Cockpit.DefinitionId,
                InstallationRotation = ToRecord(ship.Cockpit.InstallationRotation),
                CanopyLightsOn = ship.Cockpit.CanopyLightsOn,
                CockpitLightsOn = ship.Cockpit.CockpitLightsOn,
            },
        Components    = [],                    // stub — not yet mapped
        HullElements  = [],                    // stub — hull elements not yet implemented
        PanelLayout   = new CockpitLayoutRecord(),
        Consumables   = new ConsumablesRecord(),
    };

    private static CockpitRotationStepRecord ToRecord(CockpitRotationStep rotation)
        => rotation switch
        {
            CockpitRotationStep.Deg0 => CockpitRotationStepRecord.Deg0,
            CockpitRotationStep.Deg90 => CockpitRotationStepRecord.Deg90,
            CockpitRotationStep.Deg180 => CockpitRotationStepRecord.Deg180,
            CockpitRotationStep.Deg270 => CockpitRotationStepRecord.Deg270,
            _ => throw new ArgumentOutOfRangeException(nameof(rotation)),
        };
}
