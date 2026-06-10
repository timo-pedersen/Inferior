using Inferior.Core.DataBus;
using Inferior.Core.Simulation;
using Inferior.Gameplay.SensorData;
using Env = Inferior.Gameplay.SensorData.Environment;

namespace Inferior.Gameplay.Sensors;

/// <summary>
/// Passive magnetic field sensor. Measures the ambient magnetic field vector at the
/// ship's position. Significant near neutron stars; negligible in open space.
///
/// Topics:
///   "{Name}.Strength"  — field magnitude in Tesla
///   "{Name}.X/Y/Z"     — normalised direction components (no noise — same reason as gravity)
///
/// Noise model applied to Strength only:
///   ±0.4% white jitter
///   ±0.5% pink drift
/// </summary>
public sealed class MagneticFieldSensor
{
    private readonly string        _name;
    private readonly PassiveSensor _strengthSensor;

    public MagneticFieldSensor(string name = "MagSensor")
    {
        _name           = name;
        _strengthSensor = new PassiveSensor
        {
            TopicPrefix = name,
            ValueName   = Topics.MagneticField.Strength,
            MaxValue    = 1e9,    // Tesla — neutron star surface field order of magnitude
            Seed        = (double)HashCode.Combine(name + ".Mag"),
            NoiseWhite  = 0.004,
            NoisePink   = 0.005,
        };
    }

    public PassiveSensor StrengthSensor => _strengthSensor;

    public void Tick()
    {
        var    vec      = Env.MagneticFieldVector;
        double strength = vec.Length;

        _strengthSensor.Publish(strength);

        if (strength > 1e-10)
        {
            var norm = vec / strength;
            DataBus.Instruments.Publish($"{_name}.{Topics.MagneticField.X}", norm.X);
            DataBus.Instruments.Publish($"{_name}.{Topics.MagneticField.Y}", norm.Y);
            DataBus.Instruments.Publish($"{_name}.{Topics.MagneticField.Z}", norm.Z);
        }
    }
}
