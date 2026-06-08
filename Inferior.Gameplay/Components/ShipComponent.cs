using Inferior.Core.DataBus;

namespace Inferior.Gameplay.Components;

/// <summary>
/// Common base for every installed ship component — power-bus consumers, sensors,
/// shields, drives, gyro, and battery-backed components alike.
///
/// Battery-backed components (FlyabilityMonitor, life support, cockpit) derive from this
/// but set PowerConsumption = 0 and are not registered with PowerPriorityManager.
/// </summary>
public abstract class ShipComponent
{
    // ── Identity ──────────────────────────────────────────────────────────────
    /// <summary>Used in bus messages and diagnostics. e.g. "MainEngine", "TopShield".</summary>
    public string Name { get; init; } = string.Empty;

    // ── Lifecycle ──────────────────────────────────────────────────────────────
    public ComponentStatus Status { get; protected set; } = ComponentStatus.Stopped;

    /// <summary>
    /// Seconds from power-on until the component transitions to Started.
    /// 0.0 = instant. Non-zero values produce the emergent cold-start sequence.
    /// </summary>
    public double StartupTimer { get; init; } = 0.0;

    // ── Power ──────────────────────────────────────────────────────────────────
    /// <summary>
    /// Local energy buffer in joules. Absorbs brief supply interruptions without
    /// the component noticing. Size generously for ArtificialGravity; minimally for a sensor.
    /// </summary>
    public double InputCapacitorMaxJ    { get; init; } = 0.0;
    public double InputCapacitorChargeJ { get; protected set; }

    /// <summary>
    /// Nominal peak draw in watts. Used by FlyabilityMonitor overspec checks.
    /// Reflects maximum demand, not instantaneous — actual draw varies with load.
    /// </summary>
    public double PowerConsumption { get; init; }  // watts

    // ── Health ────────────────────────────────────────────────────────────────
    public double Efficiency { get; protected set; } = 1.0;  // 0.0–1.0
    public double Damage     { get; protected set; } = 0.0;  // 0.0 = pristine, 1.0 = destroyed

    /// <summary>
    /// Accumulate damage from excess heat. Called by TickDamage when ThermalNode.IsFailure.
    /// excessHeatJ: joules above MaxHeatJ — used to scale the accumulation rate.
    /// Virtual — subclasses may override to tune the rate per component type.
    /// </summary>
    public virtual void AccumulateDamage(double excessHeatJ, double dt)
        => Damage = Math.Min(1.0, Damage + excessHeatJ * dt * 0.0001);  // rate TBD

    // ── Heat ──────────────────────────────────────────────────────────────────
    /// <summary>
    /// Null for battery-backed components that generate no heat.
    /// Heated components construct this in their own constructor, passing the appropriate
    /// HeatCapacity (J/K) and MaxHeatJ (joules). Those values live on the node.
    /// </summary>
    public ThermalNode? ThermalNode { get; protected init; }

    // ── Sensors ───────────────────────────────────────────────────────────────
    public IReadOnlyList<ComponentSensor> Sensors => _sensors;
    protected readonly List<ComponentSensor> _sensors = new();

    // ── Tick ──────────────────────────────────────────────────────────────────
    public virtual void Tick(double dt) { }

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
}
