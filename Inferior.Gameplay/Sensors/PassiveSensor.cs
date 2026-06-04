using Inferior.Core.DataBus;
using Inferior.Core.Simulation;

namespace Inferior.Gameplay.Sensors;

/// <summary>
/// Reusable passive sensor. Reads a raw physical value, applies layered noise,
/// and publishes the result to DataBus.Instruments.
///
/// Called from the sim thread — Publish() is thread-safe via the bus queue.
///
/// Noise model:
///   White noise  — fast uncorrelated jitter  (baseline instrument noise)
///   Pink noise   — slow drift with texture   (thermal drift, aging)
///   ExternalNoiseSources — environment-driven additions (e.g. EM interference,
///                          stellar activity), registered as lambdas by the caller.
///
/// Usage:
///   var s = new PassiveSensor { TopicPrefix = "GravitySensor",
///                               ValueName   = "Strength",
///                               MaxValue    = 100.0,
///                               Seed        = mySeed,
///                               NoiseWhite  = 0.005,
///                               NoisePink   = 0.010 };
///   s.Publish(rawValue);  // call once per tick
/// </summary>
public sealed class PassiveSensor
{
    // ── Configuration (set at construction, treated as init-only) ─────────────

    /// <summary>Component name portion of the DataBus topic — e.g. "GravitySensor".</summary>
    public required string TopicPrefix { get; init; }

    /// <summary>Value name portion of the DataBus topic — e.g. "Strength".</summary>
    public required string ValueName   { get; init; }

    /// <summary>Maximum physical value the sensor can measure. Used for noise scaling.</summary>
    public double MaxValue   { get; init; } = 1.0;

    /// <summary>
    /// Unique seed — decorrelates noise from other sensors of the same type.
    /// Derive from sensor name: <c>(double)HashCode.Combine("SensorName")</c>
    /// </summary>
    public double Seed       { get; init; } = 0.0;

    /// <summary>White noise amplitude as a fraction of MaxValue (e.g. 0.005 = ±0.5%).</summary>
    public double NoiseWhite { get; init; } = 0.0;

    /// <summary>Pink noise amplitude as a fraction of MaxValue (e.g. 0.010 = ±1.0%).</summary>
    public double NoisePink  { get; init; } = 0.0;

    /// <summary>
    /// Environment-driven noise sources added on top of baseline.
    /// Each lambda returns a noise delta in the same units as rawValue.
    /// Add/remove at runtime to attach or detach environmental effects.
    /// </summary>
    public List<Func<double>> ExternalNoiseSources { get; } = [];

    // ── Publish ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Apply noise to <paramref name="rawValue"/> and publish to DataBus.Instruments.
    /// Safe to call from the sim thread.
    /// </summary>
    public void Publish(double rawValue)
    {
        double noise = Noise.Scale(Noise.White(Seed),       MaxValue, NoiseWhite)
                     + Noise.Scale(Noise.Pink(Seed + 1e4),  MaxValue, NoisePink);

        foreach (var source in ExternalNoiseSources)
            noise += source();

        DataBus.Instruments.Publish($"{TopicPrefix}.{ValueName}", rawValue + noise);
    }
}
