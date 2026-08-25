using Microsoft.Xna.Framework;

namespace Inferior.Game.StationGen;

internal readonly record struct StationSurfaceFrame(
    Vector3 Origin,
    Vector3 Normal,
    Vector3 Right,
    Vector3 Up)
{
    public Vector3 Point(float u, float v, float outward)
        => Origin + Right * u + Up * v + Normal * outward;

    public Matrix Transform(Vector3 centre) => new(
        Right.X, Right.Y, Right.Z, 0f,
        Up.X, Up.Y, Up.Z, 0f,
        Normal.X, Normal.Y, Normal.Z, 0f,
        centre.X, centre.Y, centre.Z, 1f);
}

// Stateless geometry vocabulary shared by ordinary-station decoration and native
// megastation infrastructure. Placement, probability, occupancy, and RNG stay with
// their respective planners/wrappers.
internal static class StationIndustrialPrimitives
{
    public static void EmitJunctionBox(
        StationModuleMesh mesh, StationSurfaceFrame frame,
        float width, float height, float depth, Color body, Color seam)
    {
        mesh.AddOrientedBox(frame.Transform(frame.Point(0f, 0f, depth * 0.5f)),
            new Vector3(width, height, depth), body);
        mesh.AddQuad(
            frame.Point(-width * 0.5f, -0.02f, depth + 0.005f),
            frame.Point(+width * 0.5f, -0.02f, depth + 0.005f),
            frame.Point(+width * 0.5f, +0.02f, depth + 0.005f),
            frame.Point(-width * 0.5f, +0.02f, depth + 0.005f), seam);
    }

    public static void EmitEquipmentHousing(
        StationModuleMesh mesh, StationSurfaceFrame frame,
        float width, float height, float baseDepth, float topDepth,
        float topWidth, float topHeight, float topOffsetU,
        Color body, Color detail)
    {
        mesh.AddOrientedBox(frame.Transform(frame.Point(0f, 0f, baseDepth * 0.5f)),
            new Vector3(width, height, baseDepth), body);
        mesh.AddOrientedBox(frame.Transform(frame.Point(topOffsetU, 0f, baseDepth + topDepth * 0.5f)),
            new Vector3(topWidth, topHeight, topDepth), detail);
    }

    public static void EmitConduitEntry(
        StationModuleMesh mesh, StationSurfaceFrame frame,
        float width, float height, float depth, float pipeLength, float pipeRadius,
        Color body, Color pipe)
    {
        mesh.AddOrientedBox(frame.Transform(frame.Point(0f, 0f, depth * 0.5f)),
            new Vector3(width, height, depth), body);
        mesh.AddPrismPipe(frame.Point(-pipeLength, 0f, depth * 0.5f),
            frame.Point(0f, 0f, depth * 0.5f), pipeRadius, 6, pipe, capStart: true);
    }

    public static void EmitHorizontalBarVent(
        StationModuleMesh mesh, StationSurfaceFrame frame,
        float width, float height, bool horizontal, int barCount,
        Color frameColour, Color barColour)
    {
        EmitVentBacking(mesh, frame, width, height, new Color(12, 12, 14));
        const float thickness = 0.04f;
        for (int i = 0; i < barCount; i++)
        {
            float t = (i + 0.5f) / barCount;
            float position = horizontal ? -height * 0.5f + height * t : -width * 0.5f + width * t;
            float u0 = horizontal ? -width * 0.5f : position - thickness * 0.5f;
            float v0 = horizontal ? position - thickness * 0.5f : -height * 0.5f;
            float u1 = horizontal ? +width * 0.5f : position + thickness * 0.5f;
            float v1 = horizontal ? position + thickness * 0.5f : +height * 0.5f;
            AddSurfaceQuad(mesh, frame, u0, v0, u1, v1, 0.030f, barColour);
        }
        EmitVentFrame(mesh, frame, width, height, frameColour);
    }

    public static void EmitLouveredVent(
        StationModuleMesh mesh, StationSurfaceFrame frame,
        float width, float height, int slatCount, Color frameColour, Color slatColour)
    {
        EmitVentBacking(mesh, frame, width, height, new Color(10, 10, 12));
        float step = height / slatCount;
        for (int i = 0; i < slatCount; i++)
        {
            float centre = -height * 0.5f + step * (i + 0.5f);
            float bottom = centre - step * 0.3f;
            float top = centre + step * 0.3f;
            mesh.AddQuad(frame.Point(-width * 0.5f, bottom, 0.022f),
                frame.Point(+width * 0.5f, bottom, 0.022f),
                frame.Point(+width * 0.5f, top, 0.067f),
                frame.Point(-width * 0.5f, top, 0.067f), slatColour);
        }
        EmitVentFrame(mesh, frame, width, height, frameColour);
    }

    public static void EmitScreenVent(
        StationModuleMesh mesh, StationSurfaceFrame frame,
        float width, float height, Color frameColour, Color wireColour)
    {
        EmitVentBacking(mesh, frame, width, height, new Color(8, 8, 10));
        const float wire = 0.025f;
        int horizontalCount = Math.Max(1, (int)(width / 0.35f));
        int verticalCount = Math.Max(1, (int)(height / 0.35f));
        for (int i = 1; i < verticalCount; i++)
        {
            float v = -height * 0.5f + height * i / verticalCount;
            AddSurfaceQuad(mesh, frame, -width * 0.5f, v - wire * 0.5f,
                width * 0.5f, v + wire * 0.5f, 0.026f, wireColour);
        }
        for (int i = 1; i < horizontalCount; i++)
        {
            float u = -width * 0.5f + width * i / horizontalCount;
            AddSurfaceQuad(mesh, frame, u - wire * 0.5f, -height * 0.5f,
                u + wire * 0.5f, height * 0.5f, 0.031f, wireColour);
        }
        EmitVentFrame(mesh, frame, width, height, frameColour);
    }

    public static void EmitTankCore(
        StationModuleMesh mesh, Vector3 start, Vector3 end, float radius,
        Color body, Color stripe, int stripeCount, Vector3 attachPoint,
        DecorClass? detailClass = null)
    {
        const int sides = 8;
        mesh.AddPrismPipe(start, end, radius, sides, body);
        var (startRing, endRing) = PrismRings(start, end, radius, sides);
        Vector3 axis = Vector3.Normalize(end - start);
        float tipRadius = radius * 0.28f;
        float capDepth = radius * 0.50f;
        Array.Reverse(startRing);
        EmitTankCap(mesh, startRing, -axis, tipRadius, capDepth, body);
        EmitTankCap(mesh, endRing, axis, tipRadius, capDepth, body);
        if (detailClass.HasValue)
            mesh.CurrentDecorClass = detailClass.Value;
        float stripeWidth = MathF.Max(radius * 0.08f, 0.04f);
        for (int i = 1; i <= stripeCount; i++)
        {
            Vector3 centre = Vector3.Lerp(start, end, (float)i / (stripeCount + 1));
            mesh.AddPrismPipe(centre - axis * stripeWidth, centre + axis * stripeWidth,
                radius * 1.04f, sides, stripe);
        }
        bool useStart = Vector3.Distance(start, attachPoint) < Vector3.Distance(end, attachPoint);
        Vector3 capTip = useStart ? start - axis * capDepth : end + axis * capDepth;
        mesh.AddPrismPipe(capTip, attachPoint, radius * 0.18f, 6, stripe);
    }

    private static void EmitVentBacking(StationModuleMesh mesh, StationSurfaceFrame frame,
        float width, float height, Color backing)
    {
        AddSurfaceQuad(mesh, frame, -width * 0.5f, -height * 0.5f,
            width * 0.5f, height * 0.5f, 0.018f, backing);
    }

    private static void EmitVentFrame(StationModuleMesh mesh, StationSurfaceFrame frame,
        float width, float height, Color frameColour)
    {
        const float fw = 0.12f;
        AddSurfaceQuad(mesh, frame, -width * 0.5f - fw, height * 0.5f,
            width * 0.5f + fw, height * 0.5f + fw, 0.025f, frameColour);
        AddSurfaceQuad(mesh, frame, -width * 0.5f - fw, -height * 0.5f - fw,
            width * 0.5f + fw, -height * 0.5f, 0.025f, frameColour);
        AddSurfaceQuad(mesh, frame, -width * 0.5f - fw, -height * 0.5f,
            -width * 0.5f, height * 0.5f, 0.025f, frameColour);
        AddSurfaceQuad(mesh, frame, width * 0.5f, -height * 0.5f,
            width * 0.5f + fw, height * 0.5f, 0.025f, frameColour);
    }

    private static void AddSurfaceQuad(StationModuleMesh mesh, StationSurfaceFrame frame,
        float u0, float v0, float u1, float v1, float outward, Color colour)
        => mesh.AddQuad(frame.Point(u0, v0, outward), frame.Point(u1, v0, outward),
            frame.Point(u1, v1, outward), frame.Point(u0, v1, outward), colour);

    private static (Vector3[] Start, Vector3[] End) PrismRings(
        Vector3 start, Vector3 end, float radius, int sides)
    {
        Vector3 direction = Vector3.Normalize(end - start);
        Vector3 arbitrary = MathF.Abs(direction.Y) < 0.85f ? Vector3.UnitY : Vector3.UnitX;
        Vector3 right = Vector3.Normalize(Vector3.Cross(direction, arbitrary));
        Vector3 up = Vector3.Normalize(Vector3.Cross(right, direction));
        var startRing = new Vector3[sides];
        var endRing = new Vector3[sides];
        for (int i = 0; i < sides; i++)
        {
            float angle = i * MathF.Tau / sides;
            Vector3 offset = right * MathF.Cos(angle) * radius + up * MathF.Sin(angle) * radius;
            startRing[i] = start + offset;
            endRing[i] = end + offset;
        }
        return (startRing, endRing);
    }

    private static void EmitTankCap(StationModuleMesh mesh, Vector3[] bodyRing,
        Vector3 outward, float tipRadius, float capDepth, Color colour)
    {
        Vector3 bodyCentre = Vector3.Zero;
        foreach (Vector3 point in bodyRing) bodyCentre += point;
        bodyCentre /= bodyRing.Length;
        Vector3 tipCentre = bodyCentre + outward * capDepth;
        var tipRing = new Vector3[bodyRing.Length];
        for (int i = 0; i < bodyRing.Length; i++)
            tipRing[i] = tipCentre + Vector3.Normalize(bodyRing[i] - bodyCentre) * tipRadius;
        for (int i = 0; i < bodyRing.Length; i++)
        {
            int next = (i + 1) % bodyRing.Length;
            mesh.AddQuad(bodyRing[next], bodyRing[i], tipRing[i], tipRing[next], colour);
        }
        for (int i = 0; i < bodyRing.Length; i++)
        {
            int next = (i + 1) % bodyRing.Length;
            mesh.AddTriangle(tipCentre, tipRing[next], tipRing[i], colour);
        }
    }
}
