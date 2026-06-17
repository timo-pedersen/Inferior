using Inferior.Core.DataBus;

namespace Inferior.Gameplay.Components;

/// <summary>
/// Internal ship lighting. Always active in some form — battery-backed, no off switch
/// in normal operation. Emergency mode reduces lighting to minimum power draw.
/// </summary>
public sealed class InternalLightsComponent : ShipComponent
{
    public bool EmergencyMode { get; private set; }

    public InternalLightsComponent(string name)
    {
        Name             = name;
        PowerConsumption = 0.0;
        StartupTimer     = 0.0;
    }

    public void SetEmergencyMode(bool active)
    {
        EmergencyMode = active;
        DataBus.System.Publish(Topics.System.All,
            new(active ? $"{Name}: emergency lighting" : $"{Name}: normal lighting"));
    }

    protected override void OnTick(double dt) { }
}
