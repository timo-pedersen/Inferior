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
    // Age: now read by TexturePalette.From (Brief S2b-2, AgeAdjustedGrime) — was
    // generated and unused before this brief (Report S2a §4/§5). Wealth/Population
    // remain unread anywhere; still not this brief's material (no wiring specified).
    public int            Age        { get; init; }   // years, 5–200
    public float          Wealth     { get; init; }   // 0.0–1.0
    public float          Population { get; init; }   // 0.0–1.0

    public static StationProfile Generate(int seed, StationScale scale)
    {
        var rng = new SeededRandom(seed ^ 0x5A3C_F17B);

        int economyCount = Enum.GetValues<StationEconomy>().Length;
        // Correction (Brief S2b-2): an earlier note here claimed this excludes the last
        // enum value (Independent) as unreachable. That was wrong — SeededRandom.NextInt
        // (int min, int max) is [min, max] INCLUSIVE on both ends (_rng.Next(min, max+1)),
        // so NextInt(0, economyCount - 1) already reaches every index 0..economyCount-1,
        // Independent included. No off-by-one bug; nothing to fix here.
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
