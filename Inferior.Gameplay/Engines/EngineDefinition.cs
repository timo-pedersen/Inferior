using Inferior.Core.Math;

namespace Inferior.Gameplay.Engines;

public readonly record struct EngineHarmonyOutput(
    int SelectedHarmony,
    int HarmonyCount,
    double NormalizedPosition,
    double Curve,
    double ThrustMultiplier,
    double SpeedCeilingMps,
    double MaximumForwardThrustN,
    double MaximumReverseThrustN,
    double MaximumLateralThrustN,
    double MaximumLiftThrustN,
    double MaximumRotationalTorqueNm);

/// <summary>Immutable manufactured engine family shared by all variants and instances.</summary>
public sealed class EngineDefinition
{
    public EngineDefinition(
        string familyId,
        string displayName,
        DVec3 nominalEnvelopeMeters,
        double dryMassKg,
        double maximumForwardThrustN,
        double reverseThrustFraction,
        double lateralThrustFraction,
        double liftThrustFraction,
        double rotationalTorqueNm,
        int harmonyCount,
        double minimumThrustFraction,
        double minimumSpeedCeilingMps,
        double maximumSpeedCeilingMps,
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
        if (!double.IsFinite(maximumForwardThrustN) || maximumForwardThrustN <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(maximumForwardThrustN));
        ValidateFraction(reverseThrustFraction, nameof(reverseThrustFraction));
        ValidateFraction(lateralThrustFraction, nameof(lateralThrustFraction));
        ValidateFraction(liftThrustFraction, nameof(liftThrustFraction));
        if (!double.IsFinite(rotationalTorqueNm) || rotationalTorqueNm < 0.0)
            throw new ArgumentOutOfRangeException(nameof(rotationalTorqueNm));
        if (harmonyCount < 2)
            throw new ArgumentOutOfRangeException(nameof(harmonyCount));
        if (!double.IsFinite(minimumThrustFraction)
            || minimumThrustFraction <= 0.0
            || minimumThrustFraction > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumThrustFraction));
        }
        if (!double.IsFinite(minimumSpeedCeilingMps) || minimumSpeedCeilingMps <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(minimumSpeedCeilingMps));
        if (!double.IsFinite(maximumSpeedCeilingMps)
            || maximumSpeedCeilingMps < minimumSpeedCeilingMps)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumSpeedCeilingMps));
        }

        FamilyId = familyId;
        DisplayName = displayName;
        NominalEnvelopeMeters = nominalEnvelopeMeters;
        DryMassKg = dryMassKg;
        MaximumForwardThrustN = maximumForwardThrustN;
        ReverseThrustFraction = reverseThrustFraction;
        LateralThrustFraction = lateralThrustFraction;
        LiftThrustFraction = liftThrustFraction;
        RotationalTorqueNm = rotationalTorqueNm;
        HarmonyCount = harmonyCount;
        MinimumThrustFraction = minimumThrustFraction;
        MinimumSpeedCeilingMps = minimumSpeedCeilingMps;
        MaximumSpeedCeilingMps = maximumSpeedCeilingMps;
        VisualGeometry = visualGeometry;
        DesignIntent = designIntent;
        VisualDefinition = visualDefinition;
    }

    public string FamilyId { get; }
    public string DisplayName { get; }
    public DVec3 NominalEnvelopeMeters { get; }
    public double DryMassKg { get; }
    public double MaximumForwardThrustN { get; }
    public double ReverseThrustFraction { get; }
    public double LateralThrustFraction { get; }
    public double LiftThrustFraction { get; }
    public double RotationalTorqueNm { get; }
    public int HarmonyCount { get; }
    public double MinimumThrustFraction { get; }
    public double MinimumSpeedCeilingMps { get; }
    public double MaximumSpeedCeilingMps { get; }
    public EngineVisualGeometry? VisualGeometry { get; }
    public EngineDesignIntent? DesignIntent { get; }
    public EngineVisualDefinition? VisualDefinition { get; }

    public EngineHarmonyOutput ResolveHarmony(int selectedHarmony)
    {
        if (selectedHarmony < 1 || selectedHarmony > HarmonyCount)
            throw new ArgumentOutOfRangeException(nameof(selectedHarmony));

        double x = (double)(selectedHarmony - 1) / (HarmonyCount - 1);
        double curve = x * x;
        double multiplier = MinimumThrustFraction
            + (1.0 - MinimumThrustFraction) * curve;
        double speedCeiling = MinimumSpeedCeilingMps
            + (MaximumSpeedCeilingMps - MinimumSpeedCeilingMps) * curve;
        double availableForward = MaximumForwardThrustN * multiplier;
        return new EngineHarmonyOutput(
            selectedHarmony,
            HarmonyCount,
            x,
            curve,
            multiplier,
            speedCeiling,
            availableForward,
            availableForward * ReverseThrustFraction,
            availableForward * LateralThrustFraction,
            availableForward * LiftThrustFraction,
            RotationalTorqueNm * multiplier);
    }

    private static bool IsPositiveFinite(DVec3 value)
        => double.IsFinite(value.X) && value.X > 0.0
        && double.IsFinite(value.Y) && value.Y > 0.0
        && double.IsFinite(value.Z) && value.Z > 0.0;

    private static void ValidateFraction(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value < 0.0 || value > 1.0)
            throw new ArgumentOutOfRangeException(parameterName);
    }
}
