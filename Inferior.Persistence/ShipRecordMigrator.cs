using Inferior.Persistence.Data;

namespace Inferior.Persistence;

public static class ShipRecordMigrator
{
    public static ShipRecord EnsureCurrent(ShipRecord record)
        => record.SchemaVersion switch
        {
            ShipRecord.CurrentVersion => record,
            _ => throw new NotSupportedException(
                     $"Unknown schema version {record.SchemaVersion}")
        };
}
