using Inferior.Core.DataBus;

namespace Inferior.Gameplay.Components;

/// <summary>
/// A single named scalar measurement on a ship component. Values publish through
/// ScalarTelemetry; retained metadata publishes through TelemetryInfo.
///
/// Topic convention: "ComponentName.Property", e.g. "Reactor.Output".
/// Query command: "ComponentName.Property.Query" republishes the metadata.
/// </summary>
public sealed class ComponentSensor
{
    public string Topic { get; }
    public RangeValue TotalRange { get; }
    public RangeValue SafeRange { get; }
    public PhysicalQuantity Quantity { get; }
    public PublicationInfo Publication { get; }
    public string QueryCommandTopic => Topic + ".Query";

    private readonly Func<double> _read;
    private IDisposable? _querySubscription;

    public ComponentSensor(
        string topic,
        Func<double> read,
        RangeValue safeRange,
        RangeValue totalRange,
        PhysicalQuantity quantity = PhysicalQuantity.Unspecified,
        PublicationInfo? publication = null)
    {
        Topic = topic;
        _read = read;
        SafeRange = safeRange;
        TotalRange = totalRange;
        Quantity = quantity;
        Publication = publication ?? new PublicationInfo(PublicationMode.EveryTick);

    }

    internal void ActivateBus()
    {
        // A query is an explicit refresh, not the normal UI bootstrap path. New UI
        // subscribers receive the retained TelemetryInfo directly from its bus.
        _querySubscription ??= CommandBus.Subscribe(QueryCommandTopic, _ => PublishInfo());
    }

    internal void DeactivateBus()
    {
        _querySubscription?.Dispose();
        _querySubscription = null;
    }

    public void PublishInfo()
    {
        var bands = new List<TelemetryBand>(2);
        if (TotalRange.Low < SafeRange.Low)
            bands.Add(new(new RangeValue(TotalRange.Low, SafeRange.Low), TelemetryBandSeverity.Warning));
        if (SafeRange.High < TotalRange.High)
            bands.Add(new(new RangeValue(SafeRange.High, TotalRange.High), TelemetryBandSeverity.Warning));

        string deviceId = Topic.Split('.', 2)[0];
        DataBus.PublishTelemetryInfo(new TelemetryInfo
        {
            Topic = Topic,
            DeviceId = deviceId,
            ValueKind = TelemetryValueKind.Scalar,
            Quantity = Quantity,
            OperatingRange = TotalRange,
            SuggestedDisplayRange = TotalRange,
            Bands = [.. bands],
            Publication = Publication,
            TopicPolicy = TopicPolicy.LatestState,
        });
    }

    public void Tick() => DataBus.ScalarTelemetry.Publish(Topic, _read());
}
