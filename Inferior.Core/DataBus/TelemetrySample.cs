namespace Inferior.Core.DataBus;

/// <summary>
/// One telemetry observation. SimulationTime is session-local simulation seconds;
/// Sequence is an increasing session-local channel counter and is not persistent identity.
/// </summary>
public readonly record struct TelemetrySample<T>(
    T Value,
    double SimulationTime,
    ulong Sequence);
