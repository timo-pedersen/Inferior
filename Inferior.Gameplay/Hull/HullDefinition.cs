using Inferior.Core.Math;
using Inferior.Gameplay.Ship;

namespace Inferior.Gameplay.Hull;

/// <summary>
/// Immutable template for a ship hull class.
/// Defines what slots exist, the hull's base mass, cockpit offset, and size class.
///
/// Each Hull has exactly one entry in <see cref="HullDefinitionLibrary"/>.
/// Ship instances reference their hull via <see cref="HullTypeId"/>.
/// </summary>
public sealed class HullDefinition
{
    /// <summary>Stable persistence key. Must match Ship.HullTypeId.</summary>
    public required string HullTypeId   { get; init; }

    /// <summary>Human-readable name shown in the fitting screen and shipyard.</summary>
    public required string DisplayName  { get; init; }

    public required ShipSizeClass SizeClass { get; init; }

    /// <summary>Hull mass in kg, excluding all components.</summary>
    public required double HullMass { get; init; }

    /// <summary>
    /// Camera/cockpit eye point offset from the ship's centre of mass in ship-local space.
    /// The camera follows the cockpit, not CoM.
    /// </summary>
    public required DVec3 CockpitOffset { get; init; }

    /// <summary>All component slots available on this hull.</summary>
    public required IReadOnlyList<HullSlot> Slots { get; init; }
}
