using Inferior.Gameplay.Ship;
using Inferior.Persistence.Data;

namespace Inferior.Game.Ships;

public sealed class ShipBuilder
{
    private string   _id          = "";
    private string   _hullTypeId  = "";
    private string?  _name;
    private DateTime _createdDate = DateTime.UtcNow;

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

    public ShipBuilder WithComponent(string slotId, string typeId, PowerPriority priority) => this;
    public ShipBuilder WithPanelLayout(CockpitLayoutRecord layout)                          => this;
    public ShipBuilder WithConsumables(int fuelRods, double coolant)                        => this;
    public ShipBuilder WithNewId(string shipId)        { _id = shipId;    return this; }
    public ShipBuilder WithResetHullIntegrity()                                             => this;
    public ShipBuilder WithDegradedComponents(double maxDamage)                             => this;
    public ShipBuilder WithDefaultConsumables()                                             => this;
    public ShipBuilder WithEmptyLog()                                                       => this;

    public Ship Build() => new()
    {
        Id          = _id,
        HullTypeId  = _hullTypeId,
        Name        = _name,
        CreatedDate = _createdDate,
    };
}
