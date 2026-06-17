using Inferior.Core.DataBus;

namespace Inferior.Gameplay.Components;

/// <summary>
/// Coolant transport system. Moves heat from component ThermalNodes to the
/// HyperspaceHeatSink. No thermal mass of its own — purely a transport medium.
///
/// Transport rate per component is capped by HeatFlowPerComponent.
/// Efficiency scales with Level (0–1 fill fraction). Coolant leaks slowly
/// at CoolantLeakage fraction/second; replenish at stations.
///
/// Call RegisterThermalNode() and AttachHeatSink() before the simulation starts.
///
/// Sensors published:
///   {Name}.Level    — coolant fill fraction (0..1)
///   {Name}.FlowRate — current heat transport rate (watts)
/// </summary>
public sealed class CoolantSystem : ShipComponent
{
    public double HeatFlowPerComponent { get; init; }  // watts max per node
    public double CoolantLeakage       { get; init; }  // fraction/second lost through leakage

    public double Level { get; private set; } = 1.0;

    private HyperspaceHeatSink?    _sink;
    private readonly List<ThermalNode> _nodes = [];

    private double _lastFlowRate;

    public CoolantSystem(string name,
                         double heatFlowPerComponent,
                         double coolantLeakage = 0.0001)
    {
        Name                   = name;
        HeatFlowPerComponent   = heatFlowPerComponent;
        CoolantLeakage         = coolantLeakage;
        PowerConsumption        = 0.0;  // battery-backed
        StartupTimer           = 0.0;

        RegisterSensors();
    }

    public void AttachHeatSink(HyperspaceHeatSink sink) => _sink = sink;
    public void RegisterThermalNode(ThermalNode node)   => _nodes.Add(node);

    public void ReplenishCoolant(double amount = 1.0)
        => Level = Math.Clamp(Level + amount, 0.0, 1.0);

    protected override void OnTick(double dt)
    {
        // Leakage
        Level = Math.Max(0.0, Level - CoolantLeakage * dt);

        if (_sink == null)
        {
            TickSensors();
            return;
        }

        // Transport heat from each node toward the sink
        double totalWatts = 0.0;
        foreach (var node in _nodes)
        {
            if (node.CurrentHeat <= 0.0) continue;

            double rate          = HeatFlowPerComponent * Level;
            double transferredJ  = Math.Min(node.CurrentHeat, rate * dt);
            double transferredW  = transferredJ / dt;

            node.Update(-transferredW, dt);   // cool the node
            totalWatts += transferredW;
        }

        double cappedWatts = Math.Min(totalWatts, _sink.TransferRate);
        _sink.Tick(cappedWatts, dt);

        _lastFlowRate = cappedWatts;
        TickSensors();
    }

    protected override void OnInitializationComplete()
    {
        PublishSensorRanges();
        DataBus.System.Publish(Topics.System.All,
            new($"{Name}: online — {HeatFlowPerComponent / 1e3:F0} kW/node, {Level:P0} coolant"));
    }

    private void RegisterSensors()
    {
        _sensors.Add(new ComponentSensor(
            $"{Name}.{Topics.CoolantSystem.Level}",
            () => Level,
            safeRange:  new RangeValue(0.2, 1.0),
            totalRange: new RangeValue(0.0, 1.0)));

        _sensors.Add(new ComponentSensor(
            $"{Name}.{Topics.CoolantSystem.FlowRate}",
            () => _lastFlowRate,
            safeRange:  new RangeValue(0, HeatFlowPerComponent * _nodes.Count),
            totalRange: new RangeValue(0, HeatFlowPerComponent * Math.Max(1, _nodes.Count))));
    }
}
