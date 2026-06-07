using Inferior.Gameplay.Ship;
using Inferior.Persistence;

namespace Inferior.Game.Ships;

public sealed class ShipPersistenceService
{
    private readonly IShipRepository _repo;

    public ShipPersistenceService(IShipRepository repo)
    {
        _repo = repo;
    }

    public async Task SaveAsync(Ship ship)
    {
        var record = ship.ToRecord();      // born here
        await _repo.SaveAsync(record);     // dies here
    }

    public async Task<Ship?> LoadAsync(string shipId)
    {
        var record = await _repo.GetAsync(shipId);   // born here
        if (record == null) return null;

        record = ShipRecordMigrator.EnsureCurrent(record);
        return ShipBuilder.From(record).Build();      // dies here
    }
}
