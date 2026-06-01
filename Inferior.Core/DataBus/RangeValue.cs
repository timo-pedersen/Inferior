namespace Inferior.Core.DataBus;

/// <summary>
/// Operating envelope for an instrument value. Published at startup and when ranges change.
/// </summary>
public readonly record struct RangeValue(double Low, double High);
