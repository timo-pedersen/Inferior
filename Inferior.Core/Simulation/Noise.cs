namespace Inferior.Core.Simulation;

/// <summary>
/// Pure static noise functions for sensor noise generation.
/// All functions are stateless — they read GameClock.SimTime as their time input.
///
/// The Seed parameter decorrelates multiple sensors using the same noise type
/// so they don't drift in lockstep. Derive a unique seed per sensor instance,
/// e.g. from a hash of its topic name.
///
/// Output ranges:
///   White, Pink, Periodic  →  roughly −1..1
///   Spike                  →  0..1  (near zero most of the time)
/// </summary>
public static class Noise
{
    // ── 1D value noise — the foundation ──────────────────────────────────────
    //
    // Smooth, aperiodic, deterministic. Given a continuous input t, returns a
    // smooth value in −1..1. Two variants:
    //   Simplex1()     — quintic smoothstep, higher quality
    //   Simplex1Fast() — cubic smoothstep, cheaper, fine for low-priority noise
    //
    // Note: this is value noise with smooth interpolation, not true lattice simplex.
    // For 1D time-varying sensor noise the distinction is irrelevant in practice.

    public static double Simplex1(double t)
    {
        int    i = (int)System.Math.Floor(t);
        double f = t - i;
        double u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0); // quintic smoothstep
        return Lerp(Grad(Hash(i), f), Grad(Hash(i + 1), f - 1.0), u);
    }

    public static double Simplex1Fast(double t)
    {
        int    i = (int)System.Math.Floor(t);
        double f = t - i;
        double u = f * f * (3.0 - 2.0 * f);                    // cubic smoothstep
        return Lerp(Grad(Hash(i), f), Grad(Hash(i + 1), f - 1.0), u);
    }

    // ── Noise types ───────────────────────────────────────────────────────────

    /// <summary>
    /// White noise — fast, uncorrelated jitter.
    /// frequency: how quickly the value changes (higher = more jitter).
    /// </summary>
    public static double White(double seed, double frequency = 500.0)
        => Simplex1(seed + GameClock.SimTime * frequency);

    /// <summary>
    /// Pink (1/f) noise — slow drift with texture. Natural-feeling wander.
    /// Sum of octaves at halving amplitudes.
    /// </summary>
    public static double Pink(double seed)
        => Simplex1(seed + GameClock.SimTime * 0.05) * 0.500   // very slow drift
         + Simplex1(seed + GameClock.SimTime * 0.20) * 0.250   // medium
         + Simplex1(seed + GameClock.SimTime * 0.80) * 0.125   // faster
         + Simplex1(seed + GameClock.SimTime * 3.00) * 0.063;  // texture

    /// <summary>
    /// Periodic — deterministic sine wave tied to a physical period.
    /// Use for neutron star precession, binary orbital period, etc.
    /// </summary>
    /// <param name="period">Period in seconds.</param>
    /// <param name="phase">Phase offset in radians.</param>
    public static double Periodic(double period, double phase = 0.0)
        => System.Math.Sin((GameClock.SimTime / period) * System.Math.Tau + phase);

    /// <summary>
    /// Spike — occasional sharp transient. Near 0 most of the time, rare burst.
    /// </summary>
    /// <param name="frequency">Approximate spikes per second (fractional OK, e.g. 0.02).</param>
    /// <param name="sharpness">Higher = narrower spike. Try 8–20.</param>
    public static double Spike(double seed, double frequency = 0.05, double sharpness = 12.0)
    {
        double t = (Simplex1(seed + GameClock.SimTime * frequency) + 1.0) * 0.5; // map to 0..1
        return t > 0.92 ? System.Math.Pow((t - 0.92) / 0.08, sharpness) : 0.0;
    }

    // ── Scaling helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Scale a noise value to a sensor's range.
    /// noiseFraction: fraction of sensorMax the noise can span (e.g. 0.05 = ±5%).
    /// </summary>
    public static double Scale(double noise, double sensorMax, double noiseFraction)
        => noise * sensorMax * noiseFraction;

    /// <summary>
    /// Linear distance falloff. Returns 1.0 at distance=0, 0.0 at distance >= maxRange.
    /// Use to scale external noise sources by proximity.
    /// </summary>
    public static double DistanceFalloff(double distance, double maxRange)
        => System.Math.Max(0.0, 1.0 - distance / maxRange);

    // ── Internals ─────────────────────────────────────────────────────────────

    private static double Lerp(double a, double b, double t) => a + t * (b - a);

    private static double Grad(int hash, double x) => (hash & 1) == 0 ? x : -x;

    private static int Hash(int i)
    {
        // Fast integer hash — decorrelates octaves and seeds
        i = ((i >> 16) ^ i) * 0x45d9f3b;
        i = ((i >> 16) ^ i) * 0x45d9f3b;
        return (i >> 16) ^ i;
    }
}
