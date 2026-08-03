using Inferior.Core.DataBus;

namespace Inferior.Gameplay.Components;

/// <summary>
/// Artificial gravity and inertial dampening system.
///
/// Requires a converter supplying H-sw sub-band power. Has no manual off switch in
/// game terms — it is registered at Critical priority so it is the last system starved.
/// Its large InputCapacitor absorbs brief bus outages silently.
///
/// Sensors published:
///   {Name}.Power — current draw (watts; 0 when not Running)
/// </summary>
public sealed class ArtificialGravityComponent : ShipComponent
{
    public ArtificialGravityComponent(string name, double powerWatts)
    {
        Name               = name;
        PowerConsumption   = powerWatts;
        InputCapacitorMaxJ = powerWatts * 10.0;  // 10-second buffer — survives brief outages
        StartupTimer       = 0.0;

        RegisterSensors();
    }

    protected override void OnTick(double dt) => TickSensors();

    private void RegisterSensors()
    {
        _sensors.Add(new ComponentSensor(
            $"{Name}.{Topics.ArtificialGravity.Power}",
            () => Status == ComponentStatus.Running ? PowerConsumption : 0.0,
            safeRange:  new RangeValue(0, PowerConsumption),
            totalRange: new RangeValue(0, PowerConsumption),
            quantity: PhysicalQuantity.Power));
    }
}
