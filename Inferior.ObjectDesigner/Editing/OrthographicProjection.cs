using Inferior.Core.Math;
using Microsoft.Xna.Framework;

namespace Inferior.ObjectDesigner.Editing;

public sealed class OrthographicProjection
{
    public ProjectionKind Kind { get; set; } = ProjectionKind.Top;
    public float PixelsPerMeter { get; set; } = 28.0f;
    public Vector2 PanPixels { get; set; } = Vector2.Zero;

    public Vector2 Project(DVec3 point, Rectangle viewport)
    {
        Vector2 axes = ToAxes(point);
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

    private Vector2 ToAxes(DVec3 point) => Kind switch
    {
        ProjectionKind.Top => new Vector2((float)point.X, (float)point.Z),
        ProjectionKind.Side => new Vector2((float)point.Z, (float)point.Y),
        ProjectionKind.Front => new Vector2((float)point.X, (float)point.Y),
        _ => throw new ArgumentOutOfRangeException(nameof(Kind)),
    };
}
