using Inferior.Core.Math;
using Microsoft.Xna.Framework;

namespace Inferior.ObjectDesigner.Editing;

public static class OrthographicNavigation
{
    public const double CentimetreGridSpacingMetres = 0.01;
    public const double MinimumCentimetreGridPixels = 10.0;
    public const float MaximumPixelsPerMeter = (float)(MinimumCentimetreGridPixels / CentimetreGridSpacingMetres);
    public const float MinimumPixelsPerMeter = 4f;
    public const double MinimumWorldUnitsPerPixel = 1.0 / MaximumPixelsPerMeter;
    public const double MaximumWorldUnitsPerPixel = 1.0 / MinimumPixelsPerMeter;
    public const float ZoomStepFactor = 1.18f;
    public const double MiddleDoubleClickSeconds = 0.32;
    public const int MiddleDoubleClickMaxPixels = 8;

    public static float ClampPixelsPerMeter(float pixelsPerMeter)
        => MathHelper.Clamp(pixelsPerMeter, MinimumPixelsPerMeter, MaximumPixelsPerMeter);

    public static float ApplyWheelZoom(float currentPixelsPerMeter, int scrollDelta)
    {
        if (scrollDelta == 0)
            return ClampPixelsPerMeter(currentPixelsPerMeter);

        int detents = scrollDelta / 120;
        if (detents == 0)
            detents = scrollDelta > 0 ? 1 : -1;

        float factor = MathF.Pow(ZoomStepFactor, Math.Abs(detents));
        float scaled = detents > 0 ? currentPixelsPerMeter * factor : currentPixelsPerMeter / factor;
        return ClampPixelsPerMeter(scaled);
    }

    public static void ZoomAroundCursor(OrthographicProjection projection, Rectangle viewport, Point cursor, int scrollDelta)
    {
        Vector2 before = projection.ScreenToProjectionAxes(cursor, viewport);
        float next = ApplyWheelZoom(projection.PixelsPerMeter, scrollDelta);
        projection.PixelsPerMeter = next;
        projection.CenterProjectionAxesAtScreenPoint(before, cursor, viewport);
    }

    public static Vector2 HullBoundsCenter(IEnumerable<DVec3> positions, OrthographicProjection projection)
    {
        Vector2 min = new(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 max = new(float.NegativeInfinity, float.NegativeInfinity);
        bool any = false;
        foreach (DVec3 position in positions)
        {
            Vector2 axes = projection.ToProjectionAxes(position);
            min = Vector2.Min(min, axes);
            max = Vector2.Max(max, axes);
            any = true;
        }

        return any ? (min + max) * 0.5f : Vector2.Zero;
    }

    public static Vector2 Centroid(IEnumerable<DVec3> positions, OrthographicProjection projection)
    {
        Vector2 sum = Vector2.Zero;
        int count = 0;
        foreach (DVec3 position in positions)
        {
            sum += projection.ToProjectionAxes(position);
            count++;
        }

        return count == 0 ? Vector2.Zero : sum / count;
    }
}
