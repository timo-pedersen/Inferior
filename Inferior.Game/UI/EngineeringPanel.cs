using Inferior.Core.DataBus;
using Inferior.UI;
using Inferior.UI.Controls;
using Microsoft.Xna.Framework;

namespace Inferior.Game.UI;

/// <summary>
/// Inferior-specific adapter between the ship engineering bus projection and the generic
/// diagram control. It owns subscriptions only while the containing edge panel is open.
/// </summary>
public sealed class EngineeringPanel : Control, IDisposable
{
    private readonly DiagramCanvas _diagram = new();
    private readonly List<IDisposable> _liveSubscriptions = [];
    private IDisposable? _topologySubscription;
    private ShipSystemsTopologySnapshot? _topology;
    private int _layoutWidth = -1;
    private int _layoutHeight = -1;
    private bool _active;

    public bool IsActive => _active;

    public EngineeringPanel()
    {
        Overflow = OverflowMode.Clip;
        _diagram.ActionToggleRequested += RequestPowerChange;
        Add(_diagram);
    }

    public void Activate()
    {
        if (_active)
            return;

        _active = true;
        _topologySubscription = DataBus.ShipSystemsTopology.Subscribe(
            Topics.Ship.SystemsTopology,
            ApplyTopology,
            ReplayMode.Latest);
    }

    public void Deactivate()
    {
        if (!_active)
            return;

        _active = false;
        _topologySubscription?.Dispose();
        _topologySubscription = null;
        DisposeLiveSubscriptions();
    }

    public override void Update(double dt)
    {
        Rectangle bounds = Bounds;
        if (_diagram.Bounds.Width != bounds.Width || _diagram.Bounds.Height != bounds.Height)
            _diagram.Bounds = new Rectangle(0, 0, bounds.Width, bounds.Height);

        if (_topology is not null
            && (_layoutWidth != bounds.Width || _layoutHeight != bounds.Height))
        {
            BuildDiagram(_topology);
            if (_active)
                SubscribeToLiveValues(_topology);
        }

        base.Update(dt);
    }

    public void Dispose()
    {
        Deactivate();
        _diagram.ActionToggleRequested -= RequestPowerChange;
    }

    private void ApplyTopology(ShipSystemsTopologySnapshot topology)
    {
        _topology = topology;
        BuildDiagram(topology);
        SubscribeToLiveValues(topology);
    }

    private void BuildDiagram(ShipSystemsTopologySnapshot topology)
    {
        int width = Math.Max(1, Bounds.Width);
        int height = Math.Max(1, Bounds.Height);
        _layoutWidth = width;
        _layoutHeight = height;

        const int outerPad = 8;
        const int laneGap = 8;
        const int nodeGap = 7;
        int laneWidth = Math.Max(110, (width - outerPad * 2 - laneGap * 4) / 5);
        int usableHeight = Math.Max(100, height - outerPad * 2);

        var nodes = new List<DiagramNode>(topology.Nodes.Length);
        foreach (ShipSystemStage stage in Enum.GetValues<ShipSystemStage>())
        {
            ShipSystemNodeInfo[] stageNodes = topology.Nodes
                .Where(node => node.Stage == stage)
                .OrderBy(node => node.Order)
                .ToArray();
            if (stageNodes.Length == 0)
                continue;

            int stageIndex = (int)stage;
            int columns = stageNodes.Length > 4 ? 2 : 1;
            int rows = (int)Math.Ceiling(stageNodes.Length / (double)columns);
            int nodeWidth = Math.Max(82, (laneWidth - nodeGap * (columns - 1)) / columns);
            int nodeHeight = Math.Clamp((usableHeight - nodeGap * (rows - 1)) / rows, 88, 126);
            int xBase = outerPad + stageIndex * (laneWidth + laneGap);

            for (int i = 0; i < stageNodes.Length; i++)
            {
                ShipSystemNodeInfo info = stageNodes[i];
                int column = i / rows;
                int row = i % rows;
                var node = new DiagramNode
                {
                    Id = info.NodeId,
                    Title = info.Label.ToUpperInvariant(),
                    Subtitle = info.DeviceId is null
                        ? $"EMPTY / {info.Category.ToUpperInvariant()}"
                        : info.Category.ToUpperInvariant(),
                    Bounds = new Rectangle(
                        xBase + column * (nodeWidth + nodeGap),
                        outerPad + row * (nodeHeight + nodeGap),
                        nodeWidth,
                        nodeHeight),
                    State = info.DeviceId is null ? DiagramNodeState.Empty : DiagramNodeState.Unknown,
                    StateText = info.DeviceId is null ? "EMPTY" : "NO STATE",
                    ShowActionToggle = info.CanSetPower,
                    ActionState = false,
                    ActionConfirmed = info.CanSetPower ? false : null,
                };

                foreach (ShipSystemMetricBinding metric in info.Metrics.Take(4))
                    node.Values.Add(new DiagramValue(MetricLabel(metric.Role), "--", DiagramValueSeverity.Stale));
                nodes.Add(node);
            }
        }

        var connections = topology.Connections.Select(connection => new DiagramConnection
        {
            Id = connection.ConnectionId,
            FromNodeId = connection.FromNodeId,
            ToNodeId = connection.ToNodeId,
            Label = connection.CapacityWatts is double capacity ? $"MAX {FormatPower(capacity)}" : "",
            Active = false,
        });
        _diagram.SetModel(nodes, connections);
    }

    private void SubscribeToLiveValues(ShipSystemsTopologySnapshot topology)
    {
        DisposeLiveSubscriptions();

        foreach (ShipSystemNodeInfo info in topology.Nodes)
        {
            if (info.DeviceId is not null)
            {
                string nodeId = info.NodeId;
                _liveSubscriptions.Add(DataBus.DeviceState.Subscribe(
                    info.DeviceId,
                    state => ApplyDeviceState(nodeId, state),
                    ReplayMode.Latest));
            }

            foreach (ShipSystemMetricBinding metric in info.Metrics.Take(4))
            {
                string nodeId = info.NodeId;
                ShipSystemMetricRole role = metric.Role;
                _liveSubscriptions.Add(DataBus.ScalarTelemetry.SubscribeSamples(
                    metric.Topic,
                    sample => ApplyMetric(nodeId, role, sample),
                    ReplayMode.Latest));
            }
        }

        foreach (IGrouping<string, ShipSystemConnectionInfo> flowGroup in topology.Connections
                     .Where(connection => !string.IsNullOrWhiteSpace(connection.FlowTopic))
                     .GroupBy(connection => connection.FlowTopic!, StringComparer.Ordinal))
        {
            string topic = flowGroup.Key;
            string[] connectionIds = flowGroup.Select(connection => connection.ConnectionId).ToArray();
            _liveSubscriptions.Add(DataBus.ScalarTelemetry.SubscribeSamples(
                topic,
                sample => ApplyFlow(connectionIds, sample.Value),
                ReplayMode.Latest));
        }
    }

    private void ApplyDeviceState(string nodeId, DeviceState state)
    {
        if (!_diagram.Nodes.TryGetValue(nodeId, out DiagramNode? node))
            return;

        (node.State, node.StateText) = state.Status switch
        {
            DeviceOperationalStatus.PowerOff => (DiagramNodeState.Offline, "POWER OFF"),
            DeviceOperationalStatus.PowerOn => (DiagramNodeState.Transitioning, "POWER REQUESTED"),
            DeviceOperationalStatus.Initializing => (DiagramNodeState.Transitioning, "INITIALIZING"),
            DeviceOperationalStatus.Running when state.Damage >= 0.8 => (DiagramNodeState.Fault, "CRITICAL DAMAGE"),
            DeviceOperationalStatus.Running when state.Damage >= 0.4 => (DiagramNodeState.Warning, "DAMAGED"),
            DeviceOperationalStatus.Running => (DiagramNodeState.Online, "ONLINE"),
            DeviceOperationalStatus.Faulted => (DiagramNodeState.Fault, "FAULT"),
            _ => (DiagramNodeState.Unknown, "UNAVAILABLE"),
        };

        if (!node.ShowActionToggle)
            return;

        node.ActionState = state.Status is DeviceOperationalStatus.PowerOn
            or DeviceOperationalStatus.Initializing
            or DeviceOperationalStatus.Running;
        node.ActionConfirmed = state.Status switch
        {
            DeviceOperationalStatus.Running => true,
            DeviceOperationalStatus.PowerOff => false,
            _ => null,
        };
    }

    private void ApplyMetric(
        string nodeId,
        ShipSystemMetricRole role,
        TelemetrySample<double> sample)
    {
        if (!_diagram.Nodes.TryGetValue(nodeId, out DiagramNode? node))
            return;

        string label = MetricLabel(role);
        int index = node.Values.FindIndex(value => value.Label == label);
        if (index >= 0)
            node.Values[index] = new DiagramValue(label, FormatMetric(role, sample.Value));
    }

    private void ApplyFlow(IReadOnlyList<string> connectionIds, double watts)
    {
        foreach (string connectionId in connectionIds)
        {
            if (!_diagram.Connections.TryGetValue(connectionId, out DiagramConnection? connection))
                continue;

            connection.Active = watts > 0.5;
            connection.Label = FormatPower(watts);
            ShipSystemConnectionInfo? topologyConnection = _topology?.Connections
                .FirstOrDefault(item => item.ConnectionId == connectionId);
            connection.LoadFraction = topologyConnection?.CapacityWatts is > 0.0
                ? watts / topologyConnection.CapacityWatts.Value
                : 0.0;
        }
    }

    private void RequestPowerChange(string nodeId, bool powerOn)
    {
        ShipSystemNodeInfo? node = _topology?.Nodes.FirstOrDefault(item => item.NodeId == nodeId);
        if (node?.CanSetPower == true && !string.IsNullOrWhiteSpace(node.PowerCommandTopic))
            CommandBus.Send(node.PowerCommandTopic, powerOn ? 1.0 : 0.0);
    }

    private void DisposeLiveSubscriptions()
    {
        foreach (IDisposable subscription in _liveSubscriptions)
            subscription.Dispose();
        _liveSubscriptions.Clear();
    }

    private static string MetricLabel(ShipSystemMetricRole role) => role switch
    {
        ShipSystemMetricRole.PowerInput => "POWER IN",
        ShipSystemMetricRole.PowerOutput => "POWER OUT",
        ShipSystemMetricRole.PowerFlow => "FLOW",
        ShipSystemMetricRole.CapacitorFill => "CAPACITOR",
        ShipSystemMetricRole.HeatGeneration => "HEAT",
        ShipSystemMetricRole.ThermalLoad => "THERMAL",
        ShipSystemMetricRole.Temperature => "TEMP",
        ShipSystemMetricRole.CoolantLevel => "COOLANT",
        ShipSystemMetricRole.HeatSinkFill => "SINK",
        ShipSystemMetricRole.HeatDissipation => "DISSIPATION",
        ShipSystemMetricRole.HeatIrradiance => "SOLAR HEAT",
        _ => role.ToString().ToUpperInvariant(),
    };

    private static string FormatMetric(ShipSystemMetricRole role, double value) => role switch
    {
        ShipSystemMetricRole.CapacitorFill
            or ShipSystemMetricRole.ThermalLoad
            or ShipSystemMetricRole.CoolantLevel
            or ShipSystemMetricRole.HeatSinkFill => $"{value * 100.0:F0}%",
        ShipSystemMetricRole.Temperature => $"{value:F0} K",
        ShipSystemMetricRole.HeatIrradiance => FormatIrradiance(value),
        _ => FormatPower(value),
    };

    private static string FormatIrradiance(double wattsPerSquareMetre)
    {
        double magnitude = Math.Abs(wattsPerSquareMetre);
        return magnitude switch
        {
            >= 1_000_000_000.0 => $"{wattsPerSquareMetre / 1_000_000_000.0:F2} GW/m²",
            >= 1_000_000.0 => $"{wattsPerSquareMetre / 1_000_000.0:F2} MW/m²",
            >= 1_000.0 => $"{wattsPerSquareMetre / 1_000.0:F1} kW/m²",
            _ => $"{wattsPerSquareMetre:F0} W/m²",
        };
    }

    private static string FormatPower(double watts)
    {
        double magnitude = Math.Abs(watts);
        return magnitude switch
        {
            >= 1_000_000_000.0 => $"{watts / 1_000_000_000.0:F2} GW",
            >= 1_000_000.0 => $"{watts / 1_000_000.0:F2} MW",
            >= 1_000.0 => $"{watts / 1_000.0:F1} kW",
            _ => $"{watts:F0} W",
        };
    }
}
