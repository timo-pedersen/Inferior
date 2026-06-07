using Inferior.Core.DataBus;
using Inferior.Gameplay.Components.Power;

namespace Inferior.Gameplay.Components;

/// <summary>
/// Base for all installed ship components. Handles damage, efficiency, thermal state,
/// and the list of sensors that publish to DataBus each tick.
///
/// Port model (flat, not hierarchy):
///   - Reactor:   no InputCapacitor, has OutputCapacitor
///   - Consumer:  has InputCapacitor, no OutputCapacitor
///   - Converter: has both
/// Concrete subclasses set whichever ports they need.
/// </summary>
public abstract class ShipComponent
{
    // ── Identity ──────────────────────────────────────────────────────────────
    public string Name { get; init; } = "";

    // ── Damage and efficiency ─────────────────────────────────────────────────
    /// <summary>0 = pristine, 1 = destroyed.</summary>
    public double Damage         { get; protected set; }
    public double BaseEfficiency { get; init; } = 1.0;
    /// <summary>Efficiency drops linearly with damage — at 100% damage, 60% of efficiency is lost.</summary>
    public double CurrentEfficiency => BaseEfficiency * (1.0 - Damage * 0.6);

    // ── Thermal ───────────────────────────────────────────────────────────────
    /// <summary>J/K — determines how quickly the component heats up under a given heat load.</summary>
    public double ThermalMassJ   { get; init; } = 10_000.0;
    /// <summary>Temperature above ambient at which damage begins to accumulate.</summary>
    public double HeatToleranceK { get; init; } = 600.0;
    /// <summary>Fraction of heat shed per second (passive cooling to ambient).</summary>
    public double CoolingRate    { get; init; } = 0.05;
    /// <summary>Current temperature above ambient, Kelvin.</summary>
    public double Temperature    { get; private set; }

    // ── Power ports (set by concrete subclass) ────────────────────────────────
    public PowerCapacitor? InputCapacitor  { get; protected init; }
    public PowerCapacitor? OutputCapacitor { get; protected init; }

    // ── Sensors ───────────────────────────────────────────────────────────────
    public IReadOnlyList<ComponentSensor> Sensors => _sensors;
    protected readonly List<ComponentSensor> _sensors = new();

    // ── Tick ──────────────────────────────────────────────────────────────────

    /// <summary>Advance this component by one sim tick. Subclass must call TickSensors() before returning.</summary>
    public abstract void Tick(double dt);

    // ── Startup ───────────────────────────────────────────────────────────────

    /// <summary>Publish all sensor ranges and announce presence on the system bus.</summary>
    public virtual void OnStartup()
    {
        foreach (var s in _sensors)
            s.PublishRanges();
        DataBus.System.Publish(Topics.System.All, $"{Name}: online");
    }

    // ── Protected helpers ─────────────────────────────────────────────────────

    protected void TickSensors()
    {
        foreach (var s in _sensors)
            s.Tick();
    }

    /// <summary>
    /// Apply a heat load (watts) for dt seconds. Includes passive cooling and damage
    /// accumulation when temperature exceeds tolerance.
    /// </summary>
    protected void ApplyHeat(double watts, double dt)
    {
        Temperature += watts * dt / ThermalMassJ;
        Temperature -= Temperature * CoolingRate * dt;
        Temperature  = Math.Max(0.0, Temperature);

        if (Temperature > HeatToleranceK)
            Damage = Math.Min(1.0, Damage + (Temperature - HeatToleranceK) * dt * 0.0001);
    }
}
