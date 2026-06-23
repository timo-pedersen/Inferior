namespace Inferior.Gameplay.Components;

/// <summary>
/// Local thermal state for a single component. Instantiated inside a heated
/// component's constructor; null ThermalNode on ShipComponent means no heat management.
///
/// All heat removal is via the coolant system — there is no passive dissipation through
/// the hull or otherwise. The coolant system calculates cooling watts and passes them as
/// a negative contribution to Update(). Zero coolant = zero cooling = heat rises unchecked.
/// </summary>
public sealed class ThermalNode
{
    public ThermalNode(double heatCapacity, double maxHeatJ)
    {
        HeatCapacity = heatCapacity;
        MaxHeatJ     = maxHeatJ;
    }

    /// <summary>J/K — local thermal mass. Temperature rise = heat (J) ÷ capacity (J/K).</summary>
    public double HeatCapacity { get; }

    /// <summary>Joules — heat energy at which the component enters failure state.</summary>
    public double MaxHeatJ { get; }

    /// <summary>Current thermal energy stored, in joules.</summary>
    public double CurrentHeat { get; private set; }

    /// <summary>
    /// Heat generation rate (watts) from the last Update call — positive contributions only.
    /// Cooling withdrawals are excluded. Used for ThermalSignature summing.
    /// </summary>
    public double LastHeatInputW { get; private set; }

    /// <summary>Physical temperature in Kelvin — published to DataBus.Instruments for display.</summary>
    public double Temperature => CurrentHeat / HeatCapacity;

    /// <summary>Normalised 0–1 — used for damage thresholds and gauge colour ranges.</summary>
    public double NormalizedTemperature => CurrentHeat / MaxHeatJ;

    /// <summary>Joules above the failure threshold. Zero when not in failure state.</summary>
    public double ExcessHeatJ => Math.Max(0.0, CurrentHeat - MaxHeatJ);

    // Threshold flags — based on NormalizedTemperature
    public bool IsWarning  => NormalizedTemperature > 0.7;
    public bool IsCritical => NormalizedTemperature > 0.9;
    /// <summary>Component takes damage or shuts down when true.</summary>
    public bool IsFailure  => NormalizedTemperature >= 1.0;

    /// <summary>
    /// Advance thermal state by dt seconds.
    /// netHeatWatts: positive = heating up, negative = cooling down.
    /// The cooling contribution is calculated externally by the coolant system and passed here.
    /// </summary>
    public void Update(double netHeatWatts, double dt)
    {
        if (netHeatWatts > 0.0)
            LastHeatInputW = netHeatWatts;
        CurrentHeat = Math.Max(0.0, CurrentHeat + netHeatWatts * dt);
    }

    /// <summary>Inject heat energy directly in joules (e.g. atmospheric drag on shields).</summary>
    public void AddHeatJ(double joules)
        => CurrentHeat = Math.Max(0.0, CurrentHeat + joules);
}
