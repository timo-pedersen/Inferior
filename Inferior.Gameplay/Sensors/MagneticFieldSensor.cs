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
    private readonly string        _directionTopic;
    private readonly PassiveSensor _strengthSensor;

    public MagneticFieldSensor(string name = "MagSensor")
    {
        _strengthSensor = new PassiveSensor
        {
            TopicPrefix = name,
            ValueName   = Topics.MagneticField.Strength,
            Quantity    = PhysicalQuantity.MagneticFluxDensity,
            PublishDeviceInfo = false,
            MaxValue    = 1e9,    // Tesla — neutron star surface field order of magnitude
            Seed        = (double)HashCode.Combine(name + ".Mag"),
            NoiseWhite  = 0.004,
            NoisePink   = 0.005,
        };

        _directionTopic = $"{name}.{Topics.MagneticField.Direction}";
        DataBus.PublishTelemetryInfo(new TelemetryInfo
        {
            Topic = _directionTopic,
            DeviceId = name,
            ValueKind = TelemetryValueKind.Vector,
            Quantity = PhysicalQuantity.Direction,
            ReferenceFrame = TelemetryReferenceFrame.SystemEcliptic,
            Publication = new PublicationInfo(PublicationMode.EveryTick),
            TopicPolicy = TopicPolicy.LatestState,
        });
        DataBus.DeviceInfo.Publish(name, new DeviceInfo
        {
            DeviceId = name,
            PublishedTopics = [$"{name}.{Topics.MagneticField.Strength}", _directionTopic],
            Power = new PowerProfile(0.0, 0.0),
        });
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
            DataBus.VectorTelemetry.Publish(_directionTopic, norm);
        }
    }
}
