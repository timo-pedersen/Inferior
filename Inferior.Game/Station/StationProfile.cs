using Inferior.Core.Random;

namespace Inferior.Game.StationGen;

public enum StationEconomy
{
    Industrial,
    Commercial,
    Military,
    Scientific,
    Luxury,
    Agricultural,
    Independent,
}

public sealed class StationProfile
{
    public StationEconomy Economy    { get; init; }
    public int            Age        { get; init; }   // years, 5–200
    public float          Wealth     { get; init; }   // 0.0–1.0
    public float          Population { get; init; }   // 0.0–1.0

    public static StationProfile Generate(int seed, StationScale scale)
    {
        var rng = new SeededRandom(seed ^ 0x5A3C_F17B);

        int economyCount = Enum.GetValues<StationEconomy>().Length;
        var economy      = (StationEconomy)rng.NextInt(0, economyCount - 1);

        float popScale = scale == StationScale.Outpost ? 0.35f : 1.0f;

        return new StationProfile
        {
            Economy    = economy,
            Age        = rng.NextInt(5, 200),
            Wealth     = rng.NextFloat(),
            Population = rng.NextFloat() * popScale,
        };
    }
}
