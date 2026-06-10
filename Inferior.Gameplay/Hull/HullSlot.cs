namespace Inferior.Gameplay.Hull;

/// <summary>
/// A single slot on a ship hull where a component can be installed.
///
/// SlotId is the stable key used in persistence (InstalledComponentRecord.SlotId).
/// Category restricts which component types fit. MaxComponentClass is a soft cap
/// enforced by the fitting screen, not the simulation.
/// </summary>
public record HullSlot
{
    /// <summary>Stable persistence key — never rename once ships are saved.</summary>
    public required string SlotId { get; init; }

    /// <summary>Display name shown in the fitting screen.</summary>
    public required string Label { get; init; }

    /// <summary>Which category of component this slot accepts.</summary>
    public required SlotCategory Category { get; init; }

    /// <summary>
    /// Highest component class that fits this slot.
    /// 0 = uncapped (rare — for utility slots that accept any class).
    /// </summary>
    public int MaxComponentClass { get; init; } = 0;

    /// <summary>
    /// If true, the ship cannot be considered flyable without something installed here.
    /// Used by FlyabilityMonitor checks.
    /// </summary>
    public bool Required { get; init; }
}
