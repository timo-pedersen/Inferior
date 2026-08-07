using Inferior.Core.DataBus;
using Inferior.Gameplay.Components;
using Inferior.Gameplay.Hull;

namespace Inferior.Gameplay.Ship;

/// <summary>
/// Simulation-owned functional configuration of one ship. Hull definitions provide the
/// available slot template; this object owns occupancy, bus ports, and connections.
/// </summary>
public sealed class ShipSystemTopology
{
    private sealed record SlotEntry(HullSlot Definition)
    {
        public string? DeviceId { get; set; }
        public ShipComponent? Component { get; set; }
        public string? InstalledLabel { get; set; }
    }

    private sealed record FixedEntry(
        string NodeId,
        string Label,
        ShipComponent Component,
        int Order);

    private sealed record BusPortEntry(
        string NodeId,
        string BusDeviceId,
        string ParentNodeId,
        int PortIndex)
    {
        public ShipComponent? Component { get; set; }
    }

    private readonly Dictionary<string, SlotEntry> _slots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FixedEntry> _fixedDevices = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BusPortEntry> _busPorts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ShipSystemConnectionInfo> _connections = new(StringComparer.Ordinal);

    public int Revision { get; private set; }

    public void ConfigureHullSlots(IEnumerable<HullSlot> slots)
    {
        ArgumentNullException.ThrowIfNull(slots);
        if (_slots.Count != 0)
            throw new InvalidOperationException("Ship hull slots are already configured.");

        foreach (HullSlot slot in slots)
        {
            if (!_slots.TryAdd(slot.SlotId, new SlotEntry(slot)))
                throw new InvalidOperationException($"Duplicate ship system slot '{slot.SlotId}'.");
        }
        Revision++;
    }

    public void Install(string slotId, ShipComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        SlotEntry slot = GetSlot(slotId);
        if (slot.DeviceId is not null)
            throw new InvalidOperationException($"Ship system slot '{slotId}' is already occupied.");
        EnsureUniqueDevice(component.Name);

        slot.DeviceId = component.Name;
        slot.Component = component;
        slot.InstalledLabel = component.Name;
        Revision++;
    }

    public void SetExternalDevice(string slotId, string? deviceId, string? label = null)
    {
        SlotEntry slot = GetSlot(slotId);
        if (slot.Component is not null)
            throw new InvalidOperationException(
                $"Ship system slot '{slotId}' is occupied by a ShipComponent.");
        if (deviceId is not null)
            EnsureUniqueDevice(deviceId, exceptSlotId: slotId);

        if (string.Equals(slot.DeviceId, deviceId, StringComparison.Ordinal)
            && string.Equals(slot.InstalledLabel, label, StringComparison.Ordinal))
        {
            return;
        }

        slot.DeviceId = deviceId;
        slot.InstalledLabel = label;
        Revision++;
    }

    public void InstallFixed(string nodeId, string label, ShipComponent component, int order = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(component);
        EnsureUniqueDevice(component.Name);
        if (!_fixedDevices.TryAdd(nodeId, new FixedEntry(nodeId, label, component, order)))
            throw new InvalidOperationException($"Duplicate fixed ship system node '{nodeId}'.");
        Revision++;
    }

    public void ConfigurePowerBus(string busDeviceId, int portCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(busDeviceId);
        if (portCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(portCount));

        string parentNodeId = FindNodeIdByDevice(busDeviceId);
        for (int i = 1; i <= portCount; i++)
        {
            string nodeId = PowerBusPortNodeId(busDeviceId, i);
            if (!_busPorts.TryAdd(nodeId, new BusPortEntry(nodeId, busDeviceId, parentNodeId, i)))
                throw new InvalidOperationException($"Duplicate power bus port '{nodeId}'.");
        }
        Revision++;
    }

    public string InstallBusComponent(string busDeviceId, int portIndex, ShipComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        string nodeId = PowerBusPortNodeId(busDeviceId, portIndex);
        if (!_busPorts.TryGetValue(nodeId, out BusPortEntry? port))
            throw new InvalidOperationException($"Unknown power bus port '{nodeId}'.");
        if (port.Component is not null)
            throw new InvalidOperationException($"Power bus port '{nodeId}' is already occupied.");
        EnsureUniqueDevice(component.Name);

        port.Component = component;
        Revision++;
        return nodeId;
    }

    public void ConnectPowerSource(
        string connectionId,
        string sourceDeviceId,
        string busDeviceId,
        double? capacityWatts = null,
        string? flowTopic = null)
        => AddConnection(new ShipSystemConnectionInfo
        {
            ConnectionId = connectionId,
            FromNodeId = FindNodeIdByDevice(sourceDeviceId),
            ToNodeId = FindNodeIdByDevice(busDeviceId),
            Kind = ShipSystemConnectionKind.PowerSource,
            CapacityWatts = capacityWatts,
            FlowTopic = flowTopic,
        });

    public void ConnectPowerConsumer(
        string connectionId,
        string busDeviceId,
        int portIndex,
        string targetDeviceId,
        string? flowTopic,
        double? capacityWatts)
    {
        string busNodeId = FindNodeIdByDevice(busDeviceId);
        string portNodeId = PowerBusPortNodeId(busDeviceId, portIndex);
        if (!_busPorts.ContainsKey(portNodeId))
            throw new InvalidOperationException($"Unknown power bus port '{portNodeId}'.");
        string targetNodeId = FindNodeIdByDevice(targetDeviceId);

        AddConnection(new ShipSystemConnectionInfo
        {
            ConnectionId = $"{connectionId}.bus",
            FromNodeId = busNodeId,
            ToNodeId = portNodeId,
            Kind = ShipSystemConnectionKind.PowerDistribution,
            FlowTopic = flowTopic,
            CapacityWatts = capacityWatts,
        });
        AddConnection(new ShipSystemConnectionInfo
        {
            ConnectionId = $"{connectionId}.device",
            FromNodeId = portNodeId,
            ToNodeId = targetNodeId,
            Kind = ShipSystemConnectionKind.PowerDistribution,
            FlowTopic = flowTopic,
            CapacityWatts = capacityWatts,
        });
    }

    public ShipSystemsTopologySnapshot CreateSnapshot(string shipId)
    {
        // Runtime ships have persistent IDs. A few isolated simulation/test ships are
        // intentionally anonymous; their topology must still be safe to project.
        ArgumentNullException.ThrowIfNull(shipId);
        var nodes = new List<ShipSystemNodeInfo>(_slots.Count + _fixedDevices.Count + _busPorts.Count);

        int order = 0;
        foreach (SlotEntry slot in _slots.Values)
        {
            nodes.Add(CreateSlotNode(slot, order++));
        }
        foreach (FixedEntry fixedDevice in _fixedDevices.Values)
        {
            nodes.Add(CreateComponentNode(
                fixedDevice.NodeId,
                fixedDevice.Label,
                ShipSystemNodeKind.FixedDevice,
                ShipSystemStage.Independent,
                parentNodeId: null,
                slotId: null,
                category: "ShipComputer",
                required: true,
                replaceable: false,
                fixedDevice.Component,
                fixedDevice.Order));
        }
        foreach (BusPortEntry port in _busPorts.Values.OrderBy(port => port.PortIndex))
        {
            nodes.Add(CreateComponentNode(
                port.NodeId,
                port.Component?.Name ?? $"BUS SLOT {port.PortIndex:00}",
                ShipSystemNodeKind.PowerBusSlot,
                ShipSystemStage.Conversion,
                port.ParentNodeId,
                port.NodeId,
                port.Component?.GetType().Name ?? "PowerBusSlot",
                required: false,
                replaceable: true,
                port.Component,
                port.PortIndex));
        }

        return new ShipSystemsTopologySnapshot
        {
            ShipId = shipId,
            Revision = Revision,
            Nodes = [.. nodes.OrderBy(node => node.Stage).ThenBy(node => node.Order)],
            Connections = [.. _connections.Values.OrderBy(connection => connection.ConnectionId, StringComparer.Ordinal)],
        };
    }

    public static string HullSlotNodeId(string slotId) => $"hull:{slotId}";

    public static string PowerBusPortNodeId(string busDeviceId, int portIndex)
        => $"bus:{busDeviceId}:port:{portIndex:00}";

    private ShipSystemNodeInfo CreateSlotNode(SlotEntry slot, int order)
        => CreateComponentNode(
            HullSlotNodeId(slot.Definition.SlotId),
            slot.InstalledLabel ?? slot.Definition.Label,
            ShipSystemNodeKind.ComponentSlot,
            StageFor(slot.Definition.Category),
            parentNodeId: null,
            slot.Definition.SlotId,
            slot.Definition.Category.ToString(),
            slot.Definition.Required,
            replaceable: true,
            slot.Component,
            order,
            slot.DeviceId);

    private static ShipSystemNodeInfo CreateComponentNode(
        string nodeId,
        string label,
        ShipSystemNodeKind kind,
        ShipSystemStage stage,
        string? parentNodeId,
        string? slotId,
        string category,
        bool required,
        bool replaceable,
        ShipComponent? component,
        int order,
        string? externalDeviceId = null)
        => new()
        {
            NodeId = nodeId,
            Label = label,
            Kind = kind,
            Stage = stage,
            ParentNodeId = parentNodeId,
            SlotId = slotId,
            DeviceId = component?.Name ?? externalDeviceId,
            Category = category,
            IsRequired = required,
            IsReplaceable = replaceable,
            CanSetPower = component?.CanSetPower ?? false,
            PowerCommandTopic = component?.CanSetPower == true ? component.PowerCommandTopic : null,
            Order = order,
            Metrics = component is null ? [] : [.. component.EngineeringMetrics],
        };

    private void AddConnection(ShipSystemConnectionInfo connection)
    {
        if (!_connections.TryAdd(connection.ConnectionId, connection))
            throw new InvalidOperationException(
                $"Duplicate ship power connection '{connection.ConnectionId}'.");
        Revision++;
    }

    private SlotEntry GetSlot(string slotId)
        => _slots.TryGetValue(slotId, out SlotEntry? slot)
            ? slot
            : throw new InvalidOperationException($"Unknown ship system slot '{slotId}'.");

    private string FindNodeIdByDevice(string deviceId)
    {
        foreach ((string slotId, SlotEntry slot) in _slots)
            if (string.Equals(slot.DeviceId, deviceId, StringComparison.Ordinal))
                return HullSlotNodeId(slotId);
        foreach (FixedEntry fixedDevice in _fixedDevices.Values)
            if (string.Equals(fixedDevice.Component.Name, deviceId, StringComparison.Ordinal))
                return fixedDevice.NodeId;
        foreach (BusPortEntry port in _busPorts.Values)
            if (string.Equals(port.Component?.Name, deviceId, StringComparison.Ordinal))
                return port.NodeId;
        throw new InvalidOperationException($"Ship topology contains no device '{deviceId}'.");
    }

    private void EnsureUniqueDevice(string deviceId, string? exceptSlotId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        foreach ((string slotId, SlotEntry slot) in _slots)
        {
            if (!string.Equals(slotId, exceptSlotId, StringComparison.Ordinal)
                && string.Equals(slot.DeviceId, deviceId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Duplicate ship device ID '{deviceId}'.");
            }
        }
        if (_fixedDevices.Values.Any(entry =>
                string.Equals(entry.Component.Name, deviceId, StringComparison.Ordinal))
            || _busPorts.Values.Any(entry =>
                string.Equals(entry.Component?.Name, deviceId, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"Duplicate ship device ID '{deviceId}'.");
        }
    }

    private static ShipSystemStage StageFor(SlotCategory category)
        => category switch
        {
            SlotCategory.PowerReactor => ShipSystemStage.Source,
            SlotCategory.PowerBus => ShipSystemStage.Distribution,
            SlotCategory.Connector or SlotCategory.Converter => ShipSystemStage.Conversion,
            SlotCategory.LifeSupport or SlotCategory.HeatSink or SlotCategory.CoolantSystem
                or SlotCategory.Exhaust or SlotCategory.FlyabilityMonitor
                or SlotCategory.InternalLights or SlotCategory.ExternalLights
                or SlotCategory.Cargo => ShipSystemStage.Independent,
            _ => ShipSystemStage.Consumer,
        };
}
