using Inferior.Core.Math;
using Microsoft.Xna.Framework;

namespace Inferior.Gameplay.Cockpit;

public static class AntegaCivilianBridgeGeometryFactory
{
    public const string GeometryId = "antega-civilian-bridge.geometry.v1";

    public static CockpitVisualGeometry Create()
    {
        var parts = Enum.GetValues<CockpitVisualMaterial>()
            .ToDictionary(material => material, _ => new List<CockpitVisualTriangle>());

        AddBox(
            parts[CockpitVisualMaterial.MountingBase],
            new DVec3(-2.0, -2.0, -3.0),
            new DVec3(2.0, 0.15, 3.0));
        AddBox(
            parts[CockpitVisualMaterial.MountingBase],
            new DVec3(-5.9, -0.15, -4.9),
            new DVec3(5.9, 0.70, 4.9));

        Vector2[] housingSection =
        [
            new(0.45f, -5.00f),
            new(2.55f, -4.85f),
            new(4.25f, -3.35f),
            new(4.35f, 2.35f),
            new(3.35f, 4.65f),
            new(0.45f, 5.00f),
        ];
        AddPrism(
            parts[CockpitVisualMaterial.Housing],
            -5.75,
            5.75,
            housingSection);

        Vector2[] canopySection =
        [
            new(2.20f, -4.94f),
            new(3.86f, -4.30f),
            new(4.08f, -2.15f),
            new(2.65f, -1.45f),
        ];
        AddPrism(
            parts[CockpitVisualMaterial.Canopy],
            -4.85,
            4.85,
            canopySection);
        AddBox(
            parts[CockpitVisualMaterial.Interior],
            new DVec3(-4.55, 2.25, -4.70),
            new DVec3(4.55, 3.78, -1.70));

        AddBox(
            parts[CockpitVisualMaterial.Housing],
            new DVec3(-5.40, 0.72, 2.20),
            new DVec3(5.40, 3.45, 4.55));
        AddBox(
            parts[CockpitVisualMaterial.Frame],
            new DVec3(-5.05, 2.08, -5.04),
            new DVec3(5.05, 2.30, -4.92));
        AddBox(
            parts[CockpitVisualMaterial.Frame],
            new DVec3(-5.05, 3.92, -4.42),
            new DVec3(5.05, 4.14, -4.20));
        AddBox(
            parts[CockpitVisualMaterial.Frame],
            new DVec3(-5.03, 2.20, -5.02),
            new DVec3(-4.78, 4.02, -4.18));
        AddBox(
            parts[CockpitVisualMaterial.Frame],
            new DVec3(4.78, 2.20, -5.02),
            new DVec3(5.03, 4.02, -4.18));

        foreach (double x in new[] { -2.45, 0.0, 2.45 })
        {
            AddBeam(
                parts[CockpitVisualMaterial.Frame],
                new DVec3(x, 2.18, -4.98),
                new DVec3(x, 4.03, -4.25),
                0.22);
        }
        AddBeam(
            parts[CockpitVisualMaterial.Frame],
            new DVec3(-4.90, 3.95, -4.28),
            new DVec3(-4.90, 2.58, -1.55),
            0.20);
        AddBeam(
            parts[CockpitVisualMaterial.Frame],
            new DVec3(4.90, 3.95, -4.28),
            new DVec3(4.90, 2.58, -1.55),
            0.20);

        AddBox(
            parts[CockpitVisualMaterial.CanopyLight],
            new DVec3(-5.45, 3.72, -3.75),
            new DVec3(-5.28, 4.08, -3.05));
        AddBox(
            parts[CockpitVisualMaterial.CanopyLight],
            new DVec3(5.28, 3.72, -3.75),
            new DVec3(5.45, 4.08, -3.05));
        AddBox(
            parts[CockpitVisualMaterial.InternalGlow],
            new DVec3(-2.8, 2.35, -4.77),
            new DVec3(2.8, 2.43, -4.58));

        CockpitVisualMeshPart[] meshParts = parts
            .Where(pair => pair.Value.Count > 0)
            .Select(pair => new CockpitVisualMeshPart(
                $"{GeometryId}.{pair.Key}",
                pair.Key,
                Array.AsReadOnly(pair.Value.ToArray())))
            .ToArray();
        return new CockpitVisualGeometry(GeometryId, meshParts);
    }

    private static void AddPrism(
        List<CockpitVisualTriangle> triangles,
        double minX,
        double maxX,
        IReadOnlyList<Vector2> yz)
    {
        DVec3[] left = yz.Select(point => new DVec3(minX, point.X, point.Y)).ToArray();
        DVec3[] right = yz.Select(point => new DVec3(maxX, point.X, point.Y)).ToArray();
        DVec3 center = left.Concat(right).Aggregate(DVec3.Zero, (sum, point) => sum + point)
            / (left.Length + right.Length);

        for (int i = 1; i < left.Length - 1; i++)
        {
            AddTriangle(triangles, left[0], left[i + 1], left[i], -DVec3.UnitX);
            AddTriangle(triangles, right[0], right[i], right[i + 1], DVec3.UnitX);
        }

        for (int i = 0; i < left.Length; i++)
        {
            int next = (i + 1) % left.Length;
            DVec3 faceCenter = (left[i] + left[next] + right[next] + right[i]) / 4.0;
            AddQuad(triangles, left[i], left[next], right[next], right[i], faceCenter - center);
        }
    }

    private static void AddBox(
        List<CockpitVisualTriangle> triangles,
        DVec3 min,
        DVec3 max)
    {
        DVec3[] points =
        [
            new(min.X, min.Y, min.Z),
            new(max.X, min.Y, min.Z),
            new(max.X, max.Y, min.Z),
            new(min.X, max.Y, min.Z),
            new(min.X, min.Y, max.Z),
            new(max.X, min.Y, max.Z),
            new(max.X, max.Y, max.Z),
            new(min.X, max.Y, max.Z),
        ];
        AddQuad(triangles, points[0], points[3], points[2], points[1], -DVec3.UnitZ);
        AddQuad(triangles, points[4], points[5], points[6], points[7], DVec3.UnitZ);
        AddQuad(triangles, points[0], points[4], points[7], points[3], -DVec3.UnitX);
        AddQuad(triangles, points[1], points[2], points[6], points[5], DVec3.UnitX);
        AddQuad(triangles, points[0], points[1], points[5], points[4], -DVec3.UnitY);
        AddQuad(triangles, points[3], points[7], points[6], points[2], DVec3.UnitY);
    }

    private static void AddBeam(
        List<CockpitVisualTriangle> triangles,
        DVec3 start,
        DVec3 end,
        double width)
    {
        Vector3 a = start.ToVector3();
        Vector3 b = end.ToVector3();
        Vector3 axis = Vector3.Normalize(b - a);
        Vector3 reference = Math.Abs(Vector3.Dot(axis, Vector3.UnitY)) < 0.9f
            ? Vector3.UnitY
            : Vector3.UnitX;
        Vector3 side = Vector3.Normalize(Vector3.Cross(reference, axis)) * (float)(width * 0.5);
        Vector3 up = Vector3.Normalize(Vector3.Cross(axis, side)) * (float)(width * 0.5);

        DVec3[] ringA =
        [
            ToDVec3(a - side - up),
            ToDVec3(a + side - up),
            ToDVec3(a + side + up),
            ToDVec3(a - side + up),
        ];
        DVec3[] ringB =
        [
            ToDVec3(b - side - up),
            ToDVec3(b + side - up),
            ToDVec3(b + side + up),
            ToDVec3(b - side + up),
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

    private static void AddTriangle(
        List<CockpitVisualTriangle> triangles,
        DVec3 a,
        DVec3 b,
        DVec3 c,
        DVec3 outward)
    {
        if (DVec3.Dot(DVec3.Cross(b - a, c - a), outward) < 0.0)
            (b, c) = (c, b);
        triangles.Add(new CockpitVisualTriangle(a, b, c));
    }

    private static void AddQuad(
        List<CockpitVisualTriangle> triangles,
        DVec3 a,
        DVec3 b,
        DVec3 c,
        DVec3 d,
        DVec3 outward)
    {
        AddTriangle(triangles, a, b, c, outward);
        AddTriangle(triangles, a, c, d, outward);
    }

    private static DVec3 ToDVec3(Vector3 value)
        => new(value.X, value.Y, value.Z);
}
