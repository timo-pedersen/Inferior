using Inferior.Core.DataBus;

namespace Inferior.Gameplay.Components;

/// <summary>
/// External ship lighting (running lights, landing lights). Battery-backed.
/// Player-controllable on/off via PowerOn.
/// </summary>
public sealed class ExternalLightsComponent : ShipComponent
{
    public bool LightsOn => Status == ComponentStatus.Running;

    public ExternalLightsComponent(string name)
    {
        Name             = name;
        PowerConsumption = 0.0;
        StartupTimer     = 0.0;
    }

    protected override void OnInitializationComplete()
    {
        DataBus.SystemMessages.Publish(Topics.System.All, new($"{Name}: lights on"));
    }

    protected override void OnPowerOff()
    {
        DataBus.SystemMessages.Publish(Topics.System.All, new($"{Name}: lights off"));
    }

    protected override void OnTick(double dt) { }
}
