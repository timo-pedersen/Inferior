using Inferior.Core.DataBus;
using Inferior.Game.Ships;
using Inferior.Gameplay.Hull;
using Inferior.Gameplay.Ship;
using Inferior.Gameplay.Sensors;
using Xunit;

namespace Inferior.Game.Test;

public sealed class ShipEngineeringTopologyTests
{
    [Fact]
    public void DefaultShipProjectsEveryHullSlotAndStableBusPort()
    {
        HullDefinition hull = HullDefinitionLibrary.Get("type-1");
        Ship ship = ShipBuilder.NewShip(hull.HullTypeId)
            .WithDefaultStartingComponents()
            .Build();

        ShipSystemsTopologySnapshot snapshot = ship.SystemsTopology.CreateSnapshot(ship.Id);

        foreach (HullSlot slot in hull.Slots)
            Assert.Contains(snapshot.Nodes,
                node => node.NodeId == ShipSystemTopology.HullSlotNodeId(slot.SlotId));

        Assert.Equal(8, snapshot.Nodes.Count(node =>
            node.Kind == ShipSystemNodeKind.PowerBusSlot));
        Assert.Contains(snapshot.Nodes, node =>
            node.NodeId == "fixed:ship-computer"
            && !node.IsReplaceable);
        Assert.Contains(snapshot.Nodes, node =>
            node.DeviceId == "ShieldConnector"
            && node.Kind == ShipSystemNodeKind.PowerBusSlot);
    }

    [Fact]
    public void DefaultShipProjectsPowerPathAndSolarHeatSensorWhileLeavingSensorCapacityVisible()
    {
        Ship ship = ShipBuilder.NewShip("type-1")
            .WithDefaultStartingComponents()
            .Build();
        ShipSystemsTopologySnapshot snapshot = ship.SystemsTopology.CreateSnapshot(ship.Id);

        Assert.Contains(snapshot.Connections, connection =>
            connection.ConnectionId == "power.reactor-mainbus"
            && connection.Kind == ShipSystemConnectionKind.PowerSource);
        Assert.Contains(snapshot.Connections, connection =>
            connection.ConnectionId == "power.mainbus-shield.bus"
            && connection.FlowTopic == "ShieldConnector.Flow");
        Assert.Contains(snapshot.Connections, connection =>
            connection.ConnectionId == "power.mainbus-shield.device"
            && connection.FlowTopic == "ShieldConnector.Flow");
        Assert.Contains(ship.Components, component => component is SolarHeatSensor);
        Assert.Contains(snapshot.Nodes, node =>
            node.DeviceId == "SolarHeatSensor"
            && node.Metrics.Any(metric => metric.Role == ShipSystemMetricRole.HeatIrradiance));
        Assert.Contains(snapshot.Nodes, node =>
            node.Category == SlotCategory.Sensor.ToString()
            && node.DeviceId is null
            && node.IsReplaceable);
    }
}
