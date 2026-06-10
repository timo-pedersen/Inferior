namespace Inferior.Gameplay.Components;

/// <summary>
/// Power connector — a cable between a power bus and a component.
/// Limits throughput to MaxPower (wire-gauge ceiling in watts).
///
/// This is a configuration descriptor; the power manager uses FromBusId /
/// ToComponentId when building the distribution graph. The connector itself
/// is battery-backed and generates no heat.
/// </summary>
public sealed class ConnectorComponent : ShipComponent
{
    /// <summary>ID of the bus this connector draws from.</summary>
    public string FromBusId { get; init; } = "";

    /// <summary>ID of the component this connector feeds.</summary>
    public string ToComponentId { get; init; } = "";

    /// <summary>Maximum throughput (watts). Wire-gauge ceiling — distinct from stored energy.</summary>
    public double MaxPower { get; init; }

    public ConnectorComponent(string name, string fromBusId, string toComponentId, double maxPower)
    {
        Name            = name;
        FromBusId       = fromBusId;
        ToComponentId   = toComponentId;
        MaxPower        = maxPower;
        PowerConsumption = 0.0;  // battery-backed, no bus draw
        StartupTimer    = 0.0;   // instant
    }
}
