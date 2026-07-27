using Microsoft.Xna.Framework;

namespace Inferior.ObjectDesigner.Editing;

public readonly record struct MetricGridLine(double Coordinate, bool Vertical, double Spacing, float Opacity);

public static class MetricGrid
{
    public const double MetreSpacing = 1.0;
    public const double DecimetreSpacing = 0.1;
    public const double CentimetreSpacing = 0.01;
    public const double DecimetreFadeStartPixels = 5.0;
    public const double DecimetreFullyVisiblePixels = 8.0;
    public const double CentimetreFadeStartPixels = 5.0;
    public const double CentimetreFullyVisiblePixels = 8.0;
    public const float MetreStrength = 1.0f;
    public const float DecimetreStrength = 0.55f;
    public const float CentimetreStrength = 0.2f;

    public static readonly double[] Spacings = [MetreSpacing, DecimetreSpacing, CentimetreSpacing];

    public static IReadOnlyList<MetricGridLine> Generate(OrthographicProjection projection, Rectangle viewport)
    {
        (Vector2 min, Vector2 max) = projection.VisibleProjectionBounds(viewport);
        var lines = new List<MetricGridLine>();
        for (int level = 0; level < Spacings.Length; level++)
        {
            double spacing = Spacings[level];
            float opacity = OpacityForSpacing(projection.PixelsPerMeter, spacing);
            if (opacity <= 0f)
                continue;

            AddLines(lines, min.X, max.X, vertical: true, spacing, level, opacity);
            AddLines(lines, min.Y, max.Y, vertical: false, spacing, level, opacity);
        }

        return lines;
    }

    public static float OpacityForSpacing(float pixelsPerMeter, double spacing)
    {
        double pixels = pixelsPerMeter * spacing;
        return spacing switch
        {
            MetreSpacing => 1f,
            DecimetreSpacing => SmoothStep(DecimetreFadeStartPixels, DecimetreFullyVisiblePixels, pixels),
            CentimetreSpacing => SmoothStep(CentimetreFadeStartPixels, CentimetreFullyVisiblePixels, pixels),
            _ => 0f,
        };
    }

    public static float StrengthForSpacing(double spacing)
        => spacing switch
        {
            MetreSpacing => MetreStrength,
            DecimetreSpacing => DecimetreStrength,
            CentimetreSpacing => CentimetreStrength,
            _ => 0f,
        };

    private static void AddLines(
        List<MetricGridLine> lines,
        double min,
        double max,
        bool vertical,
        double spacing,
        int level,
        float opacity)
    {
        long first = (long)Math.Ceiling((min - spacing * 1e-8) / spacing);
        long last = (long)Math.Floor((max + spacing * 1e-8) / spacing);
        for (long i = first; i <= last; i++)
        {
            double coordinate = i * spacing;
            if (BelongsToCoarserLevel(coordinate, level))
                continue;
            lines.Add(new MetricGridLine(coordinate, vertical, spacing, opacity));
        }
    }

    private static bool BelongsToCoarserLevel(double coordinate, int level)
    {
        for (int i = 0; i < level; i++)
        {
            if (LinearSnap.IsMultiple(coordinate, Spacings[i]))
                return true;
        }
        return false;
    }

    private static float SmoothStep(double begin, double full, double value)
    {
        if (value <= begin)
            return 0f;
        if (value >= full)
            return 1f;
        double t = (value - begin) / (full - begin);
        return (float)(t * t * (3 - 2 * t));
    }
}
