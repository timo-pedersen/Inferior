using Inferior.Core.DataBus;

namespace Inferior.Gameplay.Components.Power;

/// <summary>
/// Power distribution bus — a passive energy buffer between reactor and consumers.
/// Gets charged from an upstream source each tick; consumers call Draw() on demand.
/// The bus itself knows nothing about consumers or priorities.
/// Priority enforcement is handled by an optional PowerPriorityManager.
///
/// Throughput limits (MaxPower, MaxPowerPerConnection) are wire-gauge constraints —
/// how fast energy can flow — distinct from the Capacitor buffer (joules stored).
/// A bus can have a large buffer but a narrow wire gauge, or vice versa.
///
/// Install order matters: reactor must tick before bus (fills source cap),
/// bus must tick before consumers (fills bus cap from source).
///
/// Sensors published every tick:
///   {Name}.Level  — fill fraction 0–1
/// </summary>
public sealed class PowerBus : ShipComponent
{
    public override IReadOnlyList<ShipSystemMetricBinding> EngineeringMetrics =>
    [
        new(ShipSystemMetricRole.PowerFlow, $"{Name}.Consumption"),
        new(ShipSystemMetricRole.CapacitorFill, $"{Name}.Level"),
    ];
    // ── Throughput limits (watts — rate, not stored energy) ───────────────────
    /// <summary>Total throughput ceiling (watts).</summary>
    public double MaxPower              { get; init; }
    /// <summary>Per-connector ceiling (watts).</summary>
    public double MaxPowerPerConnection { get; init; }
    /// <summary>Maximum number of attached consumers.</summary>
    public int    MaxConnections        { get; init; }

    // ── Energy buffer ─────────────────────────────────────────────────────────
    public PowerCapacitor Capacitor { get; }

    /// <summary>Watts drawn from this bus during the previous tick (one-tick lag).</summary>
    public double DrawnWatts { get; private set; }

    private PowerCapacitor? _source;
    private double _drawnThisTick;

    public PowerBus(string name, double capacityJ,
                    double maxPower              = double.MaxValue,
                    double maxPowerPerConnection = double.MaxValue,
                    int    maxConnections        = int.MaxValue)
    {
        Name                  = name;
        Capacitor             = new PowerCapacitor(capacityJ);
        MaxPower              = maxPower;
        MaxPowerPerConnection = maxPowerPerConnection;
        MaxConnections        = maxConnections;
        RegisterSensors();
    }

    /// <summary>Connect the upstream source capacitor (e.g. reactor output capacitor).</summary>
    public void ConnectSource(PowerCapacitor source) => _source = source;

    /// <summary>
    /// Draw up to requestedWatts from this bus for dt seconds.
    /// Converts to joules internally (requestedWatts × dt), draws from the Capacitor,
    /// then returns actual watts delivered — may be less if depleted or capped by MaxPower.
    /// </summary>
    public double Draw(double requestedWatts, double dt)
    {
        if (Status != ComponentStatus.Running || dt <= 0.0) return 0.0;
        double cappedWatts = Math.Min(requestedWatts, MaxPower);
        double deliveredJ  = Capacitor.Draw(cappedWatts * dt);
        double deliveredW  = deliveredJ / dt;
        _drawnThisTick += deliveredW;
        return deliveredW;
    }

    protected override void OnTick(double dt)
    {
        // Snapshot previous tick's draw, then reset accumulator for this tick.
        // Consumers draw via PowerPriorityManager.Tick() which runs after this,
        // so DrawnWatts always reflects the previous tick (one-tick lag at 60 Hz).
        DrawnWatts     = _drawnThisTick;
        _drawnThisTick = 0.0;

        if (_source != null && dt > 0.0)
        {
            // Pull from source up to the wire-gauge limit (MaxPower) and available space
            double spaceJ       = Capacitor.MaxJ - Capacitor.StoredJ;
            double maxTransferJ = MaxPower * dt;
            double requestedJ   = Math.Min(spaceJ, maxTransferJ);
            if (requestedJ > 0.0)
            {
                double deliveredJ = _source.Draw(requestedJ);
                Capacitor.Charge(deliveredJ / dt, dt);
            }
        }

        TickSensors();
    }

    protected override void OnPowerOffTick(double dt)
    {
        DrawnWatts = 0.0;
        _drawnThisTick = 0.0;
        TickSensors();
    }

    protected override void OnInitializationComplete()
    {
        PublishTelemetryInfo();
        DataBus.SystemMessages.Publish(Topics.System.All,
            new($"{Name}: online — {Capacitor.MaxJ / 1e6:F1} MJ bus"));
    }

    private void RegisterSensors()
    {
        _sensors.Add(new ComponentSensor(
            $"{Name}.Level",
            () => Capacitor.FillFraction,
            safeRange:  new RangeValue(0.2, 1.0),
            totalRange: new RangeValue(0.0, 1.0),
            quantity: PhysicalQuantity.NormalizedRatio));

        double maxW = MaxPower < 1e15 ? MaxPower : 1e9;  // sensible range if unset
        _sensors.Add(new ComponentSensor(
            $"{Name}.Consumption",
            () => DrawnWatts,
            safeRange:  new RangeValue(0, maxW),
            totalRange: new RangeValue(0, maxW),
            quantity: PhysicalQuantity.Power));
    }
}
