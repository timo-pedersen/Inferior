using Inferior.Core.Math;
using Microsoft.Xna.Framework;

namespace Inferior.ObjectDesigner.Editing;

public sealed class OrthographicProjection
{
    public const double FacePlaneLineModeEpsilon = 1e-3;

    public ProjectionKind Kind { get; set; } = ProjectionKind.Top;
    public float PixelsPerMeter { get; set; } = 28.0f;
    public Vector2 PanPixels { get; set; } = Vector2.Zero;

    public Vector2 Project(DVec3 point, Rectangle viewport)
    {
        Vector2 axes = ToProjectionAxes(point);
        return new Vector2(
            viewport.X + viewport.Width * 0.5f + PanPixels.X + axes.X * PixelsPerMeter,
            viewport.Y + viewport.Height * 0.5f + PanPixels.Y - axes.Y * PixelsPerMeter);
    }

    public DVec3 ApplyScreenDelta(DVec3 original, Vector2 screenDelta)
    {
        double a = screenDelta.X / PixelsPerMeter;
        double b = -screenDelta.Y / PixelsPerMeter;
        return Kind switch
        {
            ProjectionKind.Top => new DVec3(original.X + a, original.Y, original.Z + b),
            ProjectionKind.Side => new DVec3(original.X, original.Y + b, original.Z + a),
            ProjectionKind.Front => new DVec3(original.X + a, original.Y + b, original.Z),
            _ => throw new ArgumentOutOfRangeException(nameof(Kind)),
        };
    }

    public string HorizontalAxisLabel => Kind switch
    {
        ProjectionKind.Top => "+X right",
        ProjectionKind.Side => "+Z rearward",
        ProjectionKind.Front => "+X right",
        _ => "",
    };

    public string VerticalAxisLabel => Kind switch
    {
        ProjectionKind.Top => "+Z rearward",
        ProjectionKind.Side => "+Y up",
        ProjectionKind.Front => "+Y up",
        _ => "",
    };

    public Vector2 ToProjectionAxes(DVec3 point) => Kind switch
    {
        ProjectionKind.Top => new Vector2((float)point.X, (float)point.Z),
        ProjectionKind.Side => new Vector2((float)point.Z, (float)point.Y),
        ProjectionKind.Front => new Vector2((float)point.X, (float)point.Y),
        _ => throw new ArgumentOutOfRangeException(nameof(Kind)),
    };

    public DVec3 ViewDirection => Kind switch
    {
        ProjectionKind.Top => DVec3.UnitY,
        ProjectionKind.Side => DVec3.UnitX,
        ProjectionKind.Front => DVec3.UnitZ,
        _ => throw new ArgumentOutOfRangeException(nameof(Kind)),
    };

    public DVec3 HorizontalAxis => Kind switch
    {
        ProjectionKind.Top => DVec3.UnitX,
        ProjectionKind.Side => DVec3.UnitZ,
        ProjectionKind.Front => DVec3.UnitX,
        _ => throw new ArgumentOutOfRangeException(nameof(Kind)),
    };

    public DVec3 VerticalAxis => Kind switch
    {
        ProjectionKind.Top => DVec3.UnitZ,
        ProjectionKind.Side => DVec3.UnitY,
        ProjectionKind.Front => DVec3.UnitY,
        _ => throw new ArgumentOutOfRangeException(nameof(Kind)),
    };

    public double WorldUnitsPerPixel => 1.0 / PixelsPerMeter;

    public DVec3 ScreenDeltaToWorldPlaneDelta(Point startMouse, Point mousePosition)
        => ScreenDeltaToWorldPlaneDelta((mousePosition - startMouse).ToVector2());

    public DVec3 ScreenDeltaToWorldPlaneDelta(Vector2 screenDelta)
    {
        double a = screenDelta.X * WorldUnitsPerPixel;
        double b = -screenDelta.Y * WorldUnitsPerPixel;
        return HorizontalAxis * a + VerticalAxis * b;
    }

    public (DVec3 Origin, DVec3 Direction) RayFromScreen(Point mouse, Rectangle viewport)
    {
        Vector2 axes = ScreenToProjectionAxes(mouse, viewport);
        return Kind switch
        {
            ProjectionKind.Top => (new DVec3(axes.X, 0, axes.Y), DVec3.UnitY),
            ProjectionKind.Side => (new DVec3(0, axes.Y, axes.X), DVec3.UnitX),
            ProjectionKind.Front => (new DVec3(axes.X, axes.Y, 0), DVec3.UnitZ),
            _ => throw new ArgumentOutOfRangeException(nameof(Kind)),
        };
    }

    public bool TryIntersectScreenRayWithPlane(
        Point mouse,
        Rectangle viewport,
        DVec3 planeOrigin,
        DVec3 planeNormal,
        out DVec3 point)
    {
        (DVec3 rayOrigin, DVec3 rayDirection) = RayFromScreen(mouse, viewport);
        double denom = DVec3.Dot(rayDirection, planeNormal);
        if (Math.Abs(denom) < FacePlaneLineModeEpsilon)
        {
            point = DVec3.Zero;
            return false;
        }
        double t = DVec3.Dot(planeOrigin - rayOrigin, planeNormal) / denom;
        point = rayOrigin + rayDirection * t;
        return IsFinite(point);
    }

    public Vector2 ProjectionAxesToScreen(Vector2 axes, Rectangle viewport)
        => new(
            viewport.X + viewport.Width * 0.5f + PanPixels.X + axes.X * PixelsPerMeter,
            viewport.Y + viewport.Height * 0.5f + PanPixels.Y - axes.Y * PixelsPerMeter);

    public (Vector2 Min, Vector2 Max) VisibleProjectionBounds(Rectangle viewport)
    {
        Vector2 a = ScreenToProjectionAxes(new Point(viewport.Left, viewport.Top), viewport);
        Vector2 b = ScreenToProjectionAxes(new Point(viewport.Right, viewport.Bottom), viewport);
        return (
            new Vector2(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)),
            new Vector2(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)));
    }

    public Vector2 ScreenToProjectionAxes(Point mouse, Rectangle viewport)
        => new(
            (mouse.X - viewport.X - viewport.Width * 0.5f - PanPixels.X) / PixelsPerMeter,
            -(mouse.Y - viewport.Y - viewport.Height * 0.5f - PanPixels.Y) / PixelsPerMeter);

    public void CenterOnProjectionAxes(Vector2 axes)
        => PanPixels = new Vector2(-axes.X * PixelsPerMeter, axes.Y * PixelsPerMeter);

    public void CenterProjectionAxesAtScreenPoint(Vector2 axes, Point screenPoint, Rectangle viewport)
        => PanPixels = new Vector2(
            screenPoint.X - viewport.X - viewport.Width * 0.5f - axes.X * PixelsPerMeter,
            screenPoint.Y - viewport.Y - viewport.Height * 0.5f + axes.Y * PixelsPerMeter);

    private static bool IsFinite(DVec3 value)
        => double.IsFinite(value.X) && double.IsFinite(value.Y) && double.IsFinite(value.Z);
}
