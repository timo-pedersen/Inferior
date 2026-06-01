namespace Inferior.Core.DataBus;

/// <summary>
/// Static hub for all inter-system messaging. Simulation thread publishes freely;
/// main thread calls Drain() once per frame to dispatch handlers.
/// </summary>
public static class DataBus
{
    // Device status, cold-start sequence, state changes
    public static readonly Bus<string>       System           = new();

    // Live numeric instrument values — published every sim tick
    // Topic: "ComponentName.ValueName"  e.g. "PowerCore.PowerLoad"
    // Multiple instances: "ComponentName_N.ValueName"  e.g. "PowerCore_2.PowerLoad"
    public static readonly Bus<double>       Instruments      = new();

    // Component state — damage percent, efficiency — published on change only
    public static readonly Bus<double>       InstrumentState  = new();

    // Operating envelopes — published at startup and when ranges change
    public static readonly Bus<RangeValue>   InstrumentRanges = new();

    // Radar contact updates — published when a contact appears or changes
    public static readonly Bus<RadarContact> Radar            = new();

    // Contact lost — published when a contact disappears; subscribers handle cleanup
    public static readonly Bus<string>       RadarLost        = new();

    // Called once per frame from Game.Update() on main thread
    public static void Drain()
    {
        System.Drain();
        Instruments.Drain();
        InstrumentState.Drain();
        InstrumentRanges.Drain();
        Radar.Drain();
        RadarLost.Drain();
    }
}
