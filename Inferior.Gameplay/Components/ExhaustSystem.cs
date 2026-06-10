using Inferior.Core.DataBus;

namespace Inferior.Gameplay.Components;

/// <summary>
/// Passive exhaust system. Expels degenerate matter produced when ionised metal
/// rods pass through hyperspace in the drive. Buildup scales with engine thrust;
/// reduces engine efficiency when blocked. Cleaned at stations.
///
/// Sensors published:
///   {Name}.BuildUp — degenerate material level (0..1; 0 = clean, 1 = fully blocked)
/// </summary>
public sealed class ExhaustSystem : ShipComponent
{
    /// <summary>0 = clean, 1 = fully blocked. Efficiency penalty kicks in above ~0.5.</summary>
    public double BuildUp { get; private set; }

    /// <summary>Rate at which buildup accumulates per unit of normalised thrust (1/second).</summary>
    public double BuildUpRate { get; init; } = 0.0001;  // stub — takes ~3 hours at full thrust

    private EngineComponent? _engine;

    public ExhaustSystem(string name)
    {
        Name             = name;
        PowerConsumption = 0.0;
        StartupTimer     = 0.0;

        RegisterSensors();
    }

    public void AttachEngine(EngineComponent engine) => _engine = engine;

    /// <summary>Clean the exhaust system. Returns the amount of buildup removed (0..1).</summary>
    public double Clean()
    {
        double removed = BuildUp;
        BuildUp = 0.0;
        DataBus.System.Publish(Topics.System.All, $"{Name}: exhaust cleaned");
        return removed;
    }

    protected override void OnTick(double dt)
    {
        if (_engine?.Status == ComponentStatus.Running)
        {
            double thrustFraction = Math.Abs(_engine.Throttle);
            BuildUp = Math.Min(1.0, BuildUp + BuildUpRate * thrustFraction * dt);
        }

        TickSensors();
    }

    private void RegisterSensors()
    {
        _sensors.Add(new ComponentSensor(
            $"{Name}.{Topics.Exhaust.BuildUp}",
            () => BuildUp,
            safeRange:  new RangeValue(0, 0.5),
            totalRange: new RangeValue(0, 1.0)));
    }
}
