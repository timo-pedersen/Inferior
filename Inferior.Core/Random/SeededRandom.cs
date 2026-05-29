namespace Inferior.Core.Random;

/// <summary>
/// Deterministic seeded random number generator.
/// Wraps System.Random with additional distributions needed for
/// procedural generation: gaussian, on-sphere, range variants.
///
/// Every procedural system (galaxy, star system, ship, pilot) should
/// create its own SeededRandom from a derived seed so results are
/// independent and reproducible regardless of generation order.
/// </summary>
public sealed class SeededRandom
{
    private readonly System.Random _rng;

    // Box-Muller state — gaussian generates two values per call,
    // we cache the second to avoid waste.
    private bool   _hasSpare;
    private double _spare;

    public int Seed { get; }

    public SeededRandom(int seed)
    {
        Seed = seed;
        _rng = new System.Random(seed);
    }

    // ── Derive a child seed ───────────────────────────────────────────────────

    /// <summary>
    /// Derive a new seed from this seed plus an index.
    /// Use this to create child generators that are independent but
    /// deterministically linked to the parent.
    ///
    /// Example:
    ///   var systemRng = galaxyRng.Derive(starIndex);
    ///   var planetRng = systemRng.Derive(planetIndex);
    /// </summary>
    public SeededRandom Derive(int index)
        => new(HashCode.Combine(Seed, index));

    /// <summary>Derive from two indices (e.g. grid coordinates).</summary>
    public SeededRandom Derive(int indexA, int indexB)
        => new(HashCode.Combine(Seed, indexA, indexB));

    /// <summary>Derive from a string key (e.g. system name).</summary>
    public SeededRandom Derive(string key)
        => new(HashCode.Combine(Seed, key.GetHashCode()));

    // ── Uniform distributions ─────────────────────────────────────────────────

    /// <summary>[0.0, 1.0)</summary>
    public double NextDouble() => _rng.NextDouble();

    /// <summary>[0.0f, 1.0f)</summary>
    public float NextFloat() => (float)_rng.NextDouble();

    /// <summary>[min, max)</summary>
    public double NextDouble(double min, double max)
        => min + _rng.NextDouble() * (max - min);

    /// <summary>[min, max)</summary>
    public float NextFloat(float min, float max)
        => min + (float)_rng.NextDouble() * (max - min);

    /// <summary>[min, max] inclusive</summary>
    public int NextInt(int min, int max)
        => _rng.Next(min, max + 1);

    /// <summary>[0, max] inclusive</summary>
    public int NextInt(int max)
        => _rng.Next(0, max + 1);

    /// <summary>True with the given probability [0..1].</summary>
    public bool NextBool(double probability = 0.5)
        => _rng.NextDouble() < probability;

    // ── Gaussian distribution ─────────────────────────────────────────────────

    /// <summary>
    /// Box-Muller transform — returns a normally distributed value.
    /// mean = 0, stdDev = 1 by default.
    /// Used for Z-offset of stars from galactic plane, scatter around
    /// spiral arms, and many other natural distributions.
    /// </summary>
    public double NextGaussian(double mean = 0.0, double stdDev = 1.0)
    {
        if (_hasSpare)
        {
            _hasSpare = false;
            return mean + stdDev * _spare;
        }

        double u, v, s;
        do
        {
            u = _rng.NextDouble() * 2.0 - 1.0;
            v = _rng.NextDouble() * 2.0 - 1.0;
            s = u * u + v * v;
        }
        while (s >= 1.0 || s == 0.0);

        double mul = System.Math.Sqrt(-2.0 * System.Math.Log(s) / s);
        _spare     = v * mul;
        _hasSpare  = true;

        return mean + stdDev * (u * mul);
    }

    /// <summary>
    /// Gaussian clamped to [min, max].
    /// Resamples until within range — use tight stdDevs to avoid loops.
    /// </summary>
    public double NextGaussianClamped(double mean, double stdDev, double min, double max)
    {
        double value;
        do { value = NextGaussian(mean, stdDev); }
        while (value < min || value > max);
        return value;
    }

    // ── Directional / spherical ───────────────────────────────────────────────

    /// <summary>
    /// Uniform random point on the surface of a unit sphere.
    /// Used for skybox star placement.
    /// </summary>
    public Math.DVec3 NextOnUnitSphere()
    {
        // Correct uniform distribution — NOT normalised NextDouble³
        double theta = _rng.NextDouble() * 2.0 * System.Math.PI;
        double phi   = System.Math.Acos(2.0 * _rng.NextDouble() - 1.0);

        return new Math.DVec3(
            System.Math.Sin(phi) * System.Math.Cos(theta),
            System.Math.Sin(phi) * System.Math.Sin(theta),
            System.Math.Cos(phi));
    }

    /// <summary>
    /// Random point within a sphere of given radius.
    /// Used for asteroid belt scatter, nebula particle placement.
    /// </summary>
    public Math.DVec3 NextInSphere(double radius)
    {
        // Rejection sample — ~52% acceptance, fine for generation
        Math.DVec3 point;
        do
        {
            point = new Math.DVec3(
                NextDouble(-1, 1),
                NextDouble(-1, 1),
                NextDouble(-1, 1));
        }
        while (point.LengthSquared > 1.0);

        return point * radius;
    }

    /// <summary>
    /// Random angle in radians [0, 2π).
    /// </summary>
    public double NextAngle()
        => _rng.NextDouble() * 2.0 * System.Math.PI;

    // ── Selection ─────────────────────────────────────────────────────────────

    /// <summary>Pick a random element from an array.</summary>
    public T Pick<T>(T[] items)
        => items[_rng.Next(items.Length)];

    /// <summary>Pick a random element from a list.</summary>
    public T Pick<T>(System.Collections.Generic.IList<T> items)
        => items[_rng.Next(items.Count)];

    /// <summary>
    /// Weighted pick — weights do not need to sum to 1.
    /// Example: Pick(new[]{"Common","Rare"}, new[]{0.9, 0.1})
    /// </summary>
    public T WeightedPick<T>(T[] items, double[] weights)
    {
        double total = 0;
        foreach (var w in weights) total += w;

        double roll = _rng.NextDouble() * total;
        double cumulative = 0;

        for (int i = 0; i < items.Length; i++)
        {
            cumulative += weights[i];
            if (roll < cumulative) return items[i];
        }

        return items[^1]; // fallback, floating point edge case
    }
}
