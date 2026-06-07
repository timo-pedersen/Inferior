using Inferior.Core.DataBus;

namespace Inferior.Gameplay.Components;

/// <summary>
/// Primary power source. Generates MW from fuel at a throttleable output level.
/// Has an output capacitor that buffers generated power before distribution.
///
/// Sensors published every tick (in MW / MJ / 0–1 fractions):
///   {Name}.Output       — current power output (MW)
///   {Name}.Capacitor    — output capacitor fill level (0–1)
///   {Name}.Temperature  — heat above ambient (K)
///   {Name}.Efficiency   — current efficiency (0–1)
///   {Name}.Damage       — damage level (0–1)
///
/// Commands accepted:
///   {Name}.Throttle.Set  value 0–1  — set output throttle
/// </summary>
public sealed class PowerReactor : ShipComponent
{
    public double MaxOutputW     { get; }
    public double Throttle       { get; private set; } = 1.0;
    public double CurrentOutputW { get; private set; }

    public PowerReactor(string name, double maxOutputW, double outputCapacitorJ,
                        double baseEfficiency = 0.85)
    {
        Name            = name;
        MaxOutputW      = maxOutputW;
        BaseEfficiency  = baseEfficiency;
        HeatToleranceK  = 1_500.0;
        ThermalMassJ    = 80_000.0;
        CoolingRate     = 0.03;
        OutputCapacitor = new PowerCapacitor(outputCapacitorJ);

        RegisterSensors();
        RegisterCommands();
    }

    public override void Tick(double dt)
    {
        double target  = MaxOutputW * Throttle;
        CurrentOutputW = target * CurrentEfficiency;

        OutputCapacitor!.Charge(CurrentOutputW, dt);

        double heatW = CurrentOutputW * (1.0 - CurrentEfficiency);
        ApplyHeat(heatW, dt);

        TickSensors();
    }

    public override void OnStartup()
    {
        base.OnStartup();
        DataBus.System.Publish(Topics.System.All,
            $"{Name}: {MaxOutputW / 1e6:F0} MW reactor, " +
            $"{OutputCapacitor!.MaxJ / 1e6:F1} MJ capacitor");
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private void RegisterSensors()
    {
        double maxMW = MaxOutputW / 1e6;

        _sensors.Add(new ComponentSensor(
            $"{Name}.Output",
            () => CurrentOutputW / 1e6,
            safeRange:  new RangeValue(0, maxMW * 0.85),
            totalRange: new RangeValue(0, maxMW)));

        _sensors.Add(new ComponentSensor(
            $"{Name}.Capacitor",
            () => OutputCapacitor!.Level,
            safeRange:  new RangeValue(0.1, 1.0),
            totalRange: new RangeValue(0.0, 1.0)));

        _sensors.Add(new ComponentSensor(
            $"{Name}.Temperature",
            () => Temperature,
            safeRange:  new RangeValue(0, HeatToleranceK * 0.8),
            totalRange: new RangeValue(0, HeatToleranceK)));

        _sensors.Add(new ComponentSensor(
            $"{Name}.Efficiency",
            () => CurrentEfficiency,
            safeRange:  new RangeValue(0.75, 1.0),
            totalRange: new RangeValue(0.0,  1.0)));

        _sensors.Add(new ComponentSensor(
            $"{Name}.Damage",
            () => Damage,
            safeRange:  new RangeValue(0.0, 0.2),
            totalRange: new RangeValue(0.0, 1.0)));
    }

    private void RegisterCommands()
    {
        CommandBus.Subscribe($"{Name}.Throttle.Set",
            cmd => Throttle = Math.Clamp(cmd.Value, 0.0, 1.0));
    }
}
