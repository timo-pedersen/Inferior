using Inferior.Core;
using Inferior.Core.DataBus;
using Inferior.Gameplay.Components.Power;

namespace Inferior.Gameplay.Components;

/// <summary>
/// Directed-energy shield. Absorbs incoming damage until the shield capacitor
/// is depleted. Recharges from the power bus.
///
/// Startup is self-managed (StartupTimer = ∞): the shield charges its capacitor
/// to full before going online, posting progress every 5 seconds. Any incoming
/// damage during startup is not absorbed — the shield is not yet active.
///
/// Power demand scales with charge state: full ChargeRateW while charging,
/// zero when the capacitor is full (no idle maintenance draw yet).
///
/// Sensors published every tick (once Started):
///   {Name}.Capacitor     — shield capacitor fill level (0–1)
///   {Name}.DamagePercent — component damage (0–1)
///
/// Register power demand before startup:
///   shield.RegisterWithPowerManager(manager, PowerPriority.Normal);
/// </summary>
public sealed class ShieldComponent : ShipComponent
{
    public double MaxShieldJ    { get; }  // maximum shield energy (joules)
    public double ChargeRateW   { get; }  // maximum charge rate, also peak power demand (watts)
    public double CapacitorFill => _capacitor.FillFraction;  // 0–1; readable by sim for slipstream guard

    private readonly PowerCapacitor _capacitor;
    private double _deliveredWatts;
    private double _progressCooldown;

    public ShieldComponent(string name, double maxShieldJ, double chargeRateW,
                           double heatCapacity = 3_000.0,
                           double maxHeatJ     = 1_800_000.0)
    {
        Name             = name;
        MaxShieldJ       = maxShieldJ;
        ChargeRateW      = chargeRateW;
        PowerConsumption = chargeRateW;
        StartupTimer     = double.PositiveInfinity;  // self-managed — completes when capacitor is full
        _capacitor       = new PowerCapacitor(maxShieldJ);
        ThermalNode      = new ThermalNode(heatCapacity, maxHeatJ);

        RegisterSensors();
    }

    // ── Power ─────────────────────────────────────────────────────────────────

    /// <summary>Current power demand in watts (0 when full or offline).</summary>
    public double DemandWatts() =>
        Status is ComponentStatus.Initializing or ComponentStatus.Running
        && _capacitor.FillFraction < 1.0
            ? ChargeRateW
            : 0.0;

    /// <summary>Deliver watts from the bus (called by connector or priority manager).</summary>
    public void ReceivePower(double watts) => _deliveredWatts = watts;

    /// <summary>
    /// Register as a power consumer directly with the priority manager (no connector).
    /// Prefer wiring via ConnectorComponent when a connector is installed.
    /// </summary>
    public void RegisterWithPowerManager(PowerPriorityManager manager,
                                         PowerPriority priority = PowerPriority.Normal)
        => manager.Register(Name, DemandWatts, ReceivePower, priority);

    // ── Combat ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Intercept incoming damage. Returns joules that bleed through to the hull.
    /// Absorbs nothing if the shield is not online (Started).
    /// </summary>
    public double AbsorbDamage(double incomingJ)
    {
        if (Status != ComponentStatus.Running) return incomingJ;
        double absorbed = _capacitor.Draw(incomingJ);
        return incomingJ - absorbed;
    }

    /// <summary>
    /// Drain a fraction of maximum shield capacity (0–1). Used for atmospheric depletion.
    /// Clamped: will not drain below zero.
    /// </summary>
    public void DrainCapacitor(double fraction)
        => _capacitor.Draw(System.Math.Max(0.0, fraction) * MaxShieldJ);

    /// <summary>
    /// Inject heat directly into the shield generator's thermal mass.
    /// Called by atmospheric depletion logic — heat proportional to energy drained.
    /// </summary>
    public void AddHeat(double joules)
        => ThermalNode?.AddHeatJ(joules);

    // ── Startup hooks ─────────────────────────────────────────────────────────

    protected override void OnInitializationStarted()
    {
        DataBus.SystemMessages.Publish(Topics.System.All,
            new($"{Name}: capacitor at {_capacitor.FillFraction:P0} — charging"));
        _progressCooldown = 5.0;
    }

    protected override void OnInitializingTick(double dt)
    {
        _capacitor.Charge(_deliveredWatts, dt);
        ThermalNode?.Update(_deliveredWatts * (1.0 - EffectiveEfficiency), dt);
        DataBus.ScalarTelemetry.Publish($"{Topics.Shield.Name}.{Topics.Shield.Capacitor}", _capacitor.FillFraction);

        if (_capacitor.FillFraction >= 1.0)
        {
            CompleteInitialization();
            return;
        }

        _progressCooldown -= dt;
        if (_progressCooldown <= 0.0)
        {
            DataBus.SystemMessages.Publish(Topics.System.All,
                new($"{Name}: capacitor at {_capacitor.FillFraction:P0} — charging"));
            _progressCooldown = 5.0;
        }
    }

    protected override void OnInitializationComplete()
    {
        PublishTelemetryInfo();
        DataBus.SystemMessages.Publish(Topics.System.All,
            new($"{Name}: online — {MaxShieldJ / 1e6:F1} MJ shield ready"));
    }

    // ── Running ───────────────────────────────────────────────────────────────

    protected override void OnTick(double dt)
    {
        _capacitor.Charge(_deliveredWatts, dt);
        ThermalNode?.Update(_deliveredWatts * (1.0 - EffectiveEfficiency), dt);
        TickSensors();
    }

    // ── Power-off drain ───────────────────────────────────────────────────────

    private const double DrainRateW = 1e6;  // 1 MW — drains a 5 MJ capacitor in ~5 seconds

    protected override void OnPowerOffTick(double dt)
    {
        if (_capacitor.StoredJ <= 0.0) return;
        _capacitor.Draw(DrainRateW * dt);
        TickSensors();
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private void RegisterSensors()
    {
        _sensors.Add(new ComponentSensor(
            $"{Name}.{Topics.Shield.Capacitor}",
            () => _capacitor.FillFraction,
            safeRange:  new RangeValue(0.5, 1.0),
            totalRange: new RangeValue(0.0, 1.0),
            quantity: PhysicalQuantity.NormalizedRatio));

        _sensors.Add(new ComponentSensor(
            $"{Name}.{Topics.Shield.DamagePercent}",
            () => Damage,
            safeRange:  new RangeValue(0.0, 0.2),
            totalRange: new RangeValue(0.0, 1.0),
            quantity: PhysicalQuantity.NormalizedRatio));

        if (ThermalNode != null)
        {
            double maxTempK  = ThermalNode.MaxHeatJ / ThermalNode.HeatCapacity;
            double safeTempK = maxTempK * 0.7;
            _sensors.Add(new ComponentSensor(
                $"{Name}.Temperature",
                () => ThermalNode.Temperature,
                safeRange:  new RangeValue(0, safeTempK),
                totalRange: new RangeValue(0, maxTempK),
                quantity: PhysicalQuantity.Temperature));
        }
    }
}
