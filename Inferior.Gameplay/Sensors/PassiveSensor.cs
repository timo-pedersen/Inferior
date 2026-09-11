using Inferior.Core.DataBus;
using Inferior.Core.Simulation;

namespace Inferior.Gameplay.Sensors;

/// <summary>
/// Reusable passive sensor. Reads a raw physical value, applies layered noise,
/// and publishes the result to DataBus.ScalarTelemetry.
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

    /// <summary>Physical meaning of the scalar value; raw values remain SI.</summary>
    public PhysicalQuantity Quantity { get; init; } = PhysicalQuantity.Unspecified;

    /// <summary>
    /// False when a multi-output sensor publishes one combined DeviceInfo itself.
    /// </summary>
    public bool PublishDeviceInfo { get; init; } = true;

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

    // ── Rate limiting ─────────────────────────────────────────────────────────

    /// <summary>
    /// Maximum publish rate in posts per second.
    /// Default 0 = publish every tick (no rate limiting).
    /// E.g. set to 4.0 to post at most 4 times per second.
    /// </summary>
    public double PostsPerSecond { get; set; } = 0;

    /// <summary>
    /// Minimum raw value required before publishing.
    /// Default <see cref="double.NegativeInfinity"/> = always publish.
    /// Use to suppress a sensor entirely when outside its operating range
    /// (e.g. atmospheric pressure sensor in vacuum: set to 1.0 Pa).
    /// </summary>
    public double PostThresholdMinValue { get; set; } = double.NegativeInfinity;

    private double _timeSinceLastPost;
    private bool _infoPublished;

    // ── Publish ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Apply noise to <paramref name="rawValue"/> and publish to DataBus.ScalarTelemetry,
    /// subject to <see cref="PostThresholdMinValue"/> and <see cref="PostsPerSecond"/>.
    /// <paramref name="dt"/> is the sim tick duration in seconds; pass 0 to skip rate limiting.
    /// Safe to call from the sim thread.
    /// </summary>
    public void Publish(double rawValue, double dt = 1.0 / 60.0)
    {
        PublishInfoOnce();
        if (rawValue < PostThresholdMinValue) return;

        if (PostsPerSecond > 0)
        {
            _timeSinceLastPost += dt;
            double minInterval = 1.0 / PostsPerSecond;
            if (_timeSinceLastPost < minInterval) return;
            _timeSinceLastPost = 0;
        }

        double noise = Noise.Scale(Noise.White(Seed),       MaxValue, NoiseWhite)
                     + Noise.Scale(Noise.Pink(Seed + 1e4),  MaxValue, NoisePink);

        foreach (var source in ExternalNoiseSources)
            noise += source();

        DataBus.ScalarTelemetry.Publish($"{TopicPrefix}.{ValueName}", rawValue + noise);
    }

    private void PublishInfoOnce()
    {
        if (_infoPublished)
            return;

        _infoPublished = true;
        string topic = $"{TopicPrefix}.{ValueName}";
        var policy = TopicPolicy.LatestState;
        DataBus.PublishTelemetryInfo(new TelemetryInfo
        {
            Topic = topic,
            DeviceId = TopicPrefix,
            ValueKind = TelemetryValueKind.Scalar,
            Quantity = Quantity,
            OperatingRange = new RangeValue(0.0, MaxValue),
            SuggestedDisplayRange = new RangeValue(0.0, MaxValue),
            Publication = PostsPerSecond > 0
                ? new PublicationInfo(PublicationMode.Periodic, PostsPerSecond)
                : new PublicationInfo(PublicationMode.EveryTick),
            TopicPolicy = policy,
        });

        if (PublishDeviceInfo)
        {
            DataBus.DeviceInfo.Publish(TopicPrefix, new DeviceInfo
            {
                DeviceId = TopicPrefix,
                PublishedTopics = [topic],
                Power = new PowerProfile(0.0, 0.0),
            });
        }

        DataBus.DeviceState.Publish(TopicPrefix, new DeviceState(
            TopicPrefix,
            DeviceOperationalStatus.Running,
            Damage: 0.0,
            Efficiency: 1.0,
            SimulationTime: GameClock.SimTime));
    }
}
