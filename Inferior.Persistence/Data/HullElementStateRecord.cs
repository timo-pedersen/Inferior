namespace Inferior.Persistence.Data;

public record HullElementStateRecord
{
    public int    FaceIndex { get; init; }
    public string PanelId   { get; init; } = "";
    public double Integrity { get; init; } = 1.0;
}
