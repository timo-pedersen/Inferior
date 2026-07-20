namespace Inferior.Gameplay.Engines;

public enum EngineIntentRating
{
    Low,
    Medium,
    High,
}

/// <summary>Non-simulated design metadata reserved for the eventual engine parameter model.</summary>
public sealed record EngineDesignIntent(
    string Role,
    EngineIntentRating ForwardThrust,
    EngineIntentRating FuelEfficiency,
    EngineIntentRating PowerEfficiency,
    EngineIntentRating ThermalMass,
    EngineIntentRating HeatBuildUp,
    EngineIntentRating Reliability,
    EngineIntentRating AbuseTolerance,
    EngineIntentRating MaintenanceDifficulty,
    EngineIntentRating Cost,
    bool AlphaRedProduction);
