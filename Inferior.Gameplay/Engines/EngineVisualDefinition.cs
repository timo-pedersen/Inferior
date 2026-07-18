using Inferior.Core.Math;

namespace Inferior.Gameplay.Engines;

public sealed class EngineVisualDefinition
{
    public EngineVisualDefinition(
        DVec3 glowColour,
        float idleIntensity,
        float thrustIntensity,
        float velocityCorrectionIntensity,
        float boostIntensity,
        float instabilityAmount)
    {
        if (!IsFiniteColour(glowColour))
            throw new ArgumentOutOfRangeException(nameof(glowColour));
        if (!IsNonNegativeFinite(idleIntensity)
            || !IsNonNegativeFinite(thrustIntensity)
            || !IsNonNegativeFinite(velocityCorrectionIntensity)
            || !IsNonNegativeFinite(boostIntensity))
        {
            throw new ArgumentOutOfRangeException(
                nameof(idleIntensity),
                "Engine glow intensities must be finite and non-negative.");
        }
        if (!float.IsFinite(instabilityAmount)
            || instabilityAmount < 0f
            || instabilityAmount > 1f)
        {
            throw new ArgumentOutOfRangeException(nameof(instabilityAmount));
        }

        GlowColour = glowColour;
        IdleIntensity = idleIntensity;
        ThrustIntensity = thrustIntensity;
        VelocityCorrectionIntensity = velocityCorrectionIntensity;
        BoostIntensity = boostIntensity;
        InstabilityAmount = instabilityAmount;
    }

    public DVec3 GlowColour { get; }
    public float IdleIntensity { get; }
    public float ThrustIntensity { get; }
    public float VelocityCorrectionIntensity { get; }
    public float BoostIntensity { get; }
    public float InstabilityAmount { get; }

    private static bool IsFiniteColour(DVec3 value)
        => double.IsFinite(value.X) && value.X >= 0.0
        && double.IsFinite(value.Y) && value.Y >= 0.0
        && double.IsFinite(value.Z) && value.Z >= 0.0;

    private static bool IsNonNegativeFinite(float value)
        => float.IsFinite(value) && value >= 0f;
}

public enum EngineVisualMode
{
    Idle,
    Thrust,
    Boost,
    VelocityCorrection,
}

public readonly record struct EngineVisualState(
    EngineVisualMode Mode,
    float Output)
{
    public static EngineVisualState Idle { get; } = new(EngineVisualMode.Idle, 0f);
    public static EngineVisualState Thrust { get; } = new(EngineVisualMode.Thrust, 1f);
    public static EngineVisualState Boost { get; } = new(EngineVisualMode.Boost, 1f);
    public static EngineVisualState VelocityCorrection { get; } =
        new(EngineVisualMode.VelocityCorrection, 1f);

    public EngineVisualState Validate()
    {
        if (!Enum.IsDefined(Mode) || !IsUnitFinite(Output))
            throw new ArgumentOutOfRangeException(
                nameof(EngineVisualState),
                "Engine visual state must have a defined mode and output within [0, 1].");
        if (Mode == EngineVisualMode.Idle && Output != 0f)
            throw new ArgumentOutOfRangeException(
                nameof(EngineVisualState),
                "Idle engine visual state must have zero output.");
        return this;
    }

    private static bool IsUnitFinite(float value)
        => float.IsFinite(value) && value >= 0f && value <= 1f;
}
