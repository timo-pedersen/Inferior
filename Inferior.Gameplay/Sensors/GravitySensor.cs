using Inferior.Core.DataBus;
using Inferior.Gameplay.SensorData;

namespace Inferior.Gameplay.Sensors;

/// <summary>
/// Passive gravity field sensor. Measures net gravitational acceleration at the
/// ship's current position and publishes it to DataBus.Instruments.
///
/// Topic: "GravitySensor.Strength"  (m/s²)
///
/// Noise model:
///   ±0.5% white jitter — baseline instrument noise
///   ±1.0% pink drift   — thermal drift and sensor aging
///
/// External noise sources can be attached at runtime for environmental effects
/// (e.g. neutron star EM interference via ExternalNoiseSources).
///
/// Note: reads Environment.GravitationalStrength, which returns 0 until
/// SimWorld.MassiveBodies is populated by the physics layer.
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
    /// Read gravitational strength from Environment and publish to DataBus.
    /// Call once per sim tick from Simulation.Publish().
    /// </summary>
    public void Tick()
    {
        double strength = SensorData.Environment.GravitationalStrength;
        _sensor.Publish(strength);
    }
}
