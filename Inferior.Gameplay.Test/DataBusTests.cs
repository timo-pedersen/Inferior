using Inferior.Core.DataBus;
using Inferior.Gameplay.Components;
using Xunit;

namespace Inferior.Gameplay.Test;

public sealed class DataBusTests
{
    [Fact]
    public void OrderedTopicDeliversEveryQueuedPublication()
    {
        var bus = new Bus<int>();
        var received = new List<int>();
        using var subscription = bus.Subscribe("topic", received.Add);

        bus.Publish("topic", 1);
        bus.Publish("topic", 2);
        bus.Publish("topic", 3);
        bus.Drain();

        Assert.Equal([1, 2, 3], received);
    }

    [Fact]
    public void LatestPerDrainDeliversOnlyFinalQueuedValue()
    {
        var bus = new Bus<int>(TopicPolicy.LatestState);
        var received = new List<int>();
        using var subscription = bus.Subscribe("topic", received.Add);

        bus.Publish("topic", 1);
        bus.Publish("topic", 2);
        bus.Publish("topic", 3);
        bus.Drain();

        Assert.Equal([3], received);
    }

    [Fact]
    public void LatestReplayCallsOnlyNewSubscriber()
    {
        var bus = new Bus<int>(TopicPolicy.LatestState);
        var existing = new List<int>();
        var addedLater = new List<int>();
        using var first = bus.Subscribe("topic", existing.Add);

        bus.Publish("topic", 7);
        bus.Drain();
        using var second = bus.Subscribe("topic", addedLater.Add, ReplayMode.Latest);

        Assert.Equal([7], existing);
        Assert.Equal([7], addedLater);
    }

    [Fact]
    public void SubscribeBeforeDrainDoesNotReplayQueuedPublicationTwice()
    {
        var bus = new Bus<int>(TopicPolicy.LatestState);
        var received = new List<int>();

        bus.Publish("topic", 7);
        using var subscription = bus.Subscribe("topic", received.Add, ReplayMode.Latest);
        Assert.Empty(received);

        bus.Drain();
        Assert.Equal([7], received);
    }

    [Fact]
    public void HistoryReplayIsBoundedAndOnlyTargetsNewSubscriber()
    {
        var bus = new Bus<int>(TopicPolicy.OrderedHistory(2));
        var existing = new List<int>();
        var history = new List<int>();
        using var first = bus.Subscribe("topic", existing.Add);

        bus.Publish("topic", 1);
        bus.Publish("topic", 2);
        bus.Publish("topic", 3);
        bus.Drain();
        using var second = bus.Subscribe("topic", history.Add, ReplayMode.History);

        Assert.Equal([1, 2, 3], existing);
        Assert.Equal([2, 3], history);
    }

    [Fact]
    public void HistoryRequiresPositiveBound()
    {
        var bus = new Bus<int>();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            bus.ConfigureTopic("topic", new TopicPolicy(
                DispatchMode.All,
                RetentionMode.History,
                HistoryCapacity: 0)));
    }

    [Fact]
    public void PendingQueueDropsOldestMessagesAtItsExplicitBound()
    {
        var bus = new Bus<int>(TopicPolicy.OrderedTransient, pendingCapacity: 2);
        var received = new List<int>();
        using var subscription = bus.Subscribe("topic", received.Add);

        bus.Publish("topic", 1);
        bus.Publish("topic", 2);
        bus.Publish("topic", 3);
        bus.Drain();

        Assert.Equal([2, 3], received);
        Assert.Equal(1, bus.DroppedMessageCount);
    }

    [Fact]
    public void TelemetrySamplesCarryTimeAndIncreasingSessionSequence()
    {
        var channel = new TelemetryChannel<double>(TopicPolicy.OrderedTransient);
        var received = new List<TelemetrySample<double>>();
        using var subscription = channel.SubscribeSamples(
            "topic",
            received.Add,
            ReplayMode.None);

        channel.Publish("topic", 10.0, simulationTime: 4.5);
        channel.Publish("topic", 20.0, simulationTime: 4.5);
        channel.Drain();

        Assert.Equal(2, received.Count);
        Assert.Equal(4.5, received[0].SimulationTime);
        Assert.Equal(4.5, received[1].SimulationTime);
        Assert.True(received[1].Sequence > received[0].Sequence);
    }

    [Fact]
    public void ComponentSensorPublishesOneReplayableTelemetryDescription()
    {
        string deviceId = $"TestDevice-{Guid.NewGuid():N}";
        string topic = $"{deviceId}.Power";
        var sensor = new ComponentSensor(
            topic,
            read: () => 42.0,
            safeRange: new RangeValue(10.0, 80.0),
            totalRange: new RangeValue(0.0, 100.0),
            quantity: PhysicalQuantity.Power);

        sensor.PublishInfo();
        DataBus.Drain();

        var replayed = new List<TelemetryInfo>();
        using var subscription = DataBus.TelemetryInfo.Subscribe(
            topic,
            replayed.Add,
            ReplayMode.Latest);

        TelemetryInfo info = Assert.Single(replayed);
        Assert.Equal(deviceId, info.DeviceId);
        Assert.Equal(PhysicalQuantity.Power, info.Quantity);
        Assert.Equal(new RangeValue(0.0, 100.0), info.OperatingRange);
        Assert.Equal(2, info.Bands.Length);

        DataBus.RemoveDevice(deviceId);
    }

    [Fact]
    public void RemovingDeviceForgetsItsRetainedTelemetryAndMetadata()
    {
        string deviceId = $"TestDevice-{Guid.NewGuid():N}";
        string topic = $"{deviceId}.Value";

        DataBus.PublishTelemetryInfo(new TelemetryInfo
        {
            Topic = topic,
            DeviceId = deviceId,
            ValueKind = TelemetryValueKind.Scalar,
        });
        DataBus.DeviceInfo.Publish(deviceId, new DeviceInfo { DeviceId = deviceId });
        DataBus.DeviceState.Publish(deviceId, new DeviceState(
            deviceId,
            DeviceOperationalStatus.Running,
            Damage: 0.0,
            Efficiency: 1.0,
            SimulationTime: 1.0));
        DataBus.ScalarTelemetry.Publish(topic, 12.0, simulationTime: 1.0);
        DataBus.Drain();

        DataBus.RemoveDevice(deviceId);

        var values = new List<double>();
        var infos = new List<TelemetryInfo>();
        var devices = new List<DeviceInfo>();
        var states = new List<DeviceState>();
        using var valueSubscription = DataBus.ScalarTelemetry.Subscribe(topic, values.Add);
        using var infoSubscription = DataBus.TelemetryInfo.Subscribe(topic, infos.Add, ReplayMode.Latest);
        using var deviceSubscription = DataBus.DeviceInfo.Subscribe(deviceId, devices.Add, ReplayMode.Latest);
        using var stateSubscription = DataBus.DeviceState.Subscribe(deviceId, states.Add, ReplayMode.Latest);

        Assert.Empty(values);
        Assert.Empty(infos);
        Assert.Empty(devices);
        Assert.Empty(states);
    }

    [Fact]
    public void ComponentCommandSubscriptionsFollowActiveShipLifecycle()
    {
        string deviceId = $"TestDevice-{Guid.NewGuid():N}";
        var component = new CommandTestComponent(deviceId);
        component.ActivateBus();

        CommandBus.Send($"{deviceId}.Ping");
        CommandBus.Drain();
        Assert.Equal(1, component.CommandCount);

        component.DeactivateBus();
        CommandBus.Send($"{deviceId}.Ping");
        CommandBus.Drain();
        Assert.Equal(1, component.CommandCount);
    }

    private sealed class CommandTestComponent : ShipComponent
    {
        public int CommandCount { get; private set; }

        public CommandTestComponent(string name)
        {
            Name = name;
            RegisterCommand($"{name}.Ping", _ => CommandCount++);
        }
    }
}
