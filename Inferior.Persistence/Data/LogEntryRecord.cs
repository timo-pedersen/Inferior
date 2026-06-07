namespace Inferior.Persistence.Data;

public record LogEntryRecord
{
    public DateTime Date { get; init; }
    public string   Type { get; init; } = "";
    public string   Text { get; init; } = "";
    public string   Hash { get; init; } = "";
}
