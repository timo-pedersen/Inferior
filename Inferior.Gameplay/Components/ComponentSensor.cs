using Inferior.Core.DataBus;

namespace Inferior.Gameplay.Components;

/// <summary>
/// A single named measurement on a ship component.
/// Publishes its value every sim tick via DataBus.Instruments and its operating
/// envelopes via DataBus.InstrumentRanges at startup (and on query).
///
/// Topic convention: "ComponentName.Property"  e.g. "Reactor.Output", "Reactor.Temperature"
/// Range topics:     "ComponentName.Property"        → total operating range
///                   "ComponentName.Property.Safe"   → safe operating range
/// Query command:    "ComponentName.Property.Query"  → re-publishes ranges on demand
/// </summary>
public sealed class ComponentSensor
{
    public string     Topic      { get; }
    public RangeValue TotalRange { get; }
    public RangeValue SafeRange  { get; }

    private readonly Func<double> _read;

    public ComponentSensor(string topic, Func<double> read, RangeValue safeRange, RangeValue totalRange)
    {
        Topic      = topic;
        _read      = read;
        SafeRange  = safeRange;
        TotalRange = totalRange;

        // Self-register: re-publish ranges whenever a Query command arrives for this topic
        CommandBus.Subscribe(topic + ".Query", _ => PublishRanges());
    }

    /// <summary>Publish operating envelopes. Called at component startup and on query.</summary>
    public void PublishRanges()
    {
        DataBus.InstrumentRanges.Publish(Topic,           TotalRange);
        DataBus.InstrumentRanges.Publish(Topic + ".Safe", SafeRange);
    }

    /// <summary>Read current value and publish to Instruments bus. Called every sim tick.</summary>
    public void Tick() => DataBus.Instruments.Publish(Topic, _read());
}
