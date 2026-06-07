using System.Text.Json;
using Inferior.Core;

namespace Inferior.Persistence.Data;

public record InstalledComponentRecord
{
    public string        SlotId        { get; init; } = "";
    public string        TypeId        { get; init; } = "";
    public double        DamageLevel   { get; init; }
    public string        PowerBusId    { get; init; } = "";
    public PowerPriority PowerPriority { get; init; }
    public Dictionary<string, JsonElement> Settings { get; init; } = new();
}
