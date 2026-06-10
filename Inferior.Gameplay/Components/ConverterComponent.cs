using Inferior.Gameplay.Components.Power;

namespace Inferior.Gameplay.Components;

/// <summary>
/// Power converter — transforms power from one hyperspace band to another before
/// delivering it to a consuming component (e.g. Standard → ShieldAlpha for shields,
/// Standard → ArtificialGravity for grav field generators).
///
/// Like <see cref="ConnectorComponent"/>, this is a configuration descriptor that
/// the power distribution system uses when routing. Battery-backed, no heat.
/// </summary>
public sealed class ConverterComponent : ShipComponent
{
    /// <summary>ID of the source bus.</summary>
    public string FromBusId { get; init; } = "";

    /// <summary>ID of the component this converter feeds.</summary>
    public string ToComponentId { get; init; } = "";

    /// <summary>Maximum throughput (watts).</summary>
    public double MaxPower { get; init; }

    /// <summary>Output power type produced by this converter.</summary>
    public PowerOutType PowerOutType { get; init; }

    /// <summary>Conversion efficiency — fraction of input watts delivered as output.</summary>
    public new double Efficiency { get; init; } = 0.9;

    public ConverterComponent(string name, string fromBusId, string toComponentId,
                              double maxPower, PowerOutType powerOutType,
                              double efficiency = 0.9)
    {
        Name            = name;
        FromBusId       = fromBusId;
        ToComponentId   = toComponentId;
        MaxPower        = maxPower;
        PowerOutType    = powerOutType;
        Efficiency      = efficiency;
        PowerConsumption = 0.0;
        StartupTimer    = 0.0;
    }
}
