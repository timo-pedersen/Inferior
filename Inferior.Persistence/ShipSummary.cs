namespace Inferior.Persistence;

public record ShipSummary
{
    public string  Id         { get; init; } = "";
    public string  HullTypeId { get; init; } = "";
    public string? Name       { get; init; }
}
