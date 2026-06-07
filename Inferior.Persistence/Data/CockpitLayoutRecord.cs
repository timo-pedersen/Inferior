using System.Text.Json;

namespace Inferior.Persistence.Data;

public record CockpitLayoutRecord
{
    public InstrumentRecord[] Instruments { get; init; } = [];
}

public record InstrumentRecord
{
    public string TypeId { get; init; } = "";
    public string Topic  { get; init; } = "";
    public int    X      { get; init; }
    public int    Y      { get; init; }
    public int    Width  { get; init; }
    public int    Height { get; init; }
    public Dictionary<string, JsonElement> Config { get; init; } = new();
}
