using Inferior.Core;
using Inferior.Core.DataBus;
using Inferior.Gameplay.Components.Power;

namespace Inferior.Gameplay.Components;

/// <summary>
/// Main propulsion engine. Draws from the power bus; produces forward/reverse thrust,
/// optional downward thrust (landing / pitch assist), and optionally AlphaRed power
/// for a gyro when paired with a second engine.
///
/// Sensors published every tick (once Running):
///   {Name}.Throttle      — current throttle (-1..1)
///   {Name}.Thrust        — current forward thrust (newtons)
///   {Name}.DownThrust    — current downward thrust (newtons)
///   {Name}.Temperature   — thermal node temperature (K)
///   {Name}.Damage        — component damage (0..1)
///
/// Commands accepted:
///   {Name}.Throttle.Set     value -1..1
///   {Name}.DownThrust.Set   value  0..1
/// </summary>
public sealed class EngineComponent : ShipComponent
{
    protected override IReadOnlyList<string> DeviceCommandTopics =>
        [$"{Name}.Throttle.Set", $"{Name}.DownThrust.Set"];

    // Configuration
    public double MaxPower            { get; init; }   // watts
    public double MaxDownThrust       { get; init; }   // newtons
    public bool   CanGenerateAlphaRed { get; init; }
    public double AlphaRedPower       { get; init; }   // watts when paired

    // MaxThrust derived from power; stub ratio replaced when engine physics exists
    private const double NewtonsPerWatt = 0.002;
    public double MaxThrust => MaxPower * NewtonsPerWatt * Efficiency;

    // Current state
    public double Throttle             { get; private set; }
    public double DownThrustFraction   { get; private set; }
    public double CurrentThrust        { get; private set; }
    public double CurrentDownThrust    { get; private set; }
    public double CurrentAlphaRedWatts { get; private set; }

    private double _deliveredWatts;

    public EngineComponent(string name,
                           double maxPower,
                           double maxDownThrust       = 0.0,
                           bool   canGenerateAlphaRed = false,
                           double alphaRedPower       = 0.0,
                           double heatCapacity        = 150_000.0,   // J/K
                           double maxHeatJ            = 300_000_000.0)
    {
        Name                = name;
        MaxPower            = maxPower;
        MaxDownThrust       = maxDownThrust;
        PowerConsumption    = maxPower;
        CanGenerateAlphaRed = canGenerateAlphaRed;
        AlphaRedPower       = alphaRedPower;
        ThermalNode         = new ThermalNode(heatCapacity, maxHeatJ);
        StartupTimer        = 3.0;  // cold-start sequence

        RegisterSensors();
        RegisterCommands();
    }

    public void RegisterWithPowerManager(PowerPriorityManager manager,
                                         PowerPriority priority = PowerPriority.High)
        => manager.Register(Name, DemandWatts, w => _deliveredWatts = w, priority);

    // ── Tick ──────────────────────────────────────────────────────────────────

    protected override void OnTick(double dt)
    {
        double demand   = MaxPower * (Math.Abs(Throttle) + DownThrustFraction * 0.3);
        double fraction = demand > 0.0 ? Math.Min(_deliveredWatts, demand) / demand : 0.0;

        CurrentThrust          = MaxThrust * Math.Abs(Throttle) * fraction;
        CurrentDownThrust      = MaxDownThrust * DownThrustFraction * fraction;
        CurrentAlphaRedWatts   = CanGenerateAlphaRed ? AlphaRedPower * fraction : 0.0;

        if (ThermalNode != null)
            ThermalNode.Update(_deliveredWatts * (1.0 - EffectiveEfficiency), dt);

        TickSensors();
    }

    protected override void OnInitializationComplete()
    {
        PublishTelemetryInfo();
        DataBus.SystemMessages.Publish(Topics.System.All,
            new($"{Name}: online — {MaxPower / 1e6:F0} MW, {MaxThrust / 1e3:F0} kN max thrust"));
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private double DemandWatts()
        => Status == ComponentStatus.Running
            ? MaxPower * (Math.Abs(Throttle) + DownThrustFraction * 0.3)
            : 0.0;

    private void RegisterSensors()
    {
        double maxTempK  = ThermalNode!.MaxHeatJ / ThermalNode.HeatCapacity;
        double safeTempK = maxTempK * 0.7;

        _sensors.Add(new ComponentSensor(
            $"{Name}.{Topics.Engine.Throttle}",
            () => Throttle,
            safeRange:  new RangeValue(-1.0, 1.0),
            totalRange: new RangeValue(-1.0, 1.0),
            quantity: PhysicalQuantity.Dimensionless));

        _sensors.Add(new ComponentSensor(
            $"{Name}.{Topics.Engine.Thrust}",
            () => CurrentThrust,
            safeRange:  new RangeValue(0, MaxThrust * 0.9),
            totalRange: new RangeValue(0, MaxThrust),
            quantity: PhysicalQuantity.Force));

        _sensors.Add(new ComponentSensor(
            $"{Name}.Temperature",
            () => ThermalNode?.Temperature ?? 0.0,
            safeRange:  new RangeValue(0, safeTempK),
            totalRange: new RangeValue(0, maxTempK),
            quantity: PhysicalQuantity.Temperature));

        _sensors.Add(new ComponentSensor(
            $"{Name}.{Topics.Engine.Damage}",
            () => Damage,
            safeRange:  new RangeValue(0, 0.2),
            totalRange: new RangeValue(0, 1.0),
            quantity: PhysicalQuantity.NormalizedRatio));
    }

    private void RegisterCommands()
    {
        RegisterCommand($"{Name}.Throttle.Set",
            cmd => Throttle = Math.Clamp(cmd.Value, -1.0, 1.0));
        RegisterCommand($"{Name}.DownThrust.Set",
            cmd => DownThrustFraction = Math.Clamp(cmd.Value, 0.0, 1.0));
    }
}
