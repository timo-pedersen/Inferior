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
    // Age/Wealth/Population: generated but not read anywhere outside this file today
    // (Report S2a §4/§5). Read by S2b-2 (Age → wear/grime tier); left as-is per Brief
    // S2b-1 — not this brief's material.
    public int            Age        { get; init; }   // years, 5–200
    public float          Wealth     { get; init; }   // 0.0–1.0
    public float          Population { get; init; }   // 0.0–1.0

    public static StationProfile Generate(int seed, StationScale scale)
    {
        var rng = new SeededRandom(seed ^ 0x5A3C_F17B);

        int economyCount = Enum.GetValues<StationEconomy>().Length;
        // NextInt(0, economyCount - 1) excludes the last enum value (Independent) —
        // unreachable today (Report S2a §5). Read by S2b-2 (economy → palette region);
        // left as-is per Brief S2b-1 — not this brief's material.
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
