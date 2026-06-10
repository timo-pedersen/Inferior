namespace Inferior.Gameplay.Components.Power;

/// <summary>
/// Reusable energy buffer. Always works in joules internally.
/// Used as the bus's internal buffer, component input capacitors, and reactor output staging.
/// </summary>
public sealed class PowerCapacitor
{
    public double MaxJ    { get; init; }   // maximum stored energy (joules)
    public double StoredJ { get; private set; }

    /// <summary>0–1 fill fraction. Published as "{bus}.Level" on the instruments bus.</summary>
    public double FillFraction => MaxJ > 0.0 ? StoredJ / MaxJ : 0.0;

    public PowerCapacitor() { }
    public PowerCapacitor(double maxJ) => MaxJ = maxJ;

    /// <summary>Joules drawn since the last ResetDrawn() call.</summary>
    public double DrawnJ { get; private set; }

    /// <summary>Snapshot drawn joules and reset the accumulator. Call once per tick from the owning component.</summary>
    public double SnapshotAndResetDrawn()
    {
        double snapshot = DrawnJ;
        DrawnJ = 0.0;
        return snapshot;
    }

    /// <summary>Withdraw up to requestedJ joules. Returns actual joules delivered (≤ requestedJ).</summary>
    public double Draw(double requestedJ)
    {
        double actual = Math.Min(requestedJ, StoredJ);
        StoredJ -= actual;
        DrawnJ  += actual;
        return actual;
    }

    /// <summary>Charge at up to maxWatts for dt seconds. Returns joules actually added.</summary>
    public double Charge(double maxWatts, double dt)
    {
        double spaceJ  = MaxJ - StoredJ;
        double addedJ  = Math.Min(maxWatts * dt, spaceJ);
        StoredJ += addedJ;
        return addedJ;
    }
}
