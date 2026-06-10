using Inferior.Core.Simulation;
using Inferior.Gameplay.SensorData;
using Env = Inferior.Gameplay.SensorData.Environment;

namespace Inferior.Gameplay.Sensors;

/// <summary>
/// Passive external temperature sensor. Measures skin/hull external temperature.
/// Near a star surface this is very high; in deep space it approaches CMB (~2.7 K).
///
/// Topic: "{Name}.Temperature" — temperature in Kelvin
/// </summary>
public sealed class ExternalTemperatureSensor
{
    private readonly PassiveSensor _sensor;

    public ExternalTemperatureSensor(string name = "ExtTemp")
    {
        _sensor = new PassiveSensor
        {
            TopicPrefix = name,
            ValueName   = Core.DataBus.Topics.ExternalTemperature.Value,
            MaxValue    = 30_000.0,   // K — near stellar photosphere
            Seed        = (double)HashCode.Combine(name + ".Temperature"),
            NoiseWhite  = 0.003,
            NoisePink   = 0.010,
        };
    }

    public PassiveSensor Sensor => _sensor;

    public void Tick() => _sensor.Publish(Env.ExternalTemperature);
}
