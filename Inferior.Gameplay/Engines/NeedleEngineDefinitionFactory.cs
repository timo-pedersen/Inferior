using Inferior.Core.Math;

namespace Inferior.Gameplay.Engines;

public static class NeedleEngineDefinitionFactory
{
    public const string FamilyId = "needle";
    public const string H2VariantId = "needle.h2";

    private const double BodyCenterX = -0.04;

    public static EngineVariantDefinition CreateH2Variant()
        => new(H2VariantId, CreateDefinition(), EngineMountStandardIds.H2);

    public static EngineDefinition CreateDefinition()
    {
        var regions = new Dictionary<string, RegionBuilder>(StringComparer.Ordinal);
        Ring[] rings =
        [
            CreateRing(-3.10, 0.22, 0.30),
            CreateRing(-2.75, 0.34, 0.42),
            CreateRing(-2.15, 0.48, 0.60),
            CreateRing(-1.20, 0.59, 0.68),
            CreateRing( 1.35, 0.59, 0.68),
            CreateRing( 2.15, 0.57, 0.57),
            CreateRing( 2.75, 0.55, 0.46),
            CreateRing( 2.92, 0.55, 0.42),
        ];

        AddCap(regions, "needle.body.forward-cap", EngineVisualMaterial.Structural, rings[0], -DVec3.UnitZ);
        AddLoft(regions, "needle.body.forward-fairing", EngineVisualMaterial.Casing, rings[0], rings[1], rings[2]);
        AddLoft(regions, "needle.body.main-shell", EngineVisualMaterial.Casing, rings[2], rings[3], rings[4]);
        AddLoft(regions, "needle.body.rear-collar", EngineVisualMaterial.Structural, rings[4], rings[5], rings[6], rings[7]);
        AddCap(regions, "needle.body.rear-collar", EngineVisualMaterial.Structural, rings[7], DVec3.UnitZ);

        AddBox(
            regions,
            "needle.mount.adapter-collar",
            EngineVisualMaterial.Structural,
            new DVec3(-0.69, 0.0, 0.0),
            new DVec3(0.22, 0.72, 0.62));
        AddBox(
            regions,
            "needle.mount.transition-fairing",
            EngineVisualMaterial.Structural,
            new DVec3(-0.53, 0.0, 0.0),
            new DVec3(0.18, 0.60, 0.90));
        AddBox(
            regions,
            "needle.body.lower-keel",
            EngineVisualMaterial.Structural,
            new DVec3(-0.05, -0.72, 0.15),
            new DVec3(0.28, 0.11, 3.70));

        AddBox(
            regions,
            "needle.service.spine",
            EngineVisualMaterial.Accent,
            new DVec3(0.48, 0.62, -0.15),
            new DVec3(0.12, 0.22, 3.50));
        AddBox(
            regions,
            "needle.service.access-strip",
            EngineVisualMaterial.Structural,
            new DVec3(0.545, 0.54, -0.15),
            new DVec3(0.01, 0.08, 2.90));
        double[] accessPortZ = [-1.10, -0.15, 0.80];
        for (int i = 0; i < accessPortZ.Length; i++)
        {
            AddBox(
                regions,
                $"needle.detail.service-port.{i + 1:00}",
                EngineVisualMaterial.Nozzle,
                new DVec3(0.548, 0.64, accessPortZ[i]),
                new DVec3(0.004, 0.11, 0.34));
        }

        AddBox(
            regions,
            "needle.exhaust.slot.frame.top",
            EngineVisualMaterial.Structural,
            new DVec3(BodyCenterX, 0.31, 3.01),
            new DVec3(1.10, 0.14, 0.18));
        AddBox(
            regions,
            "needle.exhaust.slot.frame.bottom",
            EngineVisualMaterial.Structural,
            new DVec3(BodyCenterX, -0.07, 3.01),
            new DVec3(1.10, 0.14, 0.18));
        AddBox(
            regions,
            "needle.exhaust.slot.frame.port",
            EngineVisualMaterial.Structural,
            new DVec3(-0.575, 0.12, 3.01),
            new DVec3(0.07, 0.24, 0.18));
        AddBox(
            regions,
            "needle.exhaust.slot.frame.starboard",
            EngineVisualMaterial.Structural,
            new DVec3(0.495, 0.12, 3.01),
            new DVec3(0.07, 0.24, 0.18));
        AddQuad(
            GetRegion(regions, "needle.exhaust.slot", EngineVisualMaterial.Nozzle).Triangles,
            new DVec3(-0.54, 0.00, 2.94),
            new DVec3(0.46, 0.00, 2.94),
            new DVec3(0.46, 0.24, 2.94),
            new DVec3(-0.54, 0.24, 2.94),
            DVec3.UnitZ);

        AddQuad(
            GetRegion(regions, "needle.light.forward", EngineVisualMaterial.LightWhite).Triangles,
            new DVec3(-0.01, 0.08, -3.10),
            new DVec3(0.31, 0.08, -3.10),
            new DVec3(0.31, 0.16, -3.10),
            new DVec3(-0.01, 0.16, -3.10),
            -DVec3.UnitZ);
        AddQuad(
            GetRegion(regions, "needle.light.rear", EngineVisualMaterial.LightRed).Triangles,
            new DVec3(0.01, 0.45, 3.10),
            new DVec3(0.29, 0.45, 3.10),
            new DVec3(0.29, 0.53, 3.10),
            new DVec3(0.01, 0.53, 3.10),
            DVec3.UnitZ);

        var geometry = new EngineVisualGeometry(
            "needle.geometry.01",
            new DVec3(-0.80, 0.0, 0.0),
            regions.Values
                .OrderBy(region => region.PartId, StringComparer.Ordinal)
                .Select(region => new EngineVisualMeshPart(
                    region.PartId,
                    region.Material,
                    Array.AsReadOnly(region.Triangles.ToArray())))
                .ToArray(),
            [
                new EngineExhaustDefinition(
                    "needle.exhaust.slot.01",
                    new DVec3(BodyCenterX, 0.12, 3.10),
                    DVec3.UnitZ,
                    RadiusMeters: 0.62),
            ],
            [
                new EngineLightDefinition(
                    "needle.light.forward.01",
                    new DVec3(0.15, 0.12, -3.10),
                    -DVec3.UnitZ,
                    new DVec3(0.82, 0.94, 1.0),
                    GlowSizeMeters: 0.10,
                    Intensity: 0.85),
                new EngineLightDefinition(
                    "needle.light.rear.01",
                    new DVec3(0.15, 0.49, 3.10),
                    DVec3.UnitZ,
                    new DVec3(1.0, 0.10, 0.06),
                    GlowSizeMeters: 0.11,
                    Intensity: 0.9),
            ]);

        return new EngineDefinition(
            FamilyId,
            "Needle",
            new DVec3(1.35, 1.55, 6.20),
            dryMassKg: 1_650.0,
            geometry,
            new EngineDesignIntent(
                "premium high-performance civilian engine",
                EngineIntentRating.High,
                EngineIntentRating.High,
                EngineIntentRating.High,
                EngineIntentRating.Low,
                EngineIntentRating.High,
                EngineIntentRating.High,
                EngineIntentRating.Low,
                EngineIntentRating.High,
                EngineIntentRating.High,
                AlphaRedProduction: false),
            new EngineVisualDefinition(
                new DVec3(0.48, 0.82, 1.0),
                idleIntensity: 0.10f,
                thrustIntensity: 0.70f,
                brakeIntensity: 0.90f,
                boostIntensity: 3.0f,
                flickerAmount: 0.12f));
    }

    private static Ring CreateRing(double z, double radiusX, double radiusY)
    {
        var points = new DVec3[8];
        for (int i = 0; i < points.Length; i++)
        {
            double angle = Math.PI / 2.0 - i * Math.PI / 4.0;
            points[i] = new DVec3(
                BodyCenterX + Math.Cos(angle) * radiusX,
                Math.Sin(angle) * radiusY,
                z);
        }
        return new Ring(points);
    }

    private static void AddLoft(
        Dictionary<string, RegionBuilder> regions,
        string partId,
        EngineVisualMaterial material,
        params Ring[] rings)
    {
        List<EngineVisualTriangle> triangles = GetRegion(regions, partId, material).Triangles;
        for (int ringIndex = 0; ringIndex < rings.Length - 1; ringIndex++)
        {
            Ring front = rings[ringIndex];
            Ring rear = rings[ringIndex + 1];
            for (int i = 0; i < front.Points.Length; i++)
            {
                int next = (i + 1) % front.Points.Length;
                DVec3 desiredNormal = new(
                    (front.Points[i].X + front.Points[next].X) / 2.0 - BodyCenterX,
                    (front.Points[i].Y + front.Points[next].Y) / 2.0,
                    0.0);
                AddQuad(
                    triangles,
                    front.Points[i],
                    rear.Points[i],
                    rear.Points[next],
                    front.Points[next],
                    desiredNormal);
            }
        }
    }

    private static void AddCap(
        Dictionary<string, RegionBuilder> regions,
        string partId,
        EngineVisualMaterial material,
        Ring ring,
        DVec3 outwardNormal)
    {
        List<EngineVisualTriangle> triangles = GetRegion(regions, partId, material).Triangles;
        DVec3 centre = ring.Points.Aggregate(DVec3.Zero, (sum, point) => sum + point) / ring.Points.Length;
        for (int i = 0; i < ring.Points.Length; i++)
        {
            int next = (i + 1) % ring.Points.Length;
            AddTriangle(triangles, centre, ring.Points[i], ring.Points[next], outwardNormal);
        }
    }

    private static void AddBox(
        Dictionary<string, RegionBuilder> regions,
        string partId,
        EngineVisualMaterial material,
        DVec3 centre,
        DVec3 size)
    {
        DVec3 h = size * 0.5;
        DVec3[] v =
        [
            centre + new DVec3(-h.X, -h.Y, -h.Z),
            centre + new DVec3( h.X, -h.Y, -h.Z),
            centre + new DVec3( h.X,  h.Y, -h.Z),
            centre + new DVec3(-h.X,  h.Y, -h.Z),
            centre + new DVec3(-h.X, -h.Y,  h.Z),
            centre + new DVec3( h.X, -h.Y,  h.Z),
            centre + new DVec3( h.X,  h.Y,  h.Z),
            centre + new DVec3(-h.X,  h.Y,  h.Z),
        ];
        List<EngineVisualTriangle> triangles = GetRegion(regions, partId, material).Triangles;
        AddQuad(triangles, v[0], v[3], v[2], v[1], -DVec3.UnitZ);
        AddQuad(triangles, v[4], v[5], v[6], v[7], DVec3.UnitZ);
        AddQuad(triangles, v[0], v[4], v[7], v[3], -DVec3.UnitX);
        AddQuad(triangles, v[1], v[2], v[6], v[5], DVec3.UnitX);
        AddQuad(triangles, v[0], v[1], v[5], v[4], -DVec3.UnitY);
        AddQuad(triangles, v[3], v[7], v[6], v[2], DVec3.UnitY);
    }

    private static void AddQuad(
        List<EngineVisualTriangle> triangles,
        DVec3 a,
        DVec3 b,
        DVec3 c,
        DVec3 d,
        DVec3 outwardNormal)
    {
        AddTriangle(triangles, a, b, c, outwardNormal);
        AddTriangle(triangles, a, c, d, outwardNormal);
    }

    private static void AddTriangle(
        List<EngineVisualTriangle> triangles,
        DVec3 a,
        DVec3 b,
        DVec3 c,
        DVec3 outwardNormal)
    {
        if (DVec3.Dot(DVec3.Cross(b - a, c - a), outwardNormal) < 0.0)
            (b, c) = (c, b);
        triangles.Add(new EngineVisualTriangle(a, b, c));
    }

    private static RegionBuilder GetRegion(
        Dictionary<string, RegionBuilder> regions,
        string partId,
        EngineVisualMaterial material)
    {
        if (regions.TryGetValue(partId, out RegionBuilder? region))
        {
            if (region.Material != material)
                throw new InvalidOperationException($"Needle region '{partId}' uses conflicting materials.");
            return region;
        }

        region = new RegionBuilder(partId, material);
        regions.Add(partId, region);
        return region;
    }

    private sealed record Ring(DVec3[] Points);

    private sealed class RegionBuilder(string partId, EngineVisualMaterial material)
    {
        public string PartId { get; } = partId;
        public EngineVisualMaterial Material { get; } = material;
        public List<EngineVisualTriangle> Triangles { get; } = [];
    }
}
