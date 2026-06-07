namespace Inferior.Persistence.Data;

public record ConsumablesRecord
{
    public double ReactorFuel { get; init; }
    public double Coolant { get; init; }
    public int    FuelRods { get; init; }
}
