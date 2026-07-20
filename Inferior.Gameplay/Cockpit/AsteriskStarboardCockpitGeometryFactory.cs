using Inferior.Core.Math;
using Microsoft.Xna.Framework;

namespace Inferior.Gameplay.Cockpit;

public static class AsteriskStarboardCockpitGeometryFactory
{
    public const string GeometryId = "asterisk-starboard-cockpit.geometry.v1";

    public static CockpitVisualGeometry Create()
    {
        var parts = Enum.GetValues<CockpitVisualMaterial>()
            .ToDictionary(material => material, _ => new List<CockpitVisualTriangle>());

        AddBox(
            parts[CockpitVisualMaterial.MountingBase],
            new DVec3(-0.65, -0.82, -0.65),
            new DVec3(0.65, 0.08, 0.65));

        Vector2[] housingSection =
        [
            new(0.00f, -1.18f),
            new(0.40f, -1.08f),
            new(0.60f, -0.52f),
            new(0.56f,  0.70f),
            new(0.12f,  1.02f),
            new(-0.12f, 0.76f),
        ];
        AddPrism(
            parts[CockpitVisualMaterial.Housing],
            -0.72,
            0.72,
            housingSection);

        Vector2[] canopySection =
        [
            new(0.18f, -0.95f),
            new(0.56f, -0.62f),
            new(0.56f,  0.48f),
            new(0.30f,  0.62f),
        ];
        AddPrism(
            parts[CockpitVisualMaterial.Canopy],
            -0.56,
            0.56,
            canopySection);
        AddBox(
            parts[CockpitVisualMaterial.Interior],
            new DVec3(-0.46, 0.10, -0.78),
            new DVec3(0.46, 0.49, 0.48));

        DVec3[][] canopyRings =
        [
            canopySection.Select(point => new DVec3(-0.58, point.X, point.Y)).ToArray(),
            canopySection.Select(point => new DVec3(0.58, point.X, point.Y)).ToArray(),
        ];
        foreach (DVec3[] ring in canopyRings)
        {
            for (int i = 0; i < ring.Length; i++)
                AddBeam(parts[CockpitVisualMaterial.Frame], ring[i], ring[(i + 1) % ring.Length], 0.075);
        }
        for (int i = 0; i < canopySection.Length; i++)
        {
            AddBeam(
                parts[CockpitVisualMaterial.Frame],
                canopyRings[0][i],
                canopyRings[1][i],
                0.075);
        }

        AddBox(
            parts[CockpitVisualMaterial.CanopyLight],
            new DVec3(-0.52, 0.565, -0.78),
            new DVec3(-0.30, 0.64, -0.54));
        AddBox(
            parts[CockpitVisualMaterial.CanopyLight],
            new DVec3(0.30, 0.565, -0.78),
            new DVec3(0.52, 0.64, -0.54));
        AddBox(
            parts[CockpitVisualMaterial.InternalGlow],
            new DVec3(-0.30, 0.50, -0.58),
            new DVec3(0.30, 0.535, -0.28));

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
        if (DVec3.Dot(DVec3.Cross(b - a, c - a), outward) < 0.0)
            (b, d) = (d, b);
        triangles.Add(new CockpitVisualTriangle(a, b, c));
        triangles.Add(new CockpitVisualTriangle(a, c, d));
    }

    private static DVec3 ToDVec3(Vector3 value)
        => new(value.X, value.Y, value.Z);
}
