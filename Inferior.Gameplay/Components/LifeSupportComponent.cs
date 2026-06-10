using Inferior.Core.DataBus;

namespace Inferior.Gameplay.Components;

/// <summary>
/// Life support system. Maintains internal pressure, temperature, and oxygen levels.
/// Battery-backed — lasts weeks/months in-game without external power.
/// Reports to the system bus if any parameter drifts outside safe range.
///
/// All values are stubs. Real life support simulation (CO2 scrubbing, waste heat,
/// emergency reserves) is deferred until the atmosphere / landing systems are designed.
///
/// Sensors published:
///   {Name}.Pressure    — internal pressure (Pa)
///   {Name}.Temperature — internal temperature (K)
///   {Name}.Oxygen      — oxygen fraction 0..1
/// </summary>
public sealed class LifeSupportComponent : ShipComponent
{
    private const double NominalPressure    = 101_325.0;  // 1 atm in Pa
    private const double NominalTemperature = 295.0;      // ~22°C in K
    private const double NominalOxygen      = 0.21;       // 21% O₂

    public double InternalPressure    { get; private set; } = NominalPressure;
    public double InternalTemperature { get; private set; } = NominalTemperature;
    public double OxygenFraction      { get; private set; } = NominalOxygen;

    public LifeSupportComponent(string name)
    {
        Name             = name;
        PowerConsumption = 0.0;  // battery-backed
        StartupTimer     = 0.0;

        RegisterSensors();
    }

    protected override void OnTick(double dt)
    {
        // Stub: nominal values held constant. Future: model CO2 buildup, thermal exchange.
        TickSensors();
    }

    private void RegisterSensors()
    {
        _sensors.Add(new ComponentSensor(
            $"{Name}.{Topics.LifeSupport.Pressure}",
            () => InternalPressure,
            safeRange:  new RangeValue(70_000, 120_000),
            totalRange: new RangeValue(0, 200_000)));

        _sensors.Add(new ComponentSensor(
            $"{Name}.{Topics.LifeSupport.Temperature}",
            () => InternalTemperature,
            safeRange:  new RangeValue(290, 303),
            totalRange: new RangeValue(250, 350)));

        _sensors.Add(new ComponentSensor(
            $"{Name}.{Topics.LifeSupport.Oxygen}",
            () => OxygenFraction,
            safeRange:  new RangeValue(0.18, 0.25),
            totalRange: new RangeValue(0.0,  1.0)));
    }
}
