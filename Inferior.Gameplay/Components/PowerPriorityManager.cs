using Inferior.Core;
using Inferior.Core.DataBus;

namespace Inferior.Gameplay.Components;

/// <summary>
/// Optional power priority manager for a bus.
/// When installed, it distributes bus power to registered consumers in priority order,
/// starving lower-priority consumers when bus power is insufficient.
///
/// Without a manager, consumers draw from the bus in tick order (first-come first-served).
/// With a manager, priority is enforced regardless of tick order.
///
/// The manager itself runs without external power — internal low-power circuit,
/// not part of the main simulation.
///
/// Settings (priority assignments) are saved with the ship.
/// </summary>
public sealed class PowerPriorityManager : ShipComponent
{
    private readonly record struct Entry(
        string          Name,
        Func<double>    DemandWatts,
        Action<double>  Deliver,
        PowerPriority   Priority);

    private readonly List<Entry> _consumers = [];
    private PowerBus? _bus;

    public PowerPriorityManager(string name = "PowerManager")
    {
        Name = name;
    }

    /// <summary>Attach this manager to a bus. Must be called before Tick.</summary>
    public void AttachToBus(PowerBus bus) => _bus = bus;

    /// <summary>
    /// Register a consumer with a priority.
    /// demandWatts  — called each tick to get the consumer's current power demand.
    /// deliver      — called each tick with the actual watts delivered (may be less than demand).
    /// </summary>
    public void Register(string name, Func<double> demandWatts, Action<double> deliver, PowerPriority priority)
        => _consumers.Add(new Entry(name, demandWatts, deliver, priority));

    public override void Tick(double dt)
    {
        if (_bus != null)
            DistributePower(dt);

        TickSensors();
    }

    private void DistributePower(double dt)
    {
        // Serve consumers in ascending priority order (Critical=0 first, Low=3 last)
        foreach (var entry in _consumers.OrderBy(e => (int)e.Priority))
        {
            double demanded  = entry.DemandWatts();
            if (demanded <= 0.0) { entry.Deliver(0.0); continue; }

            double delivered = _bus!.Draw(demanded, dt);
            entry.Deliver(delivered);

            if (delivered < demanded * 0.95)
                DataBus.System.Publish(Topics.System.All,
                    $"[POWER] {entry.Name} starved — {delivered / 1e6:F2} / {demanded / 1e6:F2} MW");
        }
    }
}
