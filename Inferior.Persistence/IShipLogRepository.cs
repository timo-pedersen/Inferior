using Inferior.Persistence.Data;

namespace Inferior.Persistence;

public interface IShipLogRepository
{
    Task AppendAsync(string shipId, LogEntryRecord entry);
    Task<IReadOnlyList<LogEntryRecord>> GetRecentAsync(string shipId, int count = 50);
    Task<IReadOnlyList<LogEntryRecord>> GetAllAsync(string shipId);
    Task DeleteAllAsync(string shipId);
    Task ValidateLog(string shipId);
}
