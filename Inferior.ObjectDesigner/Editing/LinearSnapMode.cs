namespace Inferior.ObjectDesigner.Editing;

public enum LinearSnapMode
{
    Off,
    Metre,
    Decimetre,
    Centimetre,
}

public static class LinearSnap
{
    public static double? SpacingFor(LinearSnapMode mode)
        => mode switch
        {
            LinearSnapMode.Off => null,
            LinearSnapMode.Metre => 1.0,
            LinearSnapMode.Decimetre => 0.1,
            LinearSnapMode.Centimetre => 0.01,
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };

    public static string DisplayName(LinearSnapMode mode)
        => mode switch
        {
            LinearSnapMode.Off => "OFF",
            LinearSnapMode.Metre => "1 m",
            LinearSnapMode.Decimetre => "10 cm",
            LinearSnapMode.Centimetre => "1 cm",
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };

    public static double SnapCoordinate(double value, double spacing)
        => Math.Round(value / spacing, MidpointRounding.AwayFromZero) * spacing;

    public static bool IsMultiple(double value, double spacing)
    {
        double nearest = SnapCoordinate(value, spacing);
        return Math.Abs(value - nearest) <= spacing * 1e-8;
    }
}
