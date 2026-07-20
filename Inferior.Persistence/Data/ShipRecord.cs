namespace Inferior.Persistence.Data;

public record ShipRecord
{
    public const int CurrentVersion = 1;

    public int      SchemaVersion { get; init; } = CurrentVersion;
    public string   Id            { get; init; } = "";
    public string   HullTypeId    { get; init; } = "";
    public string?  Name          { get; init; }
    public DateTime CreatedDate   { get; init; }

    public InstalledCockpitRecord? Cockpit { get; init; }
    public InstalledComponentRecord[] Components   { get; init; } = [];
    public CockpitLayoutRecord        PanelLayout  { get; init; } = new();
    public HullElementStateRecord[]   HullElements { get; init; } = [];
    public ConsumablesRecord          Consumables  { get; init; } = new();
}
