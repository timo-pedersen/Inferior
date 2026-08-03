using Inferior.Core.DataBus;
using Inferior.Core.Simulation;
using Inferior.Gameplay.SensorData;
using Env = Inferior.Gameplay.SensorData.Environment;

namespace Inferior.Gameplay.Sensors;

/// <summary>
/// Passive radiation sensor. Measures total ionising flux from all stellar sources
/// at the ship's current position and publishes it to DataBus.ScalarTelemetry.
///
/// Topics:
///   "{Name}.Flux"  — radiation flux in W/m²
///
/// Noise model:
///   ±0.3% white jitter  — baseline instrument noise
///   ±0.8% pink drift    — thermal drift
/// </summary>
public sealed class RadiationSensor
{
    private readonly string       _name;
    private readonly PassiveSensor _sensor;

    public RadiationSensor(string name = "RadSensor")
    {
        _name   = name;
        _sensor = new PassiveSensor
        {
            TopicPrefix = name,
            ValueName   = Topics.RadiationSensor.Flux,
            Quantity    = PhysicalQuantity.Irradiance,
            MaxValue    = 1e6,   // W/m² — covers stellar proximity range
            Seed        = (double)HashCode.Combine(name + ".Flux"),
            NoiseWhite  = 0.003,
            NoisePink   = 0.008,
        };
    }

    public PassiveSensor Sensor => _sensor;

    public void Tick() => _sensor.Publish(Env.RadiationFlux);
}
