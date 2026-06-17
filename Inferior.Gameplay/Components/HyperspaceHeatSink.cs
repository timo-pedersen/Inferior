using Inferior.Core.DataBus;

namespace Inferior.Gameplay.Components;

/// <summary>
/// The ship's central thermal mass and heat disposal device. Coolant transports heat
/// from component ThermalNodes into this sink; the sink dissipates it to hyperspace.
/// The coolant fluid itself has no thermal mass — it is a pure transport medium.
///
/// Dissipation is proportional to fill level (Newton's Law of Cooling):
///   dissipation = HeatDissipation × (StoredHeatJ / CapacityJ)
///
/// The sink finds natural equilibrium at: fill fraction = heat inflow / HeatDissipation.
/// If sustained heat inflow exceeds HeatDissipation, saturation is inevitable — it dumps
/// heat back into realspace, causing a massive thermal spike and EM burst detectable by
/// every passive sensor in range.
///
/// Power draw is not simulated — modelling the consequences of heat sink power loss
/// adds complexity without meaningful gameplay.
/// </summary>
public sealed class HyperspaceHeatSink : ShipComponent
{
    /// <summary>Maximum heat energy before saturation (joules).</summary>
    public double CapacityJ { get; }

    /// <summary>Current stored heat energy (joules).</summary>
    public double StoredHeatJ { get; private set; }

    /// <summary>Maximum incoming rate from coolant transport (watts). Caller must clamp to this before passing in.</summary>
    public double TransferRate { get; }

    /// <summary>Maximum dissipation rate, reached at full capacity (watts). Actual rate scales with fill level.</summary>
    public double HeatDissipation { get; }

    /// <summary>Actual dissipation rate — scales proportionally with fill level.</summary>
    public double CurrentDissipationRate => HeatDissipation * (StoredHeatJ / CapacityJ);

    public bool IsSaturated => StoredHeatJ >= CapacityJ;

    private bool _wasSaturated;

    /// <summary>
    /// Fired once when the sink transitions into saturation (edge-triggered, not level-triggered).
    /// Saturation dumps heat into realspace — massive thermal spike and EM burst.
    /// </summary>
    public event Action? OnSaturation;

    public HyperspaceHeatSink(string name, double capacityJ, double transferRate, double heatDissipation)
    {
        Name            = name;
        CapacityJ       = capacityJ;
        TransferRate    = transferRate;
        HeatDissipation = heatDissipation;

        // Saturation dumps heat to realspace — edge-triggered critical alert
        OnSaturation += () =>
        {
            StoredHeatJ = 0.0;
            DataBus.System.Publish(Topics.System.All,
                new($"{Name}: SATURATION — thermal dump to realspace",
                    SystemMessagePriority.Critical));
        };

        RegisterSensors();
    }

    /// <summary>
    /// Advance the heat sink by one tick with an explicit incoming heat rate.
    /// incomingHeatWatts must already be clamped to TransferRate by the caller (coolant system).
    /// Call this from the coolant system tick, not from the standard component tick loop.
    /// </summary>
    public void Tick(double incomingHeatWatts, double dt)
    {
        double net = incomingHeatWatts - CurrentDissipationRate;  // watts
        StoredHeatJ = Math.Clamp(StoredHeatJ + net * dt, 0.0, CapacityJ);

        // Edge-triggered: fires once on transition to saturation, not every tick while saturated.
        // The OnSaturation handler resets StoredHeatJ to 0 — check _wasSaturated after possible reset.
        if (IsSaturated && !_wasSaturated)
            OnSaturation?.Invoke();
        _wasSaturated = IsSaturated;
    }

    /// <summary>
    /// Standard sim-loop tick. Publishes sensor values.
    /// Heat processing should be driven by the coolant system via Tick(incomingHeatWatts, dt).
    /// </summary>
    protected override void OnTick(double dt)
    {
        TickSensors();
    }

    protected override void OnInitializationComplete()
    {
        PublishSensorRanges();
        DataBus.System.Publish(Topics.System.All,
            new($"{Name}: online — {CapacityJ / 1e6:F0} MJ capacity, {HeatDissipation / 1e3:F0} kW dissipation"));
    }

    private void RegisterSensors()
    {
        _sensors.Add(new ComponentSensor(
            $"{Name}.{Topics.HeatSink.Fill}",
            () => StoredHeatJ / CapacityJ,
            safeRange:  new RangeValue(0.0, 0.7),
            totalRange: new RangeValue(0.0, 1.0)));

        _sensors.Add(new ComponentSensor(
            $"{Name}.{Topics.HeatSink.Dissipation}",
            () => CurrentDissipationRate,
            safeRange:  new RangeValue(0.0, HeatDissipation),
            totalRange: new RangeValue(0.0, HeatDissipation)));
    }
}
