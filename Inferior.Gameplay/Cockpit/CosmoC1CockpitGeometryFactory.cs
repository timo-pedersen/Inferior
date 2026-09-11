using Inferior.Core.Math;

namespace Inferior.Gameplay.Cockpit;

public static class CosmoC1CockpitGeometryFactory
{
    public const string GeometryId = "cosmo-c1-sport-cockpit.geometry.v1";

    public static CockpitVisualGeometry Create()
    {
        var parts = Enum.GetValues<CockpitVisualMaterial>()
            .ToDictionary(material => material, _ => new List<CockpitVisualTriangle>());

        AddBox(parts[CockpitVisualMaterial.MountingBase],
            new DVec3(-0.50, -0.72, -0.50),
            new DVec3(0.50, -0.18, 0.50));
        AddBox(parts[CockpitVisualMaterial.Housing],
            new DVec3(-0.62, -0.22, -0.74),
            new DVec3(0.62, 0.10, 0.58));

        DVec3[] canopyLower =
        [
            new(-0.52, 0.06, -0.70),
            new( 0.52, 0.06, -0.70),
            new( 0.46, 0.06,  0.42),
            new(-0.46, 0.06,  0.42),
        ];
        DVec3[] canopyUpper =
        [
            new(-0.30, 0.64, -0.42),
            new( 0.30, 0.64, -0.42),
            new( 0.26, 0.64,  0.22),
            new(-0.26, 0.64,  0.22),
        ];
        AddRingShell(parts[CockpitVisualMaterial.Canopy], canopyLower, canopyUpper);
        AddBox(parts[CockpitVisualMaterial.Interior],
            new DVec3(-0.36, 0.12, -0.54),
            new DVec3(0.36, 0.50, 0.28));

        const double frameWidth = 0.055;
        for (int i = 0; i < 4; i++)
        {
            AddBeam(parts[CockpitVisualMaterial.Frame],
                canopyLower[i], canopyLower[(i + 1) % 4], frameWidth);
            AddBeam(parts[CockpitVisualMaterial.Frame],
                canopyUpper[i], canopyUpper[(i + 1) % 4], frameWidth);
            AddBeam(parts[CockpitVisualMaterial.Frame],
                canopyLower[i], canopyUpper[i], frameWidth);
        }

        AddBox(parts[CockpitVisualMaterial.CanopyLight],
            new DVec3(-0.42, 0.10, -0.76),
            new DVec3(-0.18, 0.20, -0.70));
        AddBox(parts[CockpitVisualMaterial.CanopyLight],
            new DVec3(0.18, 0.10, -0.76),
            new DVec3(0.42, 0.20, -0.70));
        AddBox(parts[CockpitVisualMaterial.InternalGlow],
            new DVec3(-0.24, 0.20, -0.64),
            new DVec3(0.24, 0.34, -0.62));

        CockpitVisualMeshPart[] meshParts = parts
            .Where(pair => pair.Value.Count > 0)
            .Select(pair => new CockpitVisualMeshPart(
                $"{GeometryId}.{pair.Key}",
                pair.Key,
                Array.AsReadOnly(pair.Value.ToArray())))
            .ToArray();
        return new CockpitVisualGeometry(GeometryId, meshParts);
    }

    private static void AddRingShell(
        List<CockpitVisualTriangle> triangles,
        IReadOnlyList<DVec3> lower,
        IReadOnlyList<DVec3> upper)
    {
        for (int i = 0; i < lower.Count; i++)
        {
            int next = (i + 1) % lower.Count;
            DVec3 outward = ((lower[i] + lower[next] + upper[next] + upper[i]) / 4.0)
                - DVec3.Zero;
            AddQuad(triangles, lower[i], lower[next], upper[next], upper[i], outward);
        }

        AddQuad(triangles, upper[0], upper[1], upper[2], upper[3], DVec3.UnitY);
    }

    private static void AddBox(
        List<CockpitVisualTriangle> triangles,
        DVec3 min,
        DVec3 max)
    {
        DVec3 p000 = new(min.X, min.Y, min.Z);
        DVec3 p100 = new(max.X, min.Y, min.Z);
        DVec3 p110 = new(max.X, max.Y, min.Z);
        DVec3 p010 = new(min.X, max.Y, min.Z);
        DVec3 p001 = new(min.X, min.Y, max.Z);
        DVec3 p101 = new(max.X, min.Y, max.Z);
        DVec3 p111 = new(max.X, max.Y, max.Z);
        DVec3 p011 = new(min.X, max.Y, max.Z);

        AddQuad(triangles, p000, p010, p110, p100, -DVec3.UnitZ);
        AddQuad(triangles, p101, p111, p011, p001, DVec3.UnitZ);
        AddQuad(triangles, p001, p011, p010, p000, -DVec3.UnitX);
        AddQuad(triangles, p100, p110, p111, p101, DVec3.UnitX);
        AddQuad(triangles, p010, p011, p111, p110, DVec3.UnitY);
        AddQuad(triangles, p001, p000, p100, p101, -DVec3.UnitY);
    }

    private static void AddBeam(
        List<CockpitVisualTriangle> triangles,
        DVec3 start,
        DVec3 end,
        double width)
    {
        DVec3 axis = (end - start).Normalized();
        DVec3 reference = Math.Abs(DVec3.Dot(axis, DVec3.UnitY)) < 0.9
            ? DVec3.UnitY
            : DVec3.UnitX;
        DVec3 side = DVec3.Cross(reference, axis).Normalized() * (width * 0.5);
        DVec3 up = DVec3.Cross(axis, side).Normalized() * (width * 0.5);
        DVec3[] ringA =
        [
            start - side - up,
            start + side - up,
            start + side + up,
            start - side + up,
        ];
        DVec3[] ringB =
        [
            end - side - up,
            end + side - up,
            end + side + up,
            end - side + up,
        ];
        DVec3 center = (start + end) * 0.5;
        for (int i = 0; i < 4; i++)
        {
            int next = (i + 1) % 4;
            DVec3 faceCenter = (ringA[i] + ringA[next] + ringB[next] + ringB[i]) / 4.0;
            AddQuad(triangles, ringA[i], ringA[next], ringB[next], ringB[i], faceCenter - center);
        }
        AddQuad(triangles, ringA[3], ringA[2], ringA[1], ringA[0], start - end);
        AddQuad(triangles, ringB[0], ringB[1], ringB[2], ringB[3], end - start);
    }

    private static void AddQuad(
        List<CockpitVisualTriangle> triangles,
        DVec3 a,
        DVec3 b,
        DVec3 c,
        DVec3 d,
        DVec3 outward)
    {
        if (DVec3.Dot(DVec3.Cross(b - a, c - a), outward) < 0.0)
            (b, d) = (d, b);
        triangles.Add(new CockpitVisualTriangle(a, b, c));
        triangles.Add(new CockpitVisualTriangle(a, c, d));
    }
}
