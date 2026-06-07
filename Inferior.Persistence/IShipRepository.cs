using Inferior.Persistence.Data;

namespace Inferior.Persistence;

public interface IShipRepository
{
    Task<ShipRecord?> GetAsync(string shipId);
    Task SaveAsync(ShipRecord record);
    Task DeleteAsync(string shipId);
    Task<IReadOnlyList<ShipSummary>> ListAsync();
}
