using Inferior.Core.DataBus;
using Inferior.Gameplay.Components;
using SensorEnvironment = Inferior.Gameplay.SensorData.Environment;

namespace Inferior.Gameplay.Sensors;

/// <summary>
/// Replaceable, passive radiometer measuring incoming bolometric stellar heat irradiance.
/// It requires no main-bus power and is deliberately separate from both ionising-radiation
/// instruments and the command-triggered normalized solar-spectrum scan.
/// </summary>
public sealed class SolarHeatSensor : ShipComponent
{
    public const double ReportFrequencyHz = 4.0;
    private const double ReportIntervalSeconds = 1.0 / ReportFrequencyHz;
    private double _timeSinceReport = ReportIntervalSeconds;

    public override bool CanSetPower => false;

    public override IReadOnlyList<ShipSystemMetricBinding> EngineeringMetrics =>
        [new(ShipSystemMetricRole.HeatIrradiance, $"{Name}.{Topics.SolarHeat.Irradiance}")];

    public SolarHeatSensor(string name = "SolarHeatSensor")
    {
        Name = name;
        PowerConsumption = 0.0;
        StartupTimer = 0.0;

        _sensors.Add(new ComponentSensor(
            $"{Name}.{Topics.SolarHeat.Irradiance}",
            () => SensorEnvironment.SolarHeatIrradiance,
            safeRange: new RangeValue(0.0, 2_000.0),
            totalRange: new RangeValue(0.0, 1e25),
            quantity: PhysicalQuantity.Irradiance,
            publication: new PublicationInfo(PublicationMode.Periodic, ReportFrequencyHz)));
    }

    protected override void OnTick(double dt)
    {
        _timeSinceReport += Math.Max(0.0, dt);
        if (_timeSinceReport < ReportIntervalSeconds)
            return;

        _timeSinceReport %= ReportIntervalSeconds;
        TickSensors();
    }
}
