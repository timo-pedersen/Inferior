using Inferior.Core.DataBus;
using Inferior.Gameplay.SensorData;

namespace Inferior.Gameplay.Sensors;

/// <summary>
/// Passive gravity field sensor. Measures net gravitational acceleration at the
/// ship's current position and publishes it to DataBus.Instruments.
///
/// Topics:
///   "GravitySensor.Strength"    m/s² — net gravitational acceleration (noised)
///   "GravitySensor.DirectionX/Y/Z" — normalised gravity vector, no noise applied
///                                    (direction noise would be physically wrong)
///
/// Noise model (strength only):
///   ±0.5% white jitter — baseline instrument noise
///   ±1.0% pink drift   — thermal drift and sensor aging
/// </summary>
public sealed class GravitySensor
{
    private readonly PassiveSensor _sensor = new()
    {
        TopicPrefix = "GravitySensor",
        ValueName   = Topics.GravitySensor.Strength,
        MaxValue    = 100.0,  // m/s² — covers all bodies from asteroids to neutron stars
        Seed        = (double)HashCode.Combine("GravitySensor"),
        NoiseWhite  = 0.005,  // ±0.5% baseline jitter
        NoisePink   = 0.010,  // ±1.0% slow drift
    };

    /// <summary>Access the underlying sensor to attach ExternalNoiseSources.</summary>
    public PassiveSensor Sensor => _sensor;

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
            DataBus.Instruments.Publish($"GravitySensor.{Topics.GravitySensor.DirectionX}", norm.X);
            DataBus.Instruments.Publish($"GravitySensor.{Topics.GravitySensor.DirectionY}", norm.Y);
            DataBus.Instruments.Publish($"GravitySensor.{Topics.GravitySensor.DirectionZ}", norm.Z);
        }
    }
}
