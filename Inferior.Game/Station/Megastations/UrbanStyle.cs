namespace Inferior.Game.StationGen.Megastations;

public sealed record MegastationUrbanStyle(
    float OverallDensity,
    int BaseDepthOffset,
    float TowerFrequency,
    int TowerWidthBias,
    float HeightContrast,
    float TrenchFrequency,
    float CourtyardFrequency,
    float EdgeSpineStrength,
    float CornerMassStrength,
    float FragmentationTendency)
{
    public static MegastationUrbanStyle Generate(int rootSeed)
    {
        var rng = new Random(MegastationSeed.Derive(rootSeed, "station-wide urban style"));
        return new MegastationUrbanStyle(
            OverallDensity: Next(rng, 0.85f, 1.20f),
            BaseDepthOffset: rng.Next(-1, 2),
            TowerFrequency: Next(rng, 0.75f, 1.35f),
            TowerWidthBias: rng.Next(-1, 2),
            HeightContrast: Next(rng, 0.80f, 1.35f),
            TrenchFrequency: Next(rng, 0.75f, 1.35f),
            CourtyardFrequency: Next(rng, 0.75f, 1.30f),
            EdgeSpineStrength: Next(rng, 0.65f, 1.40f),
            CornerMassStrength: Next(rng, 0.70f, 1.45f),
            FragmentationTendency: Next(rng, 0.20f, 0.75f));
    }

    private static float Next(Random rng, float min, float max)
        => min + (float)rng.NextDouble() * (max - min);
}

public static class MegastationFaceSettings
{
    public static MegastationPrototypeSettings ForPatch(
        MegastationPrototypeSettings settings,
        MegastationUrbanStyle style,
        SliceGrid grid,
        SurfacePatch patch,
        int rootSeed)
    {
        if (patch.Direction == settings.UrbanPatchNormal)
            return settings;

        var rng = new Random(MegastationSeed.Derive(rootSeed, $"face-style:{patch.Id}"));
        float density = style.OverallDensity * Next(rng, 0.80f, 1.25f);
        int districtMin = Math.Max(2, (int)MathF.Round(settings.DistrictCount.Min * density));
        int districtMax = Math.Max(districtMin, (int)MathF.Round(settings.DistrictCount.Max * density));

        int baseShift = style.BaseDepthOffset + rng.Next(-1, 2);
        int baseMin = Math.Clamp(settings.BaseUrbanDepth.Min + baseShift, 1, settings.MaximumUrbanDepth);
        int baseMax = Math.Clamp(settings.BaseUrbanDepth.Max + baseShift, baseMin, settings.MaximumUrbanDepth);

        int towerMin = Math.Max(0, (int)MathF.Round(settings.TowerCountPerDistrict.Min * style.TowerFrequency));
        int towerMax = Math.Max(towerMin, (int)MathF.Round(settings.TowerCountPerDistrict.Max * style.TowerFrequency * Next(rng, 0.80f, 1.25f)));

        int radiusMin = Math.Max(1, settings.TowerRadiusCells.Min + style.TowerWidthBias + rng.Next(-1, 2));
        int radiusMax = Math.Max(radiusMin, settings.TowerRadiusCells.Max + style.TowerWidthBias + rng.Next(-1, 2));

        int maxDepth = Math.Clamp(
            (int)MathF.Round(settings.MaximumUrbanDepth * style.HeightContrast * Next(rng, 0.85f, 1.15f)),
            baseMax + 1,
            Math.Max(baseMax + 1, Direction.AvailableLayers(grid, patch.Direction)));
        maxDepth = Math.Min(maxDepth, Direction.AvailableLayers(grid, patch.Direction));
        baseMax = Math.Min(baseMax, maxDepth);
        baseMin = Math.Min(baseMin, baseMax);

        return settings with
        {
            DistrictCount = new IntRange(districtMin, districtMax),
            BaseUrbanDepth = new IntRange(baseMin, baseMax),
            MaximumUrbanDepth = maxDepth,
            TowerCountPerDistrict = new IntRange(towerMin, towerMax),
            TowerRadiusCells = new IntRange(radiusMin, radiusMax),
            TrenchDensity = Scale(settings.TrenchDensity, style.TrenchFrequency * Next(rng, 0.75f, 1.25f)),
            CourtyardDensity = Scale(settings.CourtyardDensity, style.CourtyardFrequency * Next(rng, 0.75f, 1.25f)),
        };
    }

    private static FloatRange Scale(FloatRange range, float factor)
        => new(range.Min * factor, range.Max * factor);

    private static float Next(Random rng, float min, float max)
        => min + (float)rng.NextDouble() * (max - min);
}
