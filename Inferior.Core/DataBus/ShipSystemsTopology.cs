using System.Collections.Immutable;

namespace Inferior.Core.DataBus;

/// <summary>Functional position of a device or empty slot in the ship power diagram.</summary>
public enum ShipSystemStage
{
    Source,
    Distribution,
    Conversion,
    Consumer,
    Independent,
}

public enum ShipSystemNodeKind
{
    ComponentSlot,
    PowerBusSlot,
    FixedDevice,
}

public enum ShipSystemConnectionKind
{
    PowerSource,
    PowerDistribution,
}

/// <summary>Meaning of one live telemetry value displayed on an engineering node.</summary>
public enum ShipSystemMetricRole
{
    PowerInput,
    PowerOutput,
    PowerFlow,
    CapacitorFill,
    HeatGeneration,
    ThermalLoad,
    Temperature,
    CoolantLevel,
    HeatSinkFill,
    HeatDissipation,
}

public readonly record struct ShipSystemMetricBinding(
    ShipSystemMetricRole Role,
    string Topic);

/// <summary>
/// One installed device, empty hull slot, bus connection slot, or fixed ship system.
/// NodeId is stable within the owning ship. DeviceId is null when the slot is empty.
/// </summary>
public sealed record ShipSystemNodeInfo
{
    public required string NodeId { get; init; }
    public required string Label { get; init; }
    public required ShipSystemNodeKind Kind { get; init; }
    public required ShipSystemStage Stage { get; init; }
    public string? ParentNodeId { get; init; }
    public string? SlotId { get; init; }
    public string? DeviceId { get; init; }
    public string Category { get; init; } = "Generic";
    public bool IsRequired { get; init; }
    public bool IsReplaceable { get; init; } = true;
    public bool CanSetPower { get; init; }
    public string? PowerCommandTopic { get; init; }
    public int Order { get; init; }
    public ImmutableArray<ShipSystemMetricBinding> Metrics { get; init; } = [];
}

/// <summary>One directed functional power connection in the owning ship.</summary>
public sealed record ShipSystemConnectionInfo
{
    public required string ConnectionId { get; init; }
    public required string FromNodeId { get; init; }
    public required string ToNodeId { get; init; }
    public required ShipSystemConnectionKind Kind { get; init; }
    public string? FlowTopic { get; init; }
    public double? CapacityWatts { get; init; }
}

/// <summary>
/// Atomic immutable projection of one ship's authoritative systems topology. The ship owns
/// and mutates the real configuration; this snapshot is retained presentation data only.
/// </summary>
public sealed record ShipSystemsTopologySnapshot
{
    public required string ShipId { get; init; }
    public required int Revision { get; init; }
    public ImmutableArray<ShipSystemNodeInfo> Nodes { get; init; } = [];
    public ImmutableArray<ShipSystemConnectionInfo> Connections { get; init; } = [];
}
