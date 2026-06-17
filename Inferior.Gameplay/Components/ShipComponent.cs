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
    public ComponentStatus Status { get; protected set; } = ComponentStatus.PowerOff;

    /// <summary>
    /// The component's power switch. Setting true requests startup; false shuts it down.
    /// A component can be switched on before bus power is available — it will wait in
    /// PowerOn status until NotifyPowerAvailable() is called by the power system.
    /// </summary>
    private bool _powerOn;
    public bool PowerOn
    {
        get => _powerOn;
        set
        {
            if (_powerOn == value) return;
            _powerOn = value;

            if (_powerOn)
            {
                // Can only go to PowerOn if powered off 
                if (Status == ComponentStatus.PowerOff)
                {
                    Status = ComponentStatus.PowerOn;
                    OnPowerOn();
                }
            }
            else
            {
                _startupElapsed = 0.0;
                Status = ComponentStatus.PowerOff;
                OnPowerOff();
            }
        }
    }

    /// <summary>
    /// Seconds from power-on until the component transitions to Started.
    /// 0.0 = instant startup — no sequence messages, goes directly to Started.
    /// Non-zero produces the full cold-start sequence with status messages.
    /// Set to double.PositiveInfinity for self-managed startup via CompleteInitialization().
    /// </summary>
    public double StartupTimer { get; init; } = 0.0;

    private double _startupElapsed;

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

    // ── Events ────────────────────────────────────────────────────────────────
    /// <summary>
    /// Fired once when the component transitions to Started.
    /// Subscribe here for physics responses — e.g. a shield-start roll impulse.
    /// </summary>
    public event Action? Started;

    // ── Power system interface ────────────────────────────────────────────────
    /// <summary>
    /// Called by the power system when bus power becomes available.
    /// Ignored if the component is switched off or already running.
    /// Components with StartupTimer == 0 go directly to Started (no sequence messages).
    /// Components with StartupTimer > 0 begin the cold-start sequence via Tick().
    /// </summary>
    public void NotifyPowerAvailable()
    {
        if (!_powerOn) return;
        if (Status is ComponentStatus.Initializing or ComponentStatus.Running) return;

        if (StartupTimer <= 0.0)
        {
            Status = ComponentStatus.Running;
            OnInitializationComplete();
            Started?.Invoke();
        }
        else
        {
            Status = ComponentStatus.PowerOn;
        }
    }

    // ── Tick ──────────────────────────────────────────────────────────────────
    /// <summary>
    /// Drives the startup state machine, then delegates to OnTick once Started.
    /// Not virtual — override OnTick instead.
    /// </summary>
    public void Tick(double dt)
    {
        TickStartupSequence(dt);

        if (Status == ComponentStatus.Running)
            OnTick(dt);
        else if (Status == ComponentStatus.PowerOff)
            OnPowerOffTick(dt);
    }

    protected virtual void OnTick(double dt) { }

    /// <summary>
    /// Called every tick while Status == PowerOff. Override for post-shutdown behaviour
    /// such as capacitor drain or thermal dissipation. Default no-ops.
    /// </summary>
    protected virtual void OnPowerOffTick(double dt) { }

    // ── Lifecycle hooks ───────────────────────────────────────────────────────
    /// <summary>Called once when the startup timer begins running (StartupTimer > 0 only).</summary>
    protected virtual void OnInitializationStarted()
        => DataBus.System.Publish(Topics.System.All, new($"{Name}: initializing"));

    /// <summary>
    /// Called every tick while Status == Initializing. Override to run warmup physics
    /// and post progress messages. Call CompleteInitialization() here when ready for
    /// self-managed components (StartupTimer = double.PositiveInfinity).
    /// </summary>
    protected virtual void OnInitializingTick(double dt) { }

    /// <summary>
    /// Called once when the component reaches Started. Publishes sensor ranges and
    /// the online message. Override to customise — call PublishSensorRanges() yourself
    /// in place of base.OnInitializationComplete().
    /// </summary>
    protected virtual void OnInitializationComplete()
    {
        PublishSensorRanges();
        DataBus.System.Publish(Topics.System.All, new($"{Name}: online"));
    }

    /// <summary>
    /// Called once when the component is powered off from any running state.
    /// Override to add component-specific shutdown behaviour (drain capacitor, etc.).
    /// Base publishes the offline message.
    /// </summary>
    protected virtual void OnPowerOff()
        => DataBus.System.Publish(Topics.System.All, new($"{Name}: offline"));

    /// <summary>
    /// Called once when the component is powered on.
    /// Override to add component-specific startup behaviour (charge capacitor, etc.).
    /// Base publishes the online message.
    /// </summary>
    protected virtual void OnPowerOn()
        => DataBus.System.Publish(Topics.System.All, new($"{Name}: powering up"));

    /// <summary>
    /// Transitions directly to Started from Initializing. For components that manage
    /// their own completion condition (e.g. a shield waiting for full capacitor charge)
    /// rather than relying on StartupTimer. Safe to call from OnInitializingTick.
    /// </summary>
    protected void CompleteInitialization()
    {
        if (Status != ComponentStatus.Initializing) return;
        Status = ComponentStatus.Running;
        OnInitializationComplete();
        Started?.Invoke();
    }

    // ── Protected helpers ─────────────────────────────────────────────────────
    protected void PublishSensorRanges()
    {
        foreach (var s in _sensors)
            s.PublishRanges();
    }

    protected void TickSensors()
    {
        foreach (var s in _sensors)
            s.Tick();
    }

    // ── Private ───────────────────────────────────────────────────────────────
    private void TickStartupSequence(double dt)
    {
        switch (Status)
        {
            case ComponentStatus.PowerOn:
                Status = ComponentStatus.Initializing;
                OnInitializationStarted();
                break;

            case ComponentStatus.Initializing:
                _startupElapsed += dt;
                OnInitializingTick(dt);
                // Guard: OnInitializingTick may have called CompleteInitialization() already
                if (Status == ComponentStatus.Initializing && _startupElapsed >= StartupTimer)
                {
                    Status = ComponentStatus.Running;
                    OnInitializationComplete();
                    Started?.Invoke();
                }
                break;
        }
    }
}
