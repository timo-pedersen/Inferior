using Inferior.Core.DataBus;

namespace Inferior.Gameplay.Components;

/// <summary>
/// Power distribution bus — a passive energy buffer.
/// Gets charged from an upstream source each tick; consumers call Draw() on demand.
/// The bus itself knows nothing about consumers or priorities.
/// Priority enforcement is handled by an optional PowerPriorityManager.
///
/// Install order matters: reactor must tick before bus (fills source cap),
/// bus must tick before consumers (fills bus cap from source).
///
/// Sensors published every tick:
///   {Name}.Level  — fill fraction 0–1
/// </summary>
public sealed class PowerBus : ShipComponent
{
    public PowerCapacitor Capacitor { get; }

    private PowerCapacitor? _source;

    public PowerBus(string name, double capacityJ)
    {
        Name             = name;
        Capacitor        = new PowerCapacitor(capacityJ);
        OutputCapacitor  = Capacitor;
        RegisterSensors();
    }

    /// <summary>Connect the upstream source capacitor (e.g. reactor output cap).</summary>
    public void ConnectSource(PowerCapacitor source) => _source = source;

    /// <summary>
    /// Draw power from this bus. Returns actual watts delivered (may be less if depleted).
    /// Called by consumers each tick.
    /// </summary>
    public double Draw(double watts, double dt) => Capacitor.Draw(watts, dt);

    public override void Tick(double dt)
    {
        if (_source != null && dt > 0.0)
        {
            // Pull from source to fill up the bus capacitor
            double canAbsorbJ    = Capacitor.MaxJ - Capacitor.ChargeJ;
            if (canAbsorbJ > 0.0)
            {
                double requestedW  = canAbsorbJ / dt;
                double deliveredW  = _source.Draw(requestedW, dt);
                Capacitor.Charge(deliveredW, dt);
            }
        }

        TickSensors();
    }

    public override void OnStartup()
    {
        base.OnStartup();
        DataBus.System.Publish(Topics.System.All,
            $"{Name}: {Capacitor.MaxJ / 1e6:F1} MJ bus online");
    }

    private void RegisterSensors()
    {
        _sensors.Add(new ComponentSensor(
            $"{Name}.Level",
            () => Capacitor.Level,
            safeRange:  new RangeValue(0.2, 1.0),
            totalRange: new RangeValue(0.0, 1.0)));
    }
}
