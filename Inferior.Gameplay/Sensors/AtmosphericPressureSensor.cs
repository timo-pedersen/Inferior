using Inferior.Core.DataBus;
using SensorEnv = Inferior.Gameplay.SensorData.Environment;

namespace Inferior.Gameplay.Sensors;

/// <summary>
/// Active atmospheric-pressure sensor. Does nothing until commanded — then takes a
/// single reading at the ship's current position and publishes it to DataBus.Instruments.
///
/// Command topic prefix: the name passed to the constructor (e.g. "AtmosphericSensor").
/// Any command whose topic starts with that name triggers a measurement.
///
/// Published topic: "{name}.Pressure" (Pa)
/// </summary>
public sealed class AtmosphericPressureSensor
{
    private readonly string _name;

    public AtmosphericPressureSensor(string name)
    {
        _name = name;
        CommandBus.Subscribe(name, _ => TakeMeasurement());
    }

    private void TakeMeasurement()
    {
        double pressure = 0.0;
        var nearest = SensorEnv.GetNearestBody();
        if (nearest.HasValue)
        {
            (Galaxy.OrbitalBody body, Core.Math.DVec3 pos) = nearest.Value;
            pressure = SensorEnv.AtmosphericPressure(body, pos);
        }
        DataBus.Instruments.Publish($"{_name}.{Topics.AtmosphericPressure.Value}", pressure);
    }
}
