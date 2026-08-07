using Inferior.Core.Math;
using System.Collections.Concurrent;

namespace Inferior.Core.DataBus;

/// <summary>
/// Static compatibility hub for inter-system messaging. Simulation publishers enqueue;
/// the main thread drains once per frame. These channels are intended to become ship-owned.
/// </summary>
public static class DataBus
{
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>>
        TopicsByDevice = new(StringComparer.Ordinal);

    /// <summary>Human-readable console and HUD messages. Every occurrence is delivered.</summary>
    public static readonly Bus<SystemMessage> SystemMessages =
        new(TopicPolicy.OrderedHistory(256));

    /// <summary>Scalar measurements and operational values, in raw SI where applicable.</summary>
    public static readonly TelemetryChannel<double> ScalarTelemetry = new();

    /// <summary>Atomic vector measurements, in raw SI where applicable.</summary>
    public static readonly TelemetryChannel<DVec3> VectorTelemetry = new();

    /// <summary>Array-valued spectrum results. Latest completed scan is replayable.</summary>
    public static readonly TelemetryChannel<double[]> SpectrumTelemetry = new();

    /// <summary>Retained descriptions keyed by telemetry topic.</summary>
    public static readonly Bus<TelemetryInfo> TelemetryInfo =
        new(TopicPolicy.LatestState);

    /// <summary>Retained descriptions keyed by sensor/component device ID.</summary>
    public static readonly Bus<DeviceInfo> DeviceInfo =
        new(TopicPolicy.LatestState);

    /// <summary>Retained current operational state keyed by sensor/component device ID.</summary>
    public static readonly Bus<DeviceState> DeviceState =
        new(TopicPolicy.LatestState);

    /// <summary>Retained immutable topology projection for the currently active ship.</summary>
    public static readonly Bus<ShipSystemsTopologySnapshot> ShipSystemsTopology =
        new(TopicPolicy.LatestState);

    // Radar contact updates and losses remain event channels pending the radar migration.
    public static readonly Bus<RadarContact> Radar = new();
    public static readonly Bus<string> RadarLost = new();

    // Selected target changed; empty Id is the existing cleared sentinel.
    public static readonly Bus<RadarContact> Target = new(TopicPolicy.LatestState);

    public static void Drain()
    {
        SystemMessages.Drain();
        ScalarTelemetry.Drain();
        VectorTelemetry.Drain();
        SpectrumTelemetry.Drain();
        TelemetryInfo.Drain();
        DeviceInfo.Drain();
        DeviceState.Drain();
        ShipSystemsTopology.Drain();
        Radar.Drain();
        RadarLost.Drain();
        Target.Drain();
    }

    /// <summary>
    /// Publish a retained telemetry description and register its device ownership for
    /// deterministic removal at a device/ship lifecycle boundary.
    /// </summary>
    public static void PublishTelemetryInfo(TelemetryInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentException.ThrowIfNullOrWhiteSpace(info.Topic);
        ArgumentException.ThrowIfNullOrWhiteSpace(info.DeviceId);
        info.Publication.Validate();
        info.TopicPolicy.Validate();

        switch (info.ValueKind)
        {
            case TelemetryValueKind.Scalar:
                ScalarTelemetry.ConfigureTopic(info.Topic, info.TopicPolicy);
                break;
            case TelemetryValueKind.Vector:
                VectorTelemetry.ConfigureTopic(info.Topic, info.TopicPolicy);
                break;
            case TelemetryValueKind.Spectrum:
                SpectrumTelemetry.ConfigureTopic(info.Topic, info.TopicPolicy);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(info), "Unknown telemetry payload kind.");
        }

        var topics = TopicsByDevice.GetOrAdd(
            info.DeviceId,
            static _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
        topics[info.Topic] = 0;
        TelemetryInfo.Publish(info.Topic, info);
    }

    /// <summary>
    /// Remove retained metadata, state, and telemetry owned by one device. Call only after
    /// its publishers have stopped and their pending publications have been drained;
    /// queued publications are deliberately not rewritten.
    /// </summary>
    public static void RemoveDevice(string deviceId)
    {
        if (TopicsByDevice.TryRemove(deviceId, out var topics))
        {
            foreach (string topic in topics.Keys)
            {
                ScalarTelemetry.RemoveRetained(topic);
                VectorTelemetry.RemoveRetained(topic);
                SpectrumTelemetry.RemoveRetained(topic);
                TelemetryInfo.RemoveRetained(topic);
            }
        }

        DeviceInfo.RemoveRetained(deviceId);
        DeviceState.RemoveRetained(deviceId);
    }

    /// <summary>
    /// Clear transient retained presentation state at a simulation boundary. Subscriptions
    /// and topic contracts remain intact; future ship-owned channels will replace this hook.
    /// </summary>
    public static void ClearRetained()
    {
        SystemMessages.ClearRetained();
        ScalarTelemetry.ClearRetained();
        VectorTelemetry.ClearRetained();
        SpectrumTelemetry.ClearRetained();
        TelemetryInfo.ClearRetained();
        DeviceInfo.ClearRetained();
        DeviceState.ClearRetained();
        ShipSystemsTopology.ClearRetained();
        Target.ClearRetained();
        TopicsByDevice.Clear();
    }
}
