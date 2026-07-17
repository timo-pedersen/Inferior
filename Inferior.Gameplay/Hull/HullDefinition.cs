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
    public required string HullTypeId { get; init; }

    /// <summary>Human-readable name shown in the fitting screen and shipyard.</summary>
    public required string DisplayName { get; init; }

    public required ShipSizeClass SizeClass { get; init; }

    /// <summary>Hull mass in kg, excluding all components.</summary>
    public required double HullMass { get; init; }

    /// <summary>
    /// Camera/cockpit eye point offset from the ship's centre of mass in ship-local space.
    /// The camera follows the cockpit, not CoM.
    /// </summary>
    public required DVec3 CockpitOffset { get; init; }

    /// <summary>Full hull-local cockpit camera pose, including its authored view orientation.</summary>
    public required CockpitPoseDefinition CockpitPose { get; init; }

    /// <summary>All component slots available on this hull.</summary>
    public required IReadOnlyList<HullSlot> Slots { get; init; }

    public HullDimensions? Dimensions { get; init; }
    public string? PrimaryDesignBias { get; init; }
    public string? SecondaryDesignBias { get; init; }
    public CargoArrangementDefinition? CargoArrangement { get; init; }

    /// <summary>CPU-side semantic geometry used by the ship visual system.</summary>
    public SemanticHullGeometry? VisualGeometry { get; init; }

    /// <summary>Upward lift coefficient per unit density per (m/s)^2 of forward speed.</summary>
    public double AerodynamicLift { get; init; } = 0.0;

    /// <summary>Drag coefficient for motion in ship-forward/backward direction.</summary>
    public double AerodynamicBrakeFront { get; init; } = 0.0;

    /// <summary>Drag coefficient for motion in ship-lateral/vertical direction.</summary>
    public double AerodynamicBrakeLateral { get; init; } = 0.0;
}
