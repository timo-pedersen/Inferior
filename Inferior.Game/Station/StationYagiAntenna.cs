using Microsoft.Xna.Framework;

namespace Inferior.Game.StationGen;

internal enum YagiElementType { I, X, O, S, H }

internal enum YagiBaseType { BigDisk, VDisk, Box, OctBox, BigH, Para, BigI }

internal sealed record YagiAntennaParams(
    YagiElementType ElementType,
    YagiBaseType    BaseType,
    int             ElementCount,
    float           ElementSpacing,
    float           ElementHalfLen,
    float           BarThickness,
    float           BoomThickness,
    bool            EndCaps,
    float           BaseScale,
    float           TiltDegrees,
    float           BearingDegrees,
    Color           MetalColour,
    float           MastHeight,
    float           ElemBrightness,
    float           BaseBrightness)
{
    internal static YagiAntennaParams Generate(System.Random rng)
    {
        var elemType    = (YagiElementType)(int)(rng.NextDouble() * 5);
        var baseType    = (YagiBaseType)  (int)(rng.NextDouble() * 7);
        int count       = 3 + (int)(rng.NextDouble() * 7);
        float spacing   = 0.18f + (float)(rng.NextDouble() * 0.07f);
        float halfLen   = 0.15f + (float)(rng.NextDouble() * 0.20f);
        float barT      = 0.010f + (float)(rng.NextDouble() * 0.005f);
        float boomT     = 0.015f + (float)(rng.NextDouble() * 0.010f);
        bool  endCaps   = rng.NextDouble() < 0.60;
        float baseScale = 1.8f   + (float)(rng.NextDouble() * 0.7f);
        float tilt      = (float)(rng.NextDouble() * 90f);  // 0–90° from face normal
        float bearing   = (float)(rng.NextDouble() * 360f);

        Color metal = rng.NextDouble() < 0.15
            ? new Color(90, 95, 85)
            : new Color(
                (int)(70 + rng.NextDouble() * 60),
                (int)(70 + rng.NextDouble() * 60),
                (int)(70 + rng.NextDouble() * 60));

        float mastH      = 1.00f + (float)(rng.NextDouble() * 1.50f);  // 1.0–2.5 m
        float elemBright = 0.85f + (float)(rng.NextDouble() * 0.35f);  // 0.85–1.20
        float baseBright = 0.80f + (float)(rng.NextDouble() * 0.50f);  // 0.80–1.30

        return new(elemType, baseType, count, spacing, halfLen, barT, boomT,
                   endCaps, baseScale, tilt, bearing, metal, mastH, elemBright, baseBright);
    }
}

// Builds a pseudo-Yagi antenna: boom with orthogonal elements and a distinct base element,
// mounted on a straight mast perpendicular to the face.
// All geometry is placed in module-local space via the mount point and face normal.
internal static class StationYagiAntenna
{
    internal static void Build(
        YagiAntennaParams p,
        Vector3           mountPoint,
        Vector3           faceNormal,
        StationModuleMesh mesh)
    {
        // Mast: thin prism rising straight out from the face
        mesh.AddPrismPipe(mountPoint, mountPoint + faceNormal * p.MastHeight,
                          p.BoomThickness * 1.8f, 6, new Color(38, 40, 43));

        // Antenna sits at the mast top
        Vector3 antBase = mountPoint + faceNormal * p.MastHeight;

        // Orientation: same construction as AddParabolicDish — tilt + bearing from face normal
        Vector3 arbitrary = MathF.Abs(faceNormal.Y) < 0.85f ? Vector3.UnitY : Vector3.UnitX;
        Vector3 tiltAxis  = Vector3.Normalize(Vector3.Cross(faceNormal, arbitrary));
        tiltAxis = Vector3.Transform(tiltAxis,
            Quaternion.CreateFromAxisAngle(faceNormal, p.BearingDegrees * MathF.PI / 180f));
        Vector3 boomFwd = Vector3.Normalize(Vector3.Transform(faceNormal,
            Quaternion.CreateFromAxisAngle(tiltAxis, p.TiltDegrees * MathF.PI / 180f)));

        Vector3 boomRight = Vector3.Normalize(Vector3.Cross(boomFwd, Vector3.UnitY));
        if (boomRight.LengthSquared() < 0.01f)
            boomRight = Vector3.Normalize(Vector3.Cross(boomFwd, Vector3.UnitX));
        Vector3 boomUp = Vector3.Normalize(Vector3.Cross(boomRight, boomFwd));

        // Colour variants: standard elements share one brightness, base element gets another
        Color elemColor = Scale(p.MetalColour, p.ElemBrightness);
        Color baseColor = Scale(p.MetalColour, p.BaseBrightness);
        Color boomColor = Scale(p.MetalColour, 0.60f);

        // Mounting block — small pedestal at mast top
        float mountHW = p.BoomThickness * 2.5f;
        float mountH  = 0.04f;
        AddBox(mesh, antBase + boomFwd * (mountH * 0.5f),
               boomRight, boomUp, boomFwd, mountHW, mountHW, mountH * 0.5f,
               new Color(45, 45, 50));

        // Base / reflector element slightly forward from the mount
        float baseZ = p.ElementSpacing * 0.4f;
        AddBaseElement(p, antBase + boomFwd * baseZ, boomRight, boomUp, boomFwd, mesh, baseColor);

        // Standard elements evenly spaced along the boom — all share elemColor
        float[] twists = [0.15f, -0.10f, 0.20f, -0.18f, 0.08f, 0.22f, -0.12f, 0.16f, -0.05f];
        for (int i = 0; i < p.ElementCount; i++)
        {
            float z     = baseZ + p.ElementSpacing * (i + 1);
            float twist = twists[i % twists.Length];
            Quaternion q = Quaternion.CreateFromAxisAngle(boomFwd, twist);
            Vector3 er   = Vector3.Normalize(Vector3.Transform(boomRight, q));
            Vector3 eu   = Vector3.Normalize(Vector3.Transform(boomUp,    q));
            AddElement(p.ElementType, p.ElementHalfLen, p.BarThickness, p.EndCaps,
                       antBase + boomFwd * z, er, eu, boomFwd, mesh, elemColor);
        }

        // Boom rod — darker than elements
        float totalLen = baseZ + p.ElementSpacing * (p.ElementCount + 0.5f);
        AddBox(mesh, antBase + boomFwd * (totalLen * 0.5f),
               boomRight, boomUp, boomFwd,
               p.BoomThickness, p.BoomThickness, totalLen * 0.5f,
               boomColor);
    }

    private static void AddElement(
        YagiElementType type, float halfLen, float t, bool endCaps,
        Vector3 origin, Vector3 right, Vector3 up, Vector3 fwd,
        StationModuleMesh mesh, Color c)
    {
        switch (type)
        {
            case YagiElementType.I:
                AddBox(mesh, origin, right, up, fwd, halfLen, t, t, c);
                if (endCaps)
                {
                    AddBox(mesh, origin - right * halfLen, right, up, fwd, t * 1.5f, t * 1.5f, t * 1.5f, c);
                    AddBox(mesh, origin + right * halfLen, right, up, fwd, t * 1.5f, t * 1.5f, t * 1.5f, c);
                }
                break;

            case YagiElementType.X:
                AddBox(mesh, origin, right, up, fwd, halfLen, t, t, c);
                AddBox(mesh, origin, right, up, fwd, t, halfLen, t, c);
                break;

            case YagiElementType.O:
                AddOctDisk(mesh, origin, right, up, fwd, halfLen, t * 0.5f, c);
                break;

            case YagiElementType.S:
            {
                float q = halfLen * 0.5f;
                AddBox(mesh, origin + up * q, right, up, fwd, halfLen, t,     t, c);
                AddBox(mesh, origin - up * q, right, up, fwd, halfLen, t,     t, c);
                AddBox(mesh, origin,          right, up, fwd, t,       q + t, t, c);
                break;
            }

            case YagiElementType.H:
            {
                float off = halfLen - t;
                AddBox(mesh, origin - right * off, right, up, fwd, t,           halfLen,   t, c);
                AddBox(mesh, origin + right * off, right, up, fwd, t,           halfLen,   t, c);
                AddBox(mesh, origin,               right, up, fwd, halfLen - t, t * 1.2f, t, c);
                break;
            }
        }
    }

    private static void AddBaseElement(
        YagiAntennaParams p,
        Vector3 origin, Vector3 right, Vector3 up, Vector3 fwd,
        StationModuleMesh mesh,
        Color c)
    {
        float s  = p.BaseScale;
        float hl = p.ElementHalfLen * s;
        float t  = p.BarThickness;

        switch (p.BaseType)
        {
            case YagiBaseType.BigDisk:
                AddOctDisk(mesh, origin, right, up, fwd, hl, t * 0.75f, c);
                break;

            case YagiBaseType.VDisk:
                AddBox(mesh, origin - right * (hl * 0.5f) + up * (hl * 0.4f), right, up, fwd, hl * 0.6f, t, t, c);
                AddBox(mesh, origin + right * (hl * 0.5f) + up * (hl * 0.4f), right, up, fwd, hl * 0.6f, t, t, c);
                AddBox(mesh, origin,                                             right, up, fwd, t, hl * 0.4f, t, c);
                break;

            case YagiBaseType.Box:
                AddBox(mesh, origin, right, up, fwd, hl * 0.6f, hl * 0.6f, t * 2f, c);
                break;

            case YagiBaseType.OctBox:
                AddOctDisk(mesh, origin, right, up, fwd, hl, t * 1.5f, c);
                break;

            case YagiBaseType.BigH:
            {
                float off = hl - t * 1.2f;
                AddBox(mesh, origin - right * off, right, up, fwd, t * 1.2f,      hl,        t * 1.2f, c);
                AddBox(mesh, origin + right * off, right, up, fwd, t * 1.2f,      hl,        t * 1.2f, c);
                AddBox(mesh, origin,               right, up, fwd, hl - t * 1.2f, t * 1.44f, t * 1.2f, c);
                break;
            }

            case YagiBaseType.Para:
                AddOctDisk(mesh, origin, right, up, fwd, hl * 0.5f, t * 1.0f, c);
                AddBox(mesh, origin - fwd * (hl * 0.25f), right, up, fwd, t, t, hl * 0.25f, c);
                break;

            case YagiBaseType.BigI:
                AddBox(mesh, origin,              right, up, fwd, hl,       t * 1.2f, t * 1.2f, c);
                AddBox(mesh, origin - right * hl, right, up, fwd, t * 1.8f, t * 1.8f, t * 1.8f, c);
                AddBox(mesh, origin + right * hl, right, up, fwd, t * 1.8f, t * 1.8f, t * 1.8f, c);
                break;
        }
    }

    // Box centred at `center` with half-extents hx/hy/hz along right/up/fwd axes.
    private static void AddBox(
        StationModuleMesh mesh,
        Vector3 center, Vector3 right, Vector3 up, Vector3 fwd,
        float hx, float hy, float hz,
        Color c) =>
        mesh.AddOrientedBox(
            new Matrix(right.X,  right.Y,  right.Z,  0,
                       up.X,     up.Y,     up.Z,     0,
                       fwd.X,    fwd.Y,    fwd.Z,    0,
                       center.X, center.Y, center.Z, 1),
            new Vector3(hx * 2, hy * 2, hz * 2),
            c);

    // Octagonal disc: side faces + end caps so the disc is fully solid when viewed from any angle.
    // right/up params are unused (kept for interface symmetry with AddBox); ring uses fwd tangent frame.
    private static void AddOctDisk(
        StationModuleMesh mesh,
        Vector3 center, Vector3 right, Vector3 up, Vector3 fwd,
        float radius, float halfThick,
        Color c)
    {
        Vector3 startCenter = center - fwd * halfThick;
        Vector3 endCenter   = center + fwd * halfThick;

        // Replicate AddPrismPipe's tangent frame so ring vertices align seamlessly with the sides
        Vector3 arb       = MathF.Abs(fwd.Y) < 0.85f ? Vector3.UnitY : Vector3.UnitX;
        Vector3 pipeRight = Vector3.Normalize(Vector3.Cross(fwd, arb));
        Vector3 pipeUp    = Vector3.Cross(pipeRight, fwd);

        const int Sides = 8;
        var startRing = new Vector3[Sides];
        var endRing   = new Vector3[Sides];
        float step = MathF.PI * 2f / Sides;
        for (int i = 0; i < Sides; i++)
        {
            float a  = i * step;
            var   off = pipeRight * (radius * MathF.Cos(a)) + pipeUp * (radius * MathF.Sin(a));
            startRing[i] = startCenter + off;
            endRing[i]   = endCenter   + off;
        }

        // Side faces (same quad order as AddPrismPipe)
        for (int i = 0; i < Sides; i++)
        {
            int next = (i + 1) % Sides;
            mesh.AddQuad(startRing[next], startRing[i], endRing[i], endRing[next], c);
        }

        // Back cap (outward normal = -fwd): AddTriangle(v0,v1,v2) renders (v0,v2,v1);
        // Cross(v1-v0, v2-v0) gives -fwd given the CW-from-outside ring ordering below.
        for (int i = 0; i < Sides; i++)
        {
            int next = (i + 1) % Sides;
            mesh.AddTriangle(startCenter, startRing[i], startRing[next], c);
        }

        // Front cap (outward normal = +fwd): reversed ring order to flip normal to +fwd.
        for (int i = 0; i < Sides; i++)
        {
            int next = (i + 1) % Sides;
            mesh.AddTriangle(endCenter, endRing[next], endRing[i], c);
        }
    }

    private static Color Scale(Color c, float f) =>
        new(Math.Min(255, (int)(c.R * f)),
            Math.Min(255, (int)(c.G * f)),
            Math.Min(255, (int)(c.B * f)));
}
