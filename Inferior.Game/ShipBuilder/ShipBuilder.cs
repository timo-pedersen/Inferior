using Inferior.Core;
using Inferior.Core.Math;
using Inferior.Gameplay.Components;
using Inferior.Gameplay.Components.Power;
using Inferior.Gameplay.Ship;
using Inferior.Persistence.Data;
using Microsoft.Xna.Framework;

namespace Inferior.Game.Ships;

public sealed class ShipBuilder
{
    private string   _id          = "";
    private string   _hullTypeId  = "";
    private string?  _name;
    private DateTime _createdDate = DateTime.UtcNow;

    private DVec3      _position    = DVec3.Zero;
    private Quaternion _orientation = Quaternion.Identity;

    private bool _installDefaultComponents;

    private ShipBuilder() { }

    public static ShipBuilder From(ShipRecord record)
    {
        if (string.IsNullOrEmpty(record.Id))
            throw new ArgumentException("ShipRecord.Id must not be empty", nameof(record));

        return new ShipBuilder
        {
            _id          = record.Id,
            _hullTypeId  = record.HullTypeId,
            _name        = record.Name,
            _createdDate = record.CreatedDate,
        };
    }

    public static ShipBuilder NewShip(string hullTypeId) => new()
    {
        _id          = Guid.NewGuid().ToString(),
        _hullTypeId  = hullTypeId,
        _createdDate = DateTime.UtcNow,
    };

    public ShipBuilder WithComponent(string slotId, string typeId, PowerPriority priority) => this;
    public ShipBuilder WithPanelLayout(CockpitLayoutRecord layout)                          => this;
    public ShipBuilder WithConsumables(int fuelRods, double coolant)                        => this;
    public ShipBuilder WithNewId(string shipId)        { _id = shipId;    return this; }
    public ShipBuilder WithResetHullIntegrity()                                             => this;
    public ShipBuilder WithDegradedComponents(double maxDamage)                             => this;
    public ShipBuilder WithDefaultConsumables()                                             => this;
    public ShipBuilder WithEmptyLog()                                                       => this;

    public ShipBuilder WithPosition(DVec3 position)             { _position    = position;    return this; }
    public ShipBuilder WithOrientation(Quaternion orientation)  { _orientation = orientation; return this; }

    public ShipBuilder WithDefaultStartingComponents() { _installDefaultComponents = true; return this; }

    public Ship Build()
    {
        var ship = new Ship
        {
            Id          = _id,
            HullTypeId  = _hullTypeId,
            Name        = _name,
            CreatedDate = _createdDate,
            SizeClass   = ShipSizeClass.Medium,   // same default SpawnShip used
            MoveSpeedMs = 5e9,                    // same default SpawnShip used
            Position    = _position,
        };
        ship.SetOrientation(_orientation);

        if (_installDefaultComponents)
        {
            var reactor = new PowerReactor("Reactor", maxPower: 120e6, outputCapacitorJ: 50e6);

            var bus = new PowerBus("MainBus", capacityJ: 10e6, maxPower: 120e6);
            bus.ConnectSource(reactor.OutputCapacitor);

            var powerManager = new PowerPriorityManager();
            powerManager.AttachToBus(bus);

            var shield = new ShieldComponent("Shield", maxShieldJ: 5e6, chargeRateW: 500e3);

            var shieldConnector = new ConnectorComponent("ShieldConnector", "MainBus", "Shield", maxPower: 600e3);
            shieldConnector.Connect(powerManager, shield.DemandWatts, shield.ReceivePower);

            var heatsink = new HyperspaceHeatSink("HeatSink",
                capacityJ:       50_000_000,
                transferRate:    800_000,
                heatDissipation: 500_000);

            var coolant = new CoolantSystem("Coolant",
                heatFlowPerComponent: 150_000,
                coolantLeakage:       0.0002);
            coolant.AttachHeatSink(heatsink);
            coolant.RegisterThermalNode(reactor.ThermalNode!);
            coolant.RegisterThermalNode(shield.ThermalNode!);

            ship.Install(reactor);
            ship.Install(bus);
            ship.Install(powerManager);
            ship.Install(shield);
            ship.Install(shieldConnector);
            ship.Install(heatsink);
            ship.Install(coolant);
            shield.PowerOn = false;  // starts off — player enables via SYS panel; Install() defaults PowerOn
                                     // true for everything, this deliberately overrides it after the fact
        }

        return ship;
    }
}
