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
    public double MaxShieldJ  { get; }  // maximum shield energy (joules)
    public double ChargeRateW { get; }  // maximum charge rate, also peak power demand (watts)

    private readonly PowerCapacitor _capacitor;
    private double _deliveredWatts;
    private double _progressCooldown;

    public ShieldComponent(string name, double maxShieldJ, double chargeRateW)
    {
        Name             = name;
        MaxShieldJ       = maxShieldJ;
        ChargeRateW      = chargeRateW;
        PowerConsumption = chargeRateW;
        StartupTimer     = double.PositiveInfinity;  // self-managed — completes when capacitor is full
        _capacitor       = new PowerCapacitor(maxShieldJ);

        RegisterSensors();
    }

    // ── Power ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Register as a power consumer with the priority manager.
    /// Call once during ship construction before startup.
    /// </summary>
    public void RegisterWithPowerManager(PowerPriorityManager manager,
                                         PowerPriority priority = PowerPriority.Normal)
        => manager.Register(Name, DemandWatts, w => _deliveredWatts = w, priority);

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

    // ── Startup hooks ─────────────────────────────────────────────────────────

    protected override void OnInitializationStarted()
    {
        DataBus.System.Publish(Topics.System.All, $"{Name}: capacitor at 0% — charging");
        _progressCooldown = 5.0;
    }

    protected override void OnInitializingTick(double dt)
    {
        _capacitor.Charge(_deliveredWatts, dt);

        _progressCooldown -= dt;
        if (_progressCooldown <= 0.0)
        {
            DataBus.System.Publish(Topics.System.All,
                $"{Name}: capacitor at {_capacitor.FillFraction:P0} — charging");
            _progressCooldown = 5.0;
        }

        if (_capacitor.FillFraction >= 1.0)
            CompleteInitialization();
    }

    protected override void OnInitializationComplete()
    {
        PublishSensorRanges();
        DataBus.System.Publish(Topics.System.All,
            $"{Name}: online — {MaxShieldJ / 1e6:F1} MJ shield ready");
    }

    // ── Running ───────────────────────────────────────────────────────────────

    protected override void OnTick(double dt)
    {
        _capacitor.Charge(_deliveredWatts, dt);
        TickSensors();
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private double DemandWatts()
    {
        return Status is ComponentStatus.Initializing or ComponentStatus.Running
            && _capacitor.FillFraction < 1.0
                ? ChargeRateW
                : 0.0;
    }

    private void RegisterSensors()
    {
        _sensors.Add(new ComponentSensor(
            $"{Name}.{Topics.Shield.Capacitor}",
            () => _capacitor.FillFraction,
            safeRange:  new RangeValue(0.5, 1.0),
            totalRange: new RangeValue(0.0, 1.0)));

        _sensors.Add(new ComponentSensor(
            $"{Name}.{Topics.Shield.DamagePercent}",
            () => Damage,
            safeRange:  new RangeValue(0.0, 0.2),
            totalRange: new RangeValue(0.0, 1.0)));
    }
}
