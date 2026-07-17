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

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (VisualGeometry is null)
            return errors;

        errors.AddRange(VisualGeometry.Validate());

        var slotsById = Slots.ToDictionary(slot => slot.SlotId, StringComparer.Ordinal);
        var engineSlots = Slots.Where(slot => slot.Category == SlotCategory.Engine).Select(slot => slot.SlotId).ToHashSet(StringComparer.Ordinal);
        var enginePorts = VisualGeometry.AttachmentPorts
            .Where(port => port.Capabilities.HasFlag(AttachmentCapability.Engine))
            .ToArray();

        foreach (var port in VisualGeometry.AttachmentPorts)
        {
            if (string.IsNullOrWhiteSpace(port.ComponentSlotId))
                continue;

            if (!slotsById.TryGetValue(port.ComponentSlotId, out var slot))
            {
                errors.Add($"Attachment port '{port.PortId}' references unknown component slot '{port.ComponentSlotId}'.");
            }
            else if (port.Capabilities.HasFlag(AttachmentCapability.Engine) && slot.Category != SlotCategory.Engine)
            {
                errors.Add($"Engine attachment port '{port.PortId}' references non-engine slot '{port.ComponentSlotId}'.");
            }
        }

        var boundEngineSlots = new HashSet<string>(StringComparer.Ordinal);
        foreach (var port in enginePorts)
        {
            if (string.IsNullOrWhiteSpace(port.ComponentSlotId))
            {
                errors.Add($"Engine attachment port '{port.PortId}' has no component slot binding.");
                continue;
            }

            if (!boundEngineSlots.Add(port.ComponentSlotId))
                errors.Add($"Multiple engine attachment ports reference component slot '{port.ComponentSlotId}'.");
        }

        if (engineSlots.Count > 0 || enginePorts.Length > 0)
        {
            foreach (string slotId in engineSlots)
            {
                if (!boundEngineSlots.Contains(slotId))
                    errors.Add($"Engine slot '{slotId}' has no matching engine attachment port.");
            }

            foreach (string slotId in boundEngineSlots)
            {
                if (!engineSlots.Contains(slotId))
                    errors.Add($"Engine attachment port references non-engine slot '{slotId}'.");
            }
        }

        var landingPorts = VisualGeometry.AttachmentPorts
            .Where(port => port.Capabilities.HasFlag(AttachmentCapability.LandingGear))
            .ToArray();
        if (landingPorts.Length is > 0 and not 3)
            errors.Add($"Semantic hull defines {landingPorts.Length} landing gear attachment ports, expected 3.");

        return errors;
    }
}
