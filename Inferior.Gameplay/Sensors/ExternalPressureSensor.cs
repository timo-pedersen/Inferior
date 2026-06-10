using Inferior.Core.Simulation;
using Inferior.Gameplay.SensorData;
using Env = Inferior.Gameplay.SensorData.Environment;

namespace Inferior.Gameplay.Sensors;

/// <summary>
/// Passive external pressure sensor. Meaningful inside atmospheres; ~0 in open space.
///
/// Topic: "{Name}.Pressure" — pressure in Pascals
/// </summary>
public sealed class ExternalPressureSensor
{
    private readonly PassiveSensor _sensor;

    public ExternalPressureSensor(string name = "ExtPressure")
    {
        _sensor = new PassiveSensor
        {
            TopicPrefix = name,
            ValueName   = Core.DataBus.Topics.ExternalPressure.Value,
            MaxValue    = 1_000_000.0,  // 10 atm — upper bound for dense atmospheres
            Seed        = (double)HashCode.Combine(name + ".Pressure"),
            NoiseWhite  = 0.002,
            NoisePink   = 0.004,
        };
    }

    public PassiveSensor Sensor => _sensor;

    public void Tick() => _sensor.Publish(Env.ExternalPressure);
}
