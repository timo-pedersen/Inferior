using Inferior.Core.DataBus;
using Inferior.Gameplay.SensorData;

namespace Inferior.Gameplay.Sensors;

/// <summary>
/// Passive gravity field sensor. Measures net gravitational acceleration at the
/// ship's current position and publishes it to DataBus.ScalarTelemetry.
///
/// Topics:
///   "GravitySensor.Strength"    m/s² — net gravitational acceleration (noised)
///   "GravitySensor.Direction" — normalised gravity vector, no noise applied
///                                    (direction noise would be physically wrong)
///
/// Noise model (strength only):
///   ±0.5% white jitter — baseline instrument noise
///   ±1.0% pink drift   — thermal drift and sensor aging
/// </summary>
public sealed class GravitySensor
{
    private const string DeviceId = "GravitySensor";
    private static readonly string DirectionTopic = $"{DeviceId}.{Topics.GravitySensor.Direction}";

    private readonly PassiveSensor _sensor = new()
    {
        TopicPrefix = "GravitySensor",
        ValueName   = Topics.GravitySensor.Strength,
        Quantity    = PhysicalQuantity.Acceleration,
        PublishDeviceInfo = false,
        MaxValue    = 100.0,  // m/s² — covers all bodies from asteroids to neutron stars
        Seed        = (double)HashCode.Combine("GravitySensor"),
        NoiseWhite  = 0.005,  // ±0.5% baseline jitter
        NoisePink   = 0.010,  // ±1.0% slow drift
    };

    /// <summary>Access the underlying sensor to attach ExternalNoiseSources.</summary>
    public PassiveSensor Sensor => _sensor;

    public GravitySensor()
    {
        DataBus.PublishTelemetryInfo(new TelemetryInfo
        {
            Topic = DirectionTopic,
            DeviceId = DeviceId,
            ValueKind = TelemetryValueKind.Vector,
            Quantity = PhysicalQuantity.Direction,
            ReferenceFrame = TelemetryReferenceFrame.SystemEcliptic,
            Publication = new PublicationInfo(PublicationMode.EveryTick),
            TopicPolicy = TopicPolicy.LatestState,
        });
        DataBus.DeviceInfo.Publish(DeviceId, new DeviceInfo
        {
            DeviceId = DeviceId,
            PublishedTopics = [$"{DeviceId}.{Topics.GravitySensor.Strength}", DirectionTopic],
            Power = new PowerProfile(0.0, 0.0),
        });
    }

    /// <summary>
    /// Read gravitational vector from Environment and publish strength + direction.
    /// Call once per sim tick from Simulation.Publish().
    /// </summary>
    public void Tick()
    {
        var    vec      = SensorData.Environment.GravitationalVector;
        double strength = vec.Length;

        _sensor.Publish(strength);   // strength gets noise applied

        // Direction is published without noise — adding noise to a unit vector
        // would violate the physics (it wouldn't stay unit length)
        if (strength > 1e-10)
        {
            var norm = vec / strength;
            DataBus.VectorTelemetry.Publish(DirectionTopic, norm);
        }
    }
}
