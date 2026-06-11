using Inferior.Core.DataBus;
using SensorEnv = Inferior.Gameplay.SensorData.Environment;

namespace Inferior.Gameplay.Sensors;

/// <summary>
/// Passive atmospheric-pressure sensor. Reads the hull external pressure each tick
/// and publishes it to DataBus.Instruments — but only while actually inside an atmosphere.
///
/// Out-of-atmosphere behaviour: the threshold suppresses all posts when pressure ≈ 0 Pa
/// (open space), keeping the DataBus free of noise on the vast majority of ticks.
///
/// Published topic: "{name}.Pressure" (Pa)
///
/// Default rate: 4 posts/second — fast enough for gameplay feedback,
/// low enough to avoid clogging the bus with 60 Hz atmospheric updates.
/// </summary>
public sealed class AtmosphericPressureSensor
{
    private readonly PassiveSensor _sensor;

    public AtmosphericPressureSensor(string name)
    {
        _sensor = new PassiveSensor
        {
            TopicPrefix          = name,
            ValueName            = Topics.AtmosphericPressure.Value,
            MaxValue             = 1_000_000.0,  // Pa — up to ~10 atm (corrosive worlds)
            Seed                 = (double)HashCode.Combine(name),
            NoiseWhite           = 0.003,         // ±0.3%
            NoisePink            = 0.006,         // ±0.6%
            PostsPerSecond       = 4.0,
            PostThresholdMinValue = 1.0,           // silent in vacuum (pressure == 0 Pa)
        };
    }

    /// <summary>Call once per sim tick from Simulation.Publish().</summary>
    public void Tick(double dt)
    {
        double pressure = 0.0;
        var nearest = SensorEnv.GetNearestBody();
        if (nearest.HasValue)
        {
            (Galaxy.OrbitalBody body, Core.Math.DVec3 pos) = nearest.Value;
            pressure = SensorEnv.AtmosphericPressure(body, pos);
        }
        _sensor.Publish(pressure, dt);
    }
}
