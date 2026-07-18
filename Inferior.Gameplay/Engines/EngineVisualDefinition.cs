using Inferior.Core.Math;

namespace Inferior.Gameplay.Engines;

public sealed class EngineVisualDefinition
{
    public EngineVisualDefinition(
        DVec3 glowColour,
        float idleIntensity,
        float thrustIntensity,
        float brakeIntensity,
        float boostIntensity,
        float flickerAmount)
    {
        if (!IsFiniteColour(glowColour))
            throw new ArgumentOutOfRangeException(nameof(glowColour));
        if (!IsNonNegativeFinite(idleIntensity)
            || !IsNonNegativeFinite(thrustIntensity)
            || !IsNonNegativeFinite(brakeIntensity)
            || !IsNonNegativeFinite(boostIntensity))
        {
            throw new ArgumentOutOfRangeException(
                nameof(idleIntensity),
                "Engine glow intensities must be finite and non-negative.");
        }
        if (!float.IsFinite(flickerAmount) || flickerAmount < 0f || flickerAmount > 1f)
            throw new ArgumentOutOfRangeException(nameof(flickerAmount));

        GlowColour = glowColour;
        IdleIntensity = idleIntensity;
        ThrustIntensity = thrustIntensity;
        BrakeIntensity = brakeIntensity;
        BoostIntensity = boostIntensity;
        FlickerAmount = flickerAmount;
    }

    public DVec3 GlowColour { get; }
    public float IdleIntensity { get; }
    public float ThrustIntensity { get; }
    public float BrakeIntensity { get; }
    public float BoostIntensity { get; }
    public float FlickerAmount { get; }

    private static bool IsFiniteColour(DVec3 value)
        => double.IsFinite(value.X) && value.X >= 0.0
        && double.IsFinite(value.Y) && value.Y >= 0.0
        && double.IsFinite(value.Z) && value.Z >= 0.0;

    private static bool IsNonNegativeFinite(float value)
        => float.IsFinite(value) && value >= 0f;
}

public readonly record struct EngineVisualState(
    float Output,
    float Brake,
    float Boost)
{
    public static EngineVisualState Idle { get; } = new(0.1f, 0f, 0f);
    public static EngineVisualState Thrust { get; } = new(1f, 0f, 0f);
    public static EngineVisualState Braking { get; } = new(0.35f, 1f, 0f);
    public static EngineVisualState Boosting { get; } = new(1f, 0f, 1f);

    public EngineVisualState Validate()
    {
        if (!IsUnitFinite(Output) || !IsUnitFinite(Brake) || !IsUnitFinite(Boost))
            throw new ArgumentOutOfRangeException(
                nameof(EngineVisualState),
                "Engine visual-state channels must be finite and within [0, 1].");
        return this;
    }

    private static bool IsUnitFinite(float value)
        => float.IsFinite(value) && value >= 0f && value <= 1f;
}
