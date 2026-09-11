using System.Collections.Immutable;

namespace Inferior.Core.DataBus;

public enum TelemetryValueKind
{
    Scalar,
    Vector,
    Spectrum,
}

public enum PhysicalQuantity
{
    Unspecified,
    Dimensionless,
    NormalizedRatio,
    Boolean,
    Count,
    Distance,
    Speed,
    Acceleration,
    Force,
    Pressure,
    Temperature,
    Power,
    Energy,
    Time,
    Angle,
    Direction,
    MagneticFluxDensity,
    Irradiance,
}

public enum TelemetryReferenceFrame
{
    NotApplicable,
    Unspecified,
    Universe,
    SystemEcliptic,
    ReferenceBody,
    ShipLocal,
    DeviceLocal,
}

public enum PublicationMode
{
    EveryTick,
    Periodic,
    OnChange,
    OnCommand,
}

public enum TelemetryBandSeverity
{
    Warning,
    Critical,
}

public enum DeviceOperationalStatus
{
    Unavailable,
    PowerOff,
    PowerOn,
    Initializing,
    Running,
    Faulted,
}

public readonly record struct TelemetryBand(
    RangeValue Range,
    TelemetryBandSeverity Severity);

public readonly record struct PublicationInfo(
    PublicationMode Mode,
    double? NominalFrequencyHz = null)
{
    public PublicationInfo Validate()
    {
        if (NominalFrequencyHz is <= 0.0)
            throw new ArgumentOutOfRangeException(
                nameof(NominalFrequencyHz),
                "A specified report frequency must be positive.");
        return this;
    }
}

public readonly record struct PowerProfile(
    double IdleWatts,
    double ActiveWatts,
    double ActivationEnergyJoules = 0.0,
    double ActivationDurationSeconds = 0.0);

/// <summary>Immutable description of one telemetry topic.</summary>
public sealed record TelemetryInfo
{
    public required string Topic { get; init; }
    public required string DeviceId { get; init; }
    public required TelemetryValueKind ValueKind { get; init; }
    public PhysicalQuantity Quantity { get; init; } = PhysicalQuantity.Unspecified;
    public TelemetryReferenceFrame ReferenceFrame { get; init; } = TelemetryReferenceFrame.NotApplicable;
    public RangeValue? OperatingRange { get; init; }
    public RangeValue? SuggestedDisplayRange { get; init; }
    public ImmutableArray<TelemetryBand> Bands { get; init; } = [];
    public PublicationInfo Publication { get; init; } = new(PublicationMode.OnChange);
    public TopicPolicy TopicPolicy { get; init; } = TopicPolicy.LatestState;
}

/// <summary>Immutable description of one sensor or component and its bus surface.</summary>
public sealed record DeviceInfo
{
    public required string DeviceId { get; init; }
    public ImmutableArray<string> PublishedTopics { get; init; } = [];
    public ImmutableArray<string> CommandTopics { get; init; } = [];
    public PowerProfile Power { get; init; } = new(0.0, 0.0);
}

/// <summary>Current observable operational state of one sensor or component.</summary>
public readonly record struct DeviceState(
    string DeviceId,
    DeviceOperationalStatus Status,
    double Damage,
    double Efficiency,
    double SimulationTime);
