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
        double forwardThrustN,
        double maneuveringThrustN,
        double rotationalTorqueNm,
        EngineVisualGeometry? visualGeometry = null,
        EngineDesignIntent? designIntent = null,
        EngineVisualDefinition? visualDefinition = null)
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
        if (!double.IsFinite(forwardThrustN) || forwardThrustN <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(forwardThrustN));
        if (!double.IsFinite(maneuveringThrustN) || maneuveringThrustN < 0.0)
            throw new ArgumentOutOfRangeException(nameof(maneuveringThrustN));
        if (!double.IsFinite(rotationalTorqueNm) || rotationalTorqueNm < 0.0)
            throw new ArgumentOutOfRangeException(nameof(rotationalTorqueNm));

        FamilyId = familyId;
        DisplayName = displayName;
        NominalEnvelopeMeters = nominalEnvelopeMeters;
        DryMassKg = dryMassKg;
        ForwardThrustN = forwardThrustN;
        ManeuveringThrustN = maneuveringThrustN;
        RotationalTorqueNm = rotationalTorqueNm;
        VisualGeometry = visualGeometry;
        DesignIntent = designIntent;
        VisualDefinition = visualDefinition;
    }

    public string FamilyId { get; }
    public string DisplayName { get; }
    public DVec3 NominalEnvelopeMeters { get; }
    public double DryMassKg { get; }
    public double ForwardThrustN { get; }
    public double ManeuveringThrustN { get; }
    public double RotationalTorqueNm { get; }
    public EngineVisualGeometry? VisualGeometry { get; }
    public EngineDesignIntent? DesignIntent { get; }
    public EngineVisualDefinition? VisualDefinition { get; }

    private static bool IsPositiveFinite(DVec3 value)
        => double.IsFinite(value.X) && value.X > 0.0
        && double.IsFinite(value.Y) && value.Y > 0.0
        && double.IsFinite(value.Z) && value.Z > 0.0;
}
