using Microsoft.Xna.Framework;

namespace Inferior.Game.StationGen.Megastations;

public enum MegastationPrototypeSelectionMode
{
    Canonical,
    ForceStarterStation,
    Frequent,
}

public sealed record MegastationDevelopmentSelection(
    MegastationPrototypeSelectionMode Mode,
    double MegastationProbability,
    bool ForceStarterStation);

public sealed record MegastationPrototypeSettings
{
    public static MegastationPrototypeSettings Default { get; } = new();
    public static MegastationDevelopmentSelection DevelopmentSelection { get; } =
        new(MegastationPrototypeSelectionMode.Frequent, MegastationProbability: 0.50, ForceStarterStation: true);

    public int GeneratorVersion { get; init; } = 2;
    public int SeedCompatibilityVersion { get; init; } = 1;
    public int PositiveYUrbanSeedVersion { get; init; } = 1;
    public int FaceUrbanAlgorithmVersion { get; init; } = 1;
    public int EdgeAlgorithmVersion { get; init; } = 1;
    public int CornerAlgorithmVersion { get; init; } = 1;

    public Vector3 CoreDimensions { get; init; } = new(1400f, 520f, 900f);
    public IntRange CoreXSlices { get; init; } = new(26, 34);
    public IntRange CoreYSlices { get; init; } = new(12, 16);
    public IntRange CoreZSlices { get; init; } = new(18, 26);
    public IntRange PositiveGrowthLayers { get; init; } = new(8, 12);
    public IntRange NegativeGrowthLayers { get; init; } = new(2, 4);
    public FloatRange SliceJitter { get; init; } = new(0.55f, 1.65f);

    public GridDirection UrbanPatchNormal { get; init; } = GridDirection.PositiveY;
    public int ReservedPatchEdgeCells { get; init; } = 2;
    public IntRange DistrictCount { get; init; } = new(8, 13);
    public int MinimumDistrictCells { get; init; } = 4;
    public IntRange BaseUrbanDepth { get; init; } = new(2, 4);
    public int MaximumUrbanDepth { get; init; } = 10;
    public IntRange TowerCountPerDistrict { get; init; } = new(1, 4);
    public IntRange TowerRadiusCells { get; init; } = new(2, 6);
    public FloatRange TrenchDensity { get; init; } = new(0.06f, 0.14f);
    public FloatRange CourtyardDensity { get; init; } = new(0.04f, 0.10f);
}

public readonly record struct IntRange(int Min, int Max)
{
    public int Roll(Random rng) => rng.Next(Min, Max + 1);
}

public readonly record struct FloatRange(float Min, float Max)
{
    public float Roll(Random rng) => Min + (float)rng.NextDouble() * (Max - Min);
}
