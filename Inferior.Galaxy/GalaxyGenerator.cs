using Inferior.Core.Math;
using Inferior.Core.Random;

namespace Inferior.Galaxy;

/// <summary>
/// Generates the fixed galaxy of 2048 stars.
/// Called once at startup or first run; result can be serialised to disk.
///
/// Galaxy layout:
///   - Four spiral arms offset by 90°
///   - Stars scattered around arm centrelines with gaussian spread
///   - Z (vertical) offset: thick bulge at the core, thinning to a flat disk at the outer arms
///   - Denser core with tighter Z distribution; halo field stars are puffier than the disk
///   - Arms have slightly different character (density, star types)
///   - Sparse rift regions between arms make crossing difficult
///
/// Coordinate system: light-years, Y = galactic up.
/// Core is at origin. Arms extend outward.
/// </summary>
public static class GalaxyGenerator
{
    public const int   StarCount  = 20480;
    public const int   ArmCount   = 4;
    public const int   MasterSeed = 19721978; // homage: Elite released 1984, original BBC Micro 1981

    // Galaxy geometry (light-years)
    public  const double GalaxyRadiusLY   = 20_000.0;  // ly — exposed for skybox distance falloff
    private const double GalaxyRadius     = GalaxyRadiusLY;
    private const double CoreRadius       = 4_000.0;   // dense core region
    private const double ArmWidth         = 0.21;      // gaussian σ in radians
    private const double CoreThickness     = 300.0;     // σ of Z for nuclear bulge and inner arms (ly)
    private const double DiskThickness    = 150.0;     // σ of Z for outer disk (ly) — thin flat disk
    private const double HaloThickness    = 600.0;     // σ of Z for halo/field stars (ly)

    // Spiral arm parameters
    private const double ArmTightness = 0.45;          // how tightly wound (b in r = e^(bθ))
    private const double ArmLength    = 4.0 * DMath.Pi;// how many radians the arm sweeps

    // Max theta before spiral exceeds galaxy radius — computed from arm equation
    // r = CoreRadius * exp(ArmTightness * theta) = GalaxyRadius
    // => maxTheta = log(GalaxyRadius / CoreRadius) / ArmTightness
    private static readonly double MaxArmTheta =
        System.Math.Log(GalaxyRadius / CoreRadius) / ArmTightness;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Generate the full galaxy. Returns array of Star stubs (no systems yet).</summary>
    public static Star[] Generate(int seed = MasterSeed)
    {
        var rng   = new SeededRandom(seed);
        var stars  = new Star[StarCount];
        var names  = GenerateStarNames(rng);

        int starIndex = 0;

        // Core stars — ~10% of total, densely packed
        int coreCount = (int)(StarCount * 0.10);
        for (int i = 0; i < coreCount && starIndex < StarCount; i++, starIndex++)
        {
            var pos  = GenerateCorePosition(rng);
            var srng = rng.Derive(starIndex);
            stars[starIndex] = Star.Generate(starIndex, names[starIndex], pos, srng);
        }

        // Arm stars — ~85% of total, distributed across four arms
        int armStars = (int)(StarCount * 0.85);
        int perArm   = armStars / ArmCount;

        for (int arm = 0; arm < ArmCount && starIndex < StarCount; arm++)
        {
            double armOffset = (DMath.TwoPi / ArmCount) * arm;

            // Each arm has slightly different character
            double densityMod = 1.0 + rng.NextGaussian(0, 0.15);

            for (int i = 0; i < perArm && starIndex < StarCount; i++, starIndex++)
            {
                var pos  = GenerateArmPosition(arm, armOffset, rng);
                var srng = rng.Derive(starIndex);
                stars[starIndex] = Star.Generate(starIndex, names[starIndex], pos, srng);
            }
        }

        // Remaining — scattered halo and interarm field stars
        while (starIndex < StarCount)
        {
            var pos  = GenerateHaloPosition(rng);
            var srng = rng.Derive(starIndex);
            stars[starIndex] = Star.Generate(starIndex, names[starIndex], pos, srng);
            starIndex++;
        }

        return stars;
    }

    /// <summary>
    /// Derive a deterministic seed for a specific star's system.
    /// Use this to create SeededRandom for system generation.
    /// </summary>
    public static SeededRandom SystemSeed(Star star, int masterSeed = MasterSeed)
    {
        // Deterministic stable mix — never use HashCode.Combine (process-randomized in .NET).
        long xBits = System.BitConverter.DoubleToInt64Bits(star.GalacticPos.X);
        long zBits = System.BitConverter.DoubleToInt64Bits(star.GalacticPos.Z);

        int seed = Mix(masterSeed, star.GalaxyIndex);
        seed = Mix(seed, (int)(xBits         & 0xFFFFFFFF));
        seed = Mix(seed, (int)((xBits >> 32) & 0xFFFFFFFF));
        seed = Mix(seed, (int)(zBits         & 0xFFFFFFFF));
        seed = Mix(seed, (int)((zBits >> 32) & 0xFFFFFFFF));

        return new SeededRandom(seed);
    }

    private static int Mix(int a, int b) =>
        unchecked(a ^ (b + (int)0x9e3779b9 + (a << 6) + (a >> 2)));

    // ── Position generation ───────────────────────────────────────────────────

    private static DVec3 GenerateCorePosition(SeededRandom rng)
    {
        // Uniformly distributed within core radius (rejection sample on disk)
        DVec3 pos;
        do
        {
            double x = rng.NextDouble(-CoreRadius, CoreRadius);
            double z = rng.NextDouble(-CoreRadius, CoreRadius);
            pos = new DVec3(x, 0, z);
        }
        while (pos.PlanarDistance(DVec3.Zero) > CoreRadius);

        double y = rng.NextGaussian(0, CoreThickness);
        return new DVec3(pos.X, y, pos.Z);
    }

    private static DVec3 GenerateArmPosition(int armIndex, double armOffset, SeededRandom rng)
    {
        // Sample along the arm — theta capped so r never exceeds GalaxyRadius.
        // Previously we clamped r which caused stars to pile up at the rim.
        double effectiveLength = System.Math.Min(ArmLength, MaxArmTheta);
        double t     = System.Math.Pow(rng.NextDouble(), 0.7); // bias toward inner arm
        double theta = t * effectiveLength;

        // Logarithmic spiral: r = CoreRadius * e^(ArmTightness * theta)
        // r grows naturally without clamping — no rim ring
        double r = CoreRadius * System.Math.Exp(ArmTightness * theta);

        // Scatter around arm centreline
        double scatter = rng.NextGaussian(0, ArmWidth);
        double angle   = theta + armOffset + scatter;

        double x = r * System.Math.Cos(angle);
        double z = r * System.Math.Sin(angle);

        // Disk thins toward the outer arms: thick inner region (bulge) → flat outer disk.
        // t=0 is near the core, t=1 is the outer tip of the arm.
        double zThickness = DMath.Lerp(CoreThickness, DiskThickness, t);
        double y = rng.NextGaussian(0, zThickness);

        return new DVec3(x, y, z);
    }

    private static DVec3 GenerateHaloPosition(SeededRandom rng)
    {
        // Sparse stars outside the main arms
        double r     = rng.NextDouble(CoreRadius * 0.5, GalaxyRadius * 1.2);
        double angle = rng.NextAngle();
        double x     = r * System.Math.Cos(angle);
        double z     = r * System.Math.Sin(angle);
        double y     = rng.NextGaussian(0, HaloThickness); // halo is puffier than the disk

        return new DVec3(x, y, z);
    }

    // ── Name generation ───────────────────────────────────────────────────────

    private static string[] GenerateStarNames(SeededRandom rng)
    {
        // Simple syllable-based procedural names
        // Replace with a proper name list later if desired
        string[] prefixes =
        [
            "Al", "Ac", "Ar", "Be", "Ca", "Ce", "De", "El", "En",
            "Fo", "Ga", "He", "In", "Jo", "Ka", "Le", "Ma", "Na",
            "Or", "Pe", "Ri", "Sa", "Si", "Ta", "Te", "Ul", "Ve",
            "Xa", "Ze", "Zy"
        ];

        string[] middles =
        [
            "an", "ar", "el", "en", "in", "is", "on", "or", "us",
            "ra", "ri", "ro", "la", "li", "lo", "ma", "mi", "na",
            "ta", "ti", "va", "vi", "xa", "za", "bi", "ci", "da"
        ];

        string[] suffixes =
        [
            "a", "ae", "i", "is", "ix", "on", "or", "us", "um",
            "an", "ar", "as", "ux", "ax", "ex", "ox", "ia", "ea"
        ];

        var names = new HashSet<string>();
        var result = new string[StarCount];

        for (int i = 0; i < StarCount; i++)
        {
            string name;
            int attempts = 0;
            do
            {
                name = rng.Pick(prefixes) + rng.Pick(middles) + rng.Pick(suffixes);
                attempts++;
            }
            while (!names.Add(name) && attempts < 20);

            // If collision after 20 tries, append index
            if (attempts >= 20) name += $"-{i}";
            result[i] = name;
        }

        return result;
    }
}
