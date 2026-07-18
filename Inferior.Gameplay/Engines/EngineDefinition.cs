using Inferior.Core.Math;

namespace Inferior.Gameplay.Engines;

/// <summary>Immutable manufactured engine family shared by all variants and instances.</summary>
public sealed class EngineDefinition
{
    public EngineDefinition(
        string familyId,
        string displayName,
        DVec3 nominalEnvelopeMeters,
        double dryMassKg,
        EngineVisualGeometry? visualGeometry = null,
        EngineDesignIntent? designIntent = null)
    {
        if (string.IsNullOrWhiteSpace(familyId))
            throw new ArgumentException("Engine family id must not be empty.", nameof(familyId));
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Engine display name must not be empty.", nameof(displayName));
        if (!IsPositiveFinite(nominalEnvelopeMeters))
            throw new ArgumentOutOfRangeException(
                nameof(nominalEnvelopeMeters),
                "Engine envelope dimensions must be finite and positive.");
        if (!double.IsFinite(dryMassKg) || dryMassKg <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(dryMassKg));

        FamilyId = familyId;
        DisplayName = displayName;
        NominalEnvelopeMeters = nominalEnvelopeMeters;
        DryMassKg = dryMassKg;
        VisualGeometry = visualGeometry;
        DesignIntent = designIntent;
    }

    public string FamilyId { get; }
    public string DisplayName { get; }
    public DVec3 NominalEnvelopeMeters { get; }
    public double DryMassKg { get; }
    public EngineVisualGeometry? VisualGeometry { get; }
    public EngineDesignIntent? DesignIntent { get; }

    private static bool IsPositiveFinite(DVec3 value)
        => double.IsFinite(value.X) && value.X > 0.0
        && double.IsFinite(value.Y) && value.Y > 0.0
        && double.IsFinite(value.Z) && value.Z > 0.0;
}
