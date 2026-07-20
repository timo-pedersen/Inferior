using Inferior.Core.Math;
using Microsoft.Xna.Framework;

namespace Inferior.Gameplay.Cockpit;

public static class AriesCivilianCockpitGeometryFactory
{
    public const string GeometryId = "aries-civilian-canopy-cockpit.geometry.v1";

    public static CockpitVisualGeometry Create()
    {
        var parts = Enum.GetValues<CockpitVisualMaterial>()
            .ToDictionary(material => material, _ => new List<CockpitVisualTriangle>());

        // The C2 plug penetrates downward into the socket. The broader shoulder and
        // faceted canopy above it make the module read as replaceable machinery.
        AddBox(parts[CockpitVisualMaterial.MountingBase],
            new DVec3(-0.75, -0.95, -0.75),
            new DVec3(0.75, -0.18, 0.75));
        AddFrustum(parts[CockpitVisualMaterial.Housing],
            lowerY: -0.30,
            upperY: 0.02,
            lowerHalfX: 1.12,
            upperHalfX: 0.96,
            lowerFrontZ: -1.42,
            lowerRearZ: 1.36,
            upperFrontZ: -1.34,
            upperRearZ: 1.25,
            includeTop: false);

        DVec3[] canopyLower =
        [
            new(-0.94, -0.08, -1.34),
            new( 0.94, -0.08, -1.34),
            new( 0.94, -0.08,  1.18),
            new(-0.94, -0.08,  1.18),
        ];
        DVec3[] canopyUpper =
        [
            new(-0.62, 0.88, -0.88),
            new( 0.62, 0.88, -0.88),
            new( 0.62, 0.88,  0.82),
            new(-0.62, 0.88,  0.82),
        ];
        AddRingShell(parts[CockpitVisualMaterial.Canopy], canopyLower, canopyUpper, includeTop: true);

        // A dark volume behind the glass prevents the canopy reading as an empty shell.
        AddFrustum(parts[CockpitVisualMaterial.Interior],
            lowerY: -0.02,
            upperY: 0.70,
            lowerHalfX: 0.78,
            upperHalfX: 0.52,
            lowerFrontZ: -1.12,
            lowerRearZ: 1.00,
            upperFrontZ: -0.76,
            upperRearZ: 0.70,
            includeTop: true);

        const double frameWidth = 0.085;
        for (int i = 0; i < 4; i++)
        {
            AddBeam(parts[CockpitVisualMaterial.Frame],
                canopyLower[i], canopyLower[(i + 1) % 4], frameWidth);
            AddBeam(parts[CockpitVisualMaterial.Frame],
                canopyUpper[i], canopyUpper[(i + 1) % 4], frameWidth);
            AddBeam(parts[CockpitVisualMaterial.Frame],
                canopyLower[i], canopyUpper[i], frameWidth);
        }

        // A central roof spine and two lower side rails strengthen the civilian canopy.
        AddBeam(parts[CockpitVisualMaterial.Frame],
            new DVec3(0.0, 0.90, -0.90),
            new DVec3(0.0, 0.90, 0.84),
            0.07);
        AddBeam(parts[CockpitVisualMaterial.Frame],
            new DVec3(-0.97, -0.05, -1.30),
            new DVec3(-0.97, -0.05, 1.14),
            0.07);
        AddBeam(parts[CockpitVisualMaterial.Frame],
            new DVec3(0.97, -0.05, -1.30),
            new DVec3(0.97, -0.05, 1.14),
            0.07);

        AddBox(parts[CockpitVisualMaterial.CanopyLight],
            new DVec3(-0.91, 0.02, -1.43),
            new DVec3(-0.67, 0.16, -1.34));
        AddBox(parts[CockpitVisualMaterial.CanopyLight],
            new DVec3(0.67, 0.02, -1.43),
            new DVec3(0.91, 0.16, -1.34));

        // Small warm panels sit just behind the forward glass. They suggest powered
        // instruments without turning the whole canopy into an emissive block.
        AddBox(parts[CockpitVisualMaterial.InternalGlow],
            new DVec3(-0.42, 0.18, -1.292),
            new DVec3(-0.10, 0.40, -1.275));
        AddBox(parts[CockpitVisualMaterial.InternalGlow],
            new DVec3(0.10, 0.18, -1.292),
            new DVec3(0.42, 0.40, -1.275));

        CockpitVisualMeshPart[] meshParts = parts
            .Where(pair => pair.Value.Count > 0)
            .Select(pair => new CockpitVisualMeshPart(
                $"{GeometryId}.{pair.Key}",
                pair.Key,
                Array.AsReadOnly(pair.Value.ToArray())))
            .ToArray();
        return new CockpitVisualGeometry(GeometryId, meshParts);
    }

    private static void AddFrustum(
        List<CockpitVisualTriangle> triangles,
        double lowerY,
        double upperY,
        double lowerHalfX,
        double upperHalfX,
        double lowerFrontZ,
        double lowerRearZ,
        double upperFrontZ,
        double upperRearZ,
        bool includeTop)
    {
        DVec3[] lower =
        [
            new(-lowerHalfX, lowerY, lowerFrontZ),
            new( lowerHalfX, lowerY, lowerFrontZ),
            new( lowerHalfX, lowerY, lowerRearZ),
            new(-lowerHalfX, lowerY, lowerRearZ),
        ];
        DVec3[] upper =
        [
            new(-upperHalfX, upperY, upperFrontZ),
            new( upperHalfX, upperY, upperFrontZ),
            new( upperHalfX, upperY, upperRearZ),
            new(-upperHalfX, upperY, upperRearZ),
        ];
        AddRingShell(triangles, lower, upper, includeTop);
        AddQuad(triangles, lower[3], lower[2], lower[1], lower[0], -DVec3.UnitY);
    }

    private static void AddRingShell(
        List<CockpitVisualTriangle> triangles,
        IReadOnlyList<DVec3> lower,
        IReadOnlyList<DVec3> upper,
        bool includeTop)
    {
        DVec3 center = DVec3.Zero;
        foreach (DVec3 point in lower.Concat(upper))
            center += point;
        center /= lower.Count + upper.Count;

        for (int i = 0; i < 4; i++)
        {
            int next = (i + 1) % 4;
            DVec3 faceCenter = (lower[i] + lower[next] + upper[next] + upper[i]) / 4.0;
            AddQuad(triangles, lower[i], lower[next], upper[next], upper[i], faceCenter - center);
        }

        if (includeTop)
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
