namespace Inferior.Gameplay.Components.Power;

/// <summary>
/// Simple energy buffer. Charge fills it from a power source; Draw depletes it for a consumer.
/// Both operations are clamped so the capacitor never over- or under-flows.
/// </summary>
public sealed class PowerCapacitor
{
    public double MaxJ    { get; }
    public double ChargeJ { get; private set; }

    /// <summary>0.0 = empty, 1.0 = full.</summary>
    public double Level => MaxJ > 0.0 ? ChargeJ / MaxJ : 0.0;

    public PowerCapacitor(double maxJ) => MaxJ = maxJ;

    /// <summary>
    /// Fill from a source delivering <paramref name="watts"/> for <paramref name="dt"/> seconds.
    /// Returns the actual watts absorbed (may be less if nearly full).
    /// </summary>
    public double Charge(double watts, double dt)
    {
        double toAdd = Math.Min(watts * dt, MaxJ - ChargeJ);
        ChargeJ += toAdd;
        return dt > 0.0 ? toAdd / dt : 0.0;
    }

    /// <summary>
    /// Deliver up to <paramref name="watts"/> to a consumer for <paramref name="dt"/> seconds.
    /// Returns the actual watts delivered (may be less if nearly empty).
    /// </summary>
    public double Draw(double watts, double dt)
    {
        double toTake = Math.Min(watts * dt, ChargeJ);
        ChargeJ -= toTake;
        return dt > 0.0 ? toTake / dt : 0.0;
    }
}
