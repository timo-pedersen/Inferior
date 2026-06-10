using Inferior.Core;
using Inferior.Core.DataBus;
using Inferior.Gameplay.Components.Power;

namespace Inferior.Gameplay.Components;

/// <summary>
/// Power connector — a cable between a power bus and a single consumer.
/// Limits throughput to MaxPower (wire-gauge ceiling in watts) and publishes
/// the actual flow as a sensor.
///
/// Call Connect() before installing to wire up demand/delivery delegates through
/// the priority manager. The connector intercepts the manager registration so
/// power is capped at MaxPower before reaching the downstream component.
///
/// Sensors published every tick (once Running):
///   {Name}.Flow  — watts currently flowing through this connector
/// </summary>
public sealed class ConnectorComponent : ShipComponent
{
    /// <summary>ID of the bus this connector draws from.</summary>
    public string FromBusId { get; init; } = "";

    /// <summary>ID of the component this connector feeds.</summary>
    public string ToComponentId { get; init; } = "";

    /// <summary>Maximum throughput (watts). Wire-gauge ceiling.</summary>
    public double MaxPower { get; init; }

    /// <summary>Watts flowing through this connector this tick.</summary>
    public double FlowWatts { get; private set; }

    public ConnectorComponent(string name, string fromBusId, string toComponentId, double maxPower)
    {
        Name             = name;
        FromBusId        = fromBusId;
        ToComponentId    = toComponentId;
        MaxPower         = maxPower;
        PowerConsumption = 0.0;
        StartupTimer     = 0.0;
        RegisterSensors();
    }

    /// <summary>
    /// Wire the connector between a priority manager and a downstream consumer.
    /// The connector caps the demand at MaxPower and forwards actual delivered watts
    /// to the consumer. Call before ship.Install().
    /// </summary>
    public void Connect(PowerPriorityManager manager,
                        Func<double>    demandFunc,
                        Action<double>  deliverFunc,
                        PowerPriority   priority = PowerPriority.Normal)
    {
        manager.Register(
            Name,
            () => Math.Min(demandFunc(), MaxPower),
            w => { FlowWatts = w; deliverFunc(w); },
            priority);
    }

    protected override void OnInitializationComplete()
    {
        PublishSensorRanges();
        DataBus.System.Publish(Topics.System.All,
            $"{Name}: online — {MaxPower / 1e6:F0} MW connector");
    }

    protected override void OnTick(double dt)
    {
        TickSensors();
    }

    private void RegisterSensors()
    {
        _sensors.Add(new ComponentSensor(
            $"{Name}.Flow",
            () => FlowWatts,
            safeRange:  new RangeValue(0, MaxPower),
            totalRange: new RangeValue(0, MaxPower)));
    }
}
