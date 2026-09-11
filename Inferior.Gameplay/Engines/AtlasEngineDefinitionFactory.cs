using Inferior.Core.Math;

namespace Inferior.Gameplay.Engines;

public static class AtlasEngineDefinitionFactory
{
    public const string FamilyId = "atlas-civilian-drive";
    public const string H10VariantId = "atlas-civilian-drive.h10";

    private const double HalfWidth = 2.50;
    private const double HalfHeight = 2.40;

    public static EngineVariantDefinition CreateH10Variant()
        => new(H10VariantId, CreateDefinition(), EngineMountStandardIds.H10);

    public static EngineDefinition CreateDefinition()
    {
        var regions = new Dictionary<string, RegionBuilder>(StringComparer.Ordinal);
        Ring[] rings =
        [
            CreateRing(-29.00, 1.70, 1.55),
            CreateRing(-26.00, 2.30, 2.05),
            CreateRing(-21.50, HalfWidth, HalfHeight),
            CreateRing( 18.50, HalfWidth, HalfHeight),
            CreateRing( 23.50, 2.38, 2.28),
            CreateRing( 27.00, 2.18, 2.02),
            CreateRing( 28.25, 2.08, 1.92),
        ];

        AddCap(
            regions,
            "atlas.mount.forward-cap",
            EngineVisualMaterial.Structural,
            rings[0],
            -DVec3.UnitZ);
        AddLoft(
            regions,
            "atlas.mount.forward-section",
            EngineVisualMaterial.Structural,
            rings[0],
            rings[1],
            rings[2]);
        AddLoft(
            regions,
            "atlas.body.main-casing",
            EngineVisualMaterial.Casing,
            rings[2],
            rings[3]);
        AddLoft(
            regions,
            "atlas.body.aft-drive-section",
            EngineVisualMaterial.Structural,
            rings[3],
            rings[4],
            rings[5],
            rings[6]);
        AddCap(
            regions,
            "atlas.exhaust.rear-plate",
            EngineVisualMaterial.Structural,
            rings[6],
            DVec3.UnitZ);

        foreach ((string id, double z) in new[]
                 {
                     ("forward", -18.25),
                     ("mid", 0.0),
                     ("aft", 17.25),
                 })
        {
            AddBox(
                regions,
                $"atlas.segment.{id}.top",
                EngineVisualMaterial.Structural,
                new DVec3(0.0, 2.46, z),
                new DVec3(5.20, 0.34, 0.90));
            AddBox(
                regions,
                $"atlas.segment.{id}.bottom",
                EngineVisualMaterial.Structural,
                new DVec3(0.0, -2.46, z),
                new DVec3(5.20, 0.34, 0.90));
            AddBox(
                regions,
                $"atlas.segment.{id}.port",
                EngineVisualMaterial.Structural,
                new DVec3(-2.45, 0.0, z),
                new DVec3(0.30, 4.65, 0.90));
            AddBox(
                regions,
                $"atlas.segment.{id}.starboard",
                EngineVisualMaterial.Structural,
                new DVec3(2.45, 0.0, z),
                new DVec3(0.30, 4.65, 0.90));
        }

        AddBox(
            regions,
            "atlas.service.dorsal-spine",
            EngineVisualMaterial.Accent,
            new DVec3(0.0, 2.55, -1.0),
            new DVec3(1.30, 0.38, 34.0));
        AddBox(
            regions,
            "atlas.service.vent-bank.port",
            EngineVisualMaterial.Nozzle,
            new DVec3(-2.56, 0.55, 7.0),
            new DVec3(0.18, 1.55, 12.0));
        AddBox(
            regions,
            "atlas.service.vent-bank.starboard",
            EngineVisualMaterial.Nozzle,
            new DVec3(2.56, 0.55, 7.0),
            new DVec3(0.18, 1.55, 12.0));

        AddRingFrame(regions, "atlas.exhaust.frame", 28.38);
        AddOctagonalDisc(
            regions,
            "atlas.exhaust.aperture",
            EngineVisualMaterial.Nozzle,
            28.40,
            1.72,
            1.55,
            DVec3.UnitZ);

        AddBox(
            regions,
            "atlas.light.forward",
            EngineVisualMaterial.LightWhite,
            new DVec3(0.0, 1.10, -29.08),
            new DVec3(1.30, 0.30, 0.12));
        AddBox(
            regions,
            "atlas.light.rear",
            EngineVisualMaterial.LightRed,
            new DVec3(0.0, 1.65, 28.43),
            new DVec3(1.50, 0.28, 0.10));

        var geometry = new EngineVisualGeometry(
            "atlas-civilian-drive.geometry.01",
            new DVec3(-HalfWidth, 0.0, 0.0),
            regions.Values
                .OrderBy(region => region.PartId, StringComparer.Ordinal)
                .Select(region => new EngineVisualMeshPart(
                    region.PartId,
                    region.Material,
                    Array.AsReadOnly(region.Triangles.ToArray())))
                .ToArray(),
            [
                new EngineExhaustDefinition(
                    "atlas.exhaust.main.01",
                    new DVec3(0.0, 0.0, 29.20),
                    DVec3.UnitZ,
                    RadiusMeters: 1.90),
            ],
            [
                new EngineLightDefinition(
                    "atlas.light.forward.01",
                    new DVec3(0.0, 1.10, -29.14),
                    -DVec3.UnitZ,
                    new DVec3(0.88, 0.95, 1.0),
                    GlowSizeMeters: 0.32,
                    Intensity: 0.90),
                new EngineLightDefinition(
                    "atlas.light.rear.01",
                    new DVec3(0.0, 1.65, 28.50),
                    DVec3.UnitZ,
                    new DVec3(1.0, 0.12, 0.05),
                    GlowSizeMeters: 0.36,
                    Intensity: 1.0),
            ]);

        return new EngineDefinition(
            FamilyId,
            "Atlas Civilian Drive",
            new DVec3(5.45, 5.25, 58.40),
            dryMassKg: 96_000.0,
            maximumForwardThrustN: 17_926_000.0,
            reverseThrustFraction: 1.0,
            lateralThrustFraction: 0.25,
            liftThrustFraction: 0.50,
            rotationalTorqueNm: 90_000_000.0,
            harmonyCount: 10,
            minimumThrustFraction: 0.10,
            minimumSpeedCeilingMps: 50.0,
            maximumSpeedCeilingMps: 25_600.0,
            geometry,
            new EngineDesignIntent(
                "largest normal civilian sustained heavy-hauler drive",
                EngineIntentRating.High,
                EngineIntentRating.Low,
                EngineIntentRating.Medium,
                EngineIntentRating.High,
                EngineIntentRating.High,
                EngineIntentRating.High,
                EngineIntentRating.Medium,
                EngineIntentRating.Low,
                EngineIntentRating.Medium,
                AlphaRedProduction: true),
            new EngineVisualDefinition(
                new DVec3(1.0, 0.30, 0.055),
                idleIntensity: 0.18f,
                thrustIntensity: 0.62f,
                velocityCorrectionIntensity: 0.82f,
                boostIntensity: 2.1f,
                instabilityAmount: 0.35f));
    }

    private static Ring CreateRing(double z, double radiusX, double radiusY)
    {
        var points = new DVec3[8];
        for (int i = 0; i < points.Length; i++)
        {
            double angle = Math.PI / 8.0 + i * Math.PI / 4.0;
            points[i] = new DVec3(
                Math.Cos(angle) * radiusX,
                Math.Sin(angle) * radiusY,
                z);
        }
        return new Ring(points);
    }

    private static void AddRingFrame(
        Dictionary<string, RegionBuilder> regions,
        string prefix,
        double z)
    {
        Ring outer = CreateRing(z, 2.18, 2.02);
        Ring inner = CreateRing(z + 0.03, 1.80, 1.63);
        List<EngineVisualTriangle> triangles =
            GetRegion(regions, prefix, EngineVisualMaterial.Structural).Triangles;
        for (int i = 0; i < outer.Points.Length; i++)
        {
            int next = (i + 1) % outer.Points.Length;
            AddQuad(
                triangles,
                outer.Points[i],
                outer.Points[next],
                inner.Points[next],
                inner.Points[i],
                DVec3.UnitZ);
        }
    }

    private static void AddOctagonalDisc(
        Dictionary<string, RegionBuilder> regions,
        string partId,
        EngineVisualMaterial material,
        double z,
        double radiusX,
        double radiusY,
        DVec3 normal)
    {
        Ring ring = CreateRing(z, radiusX, radiusY);
        AddCap(regions, partId, material, ring, normal);
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
                DVec3 midpoint = (front.Points[i] + front.Points[next]) * 0.5;
                DVec3 desiredNormal = new(midpoint.X, midpoint.Y, 0.0);
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
        DVec3 centre = ring.Points.Aggregate(DVec3.Zero, (sum, point) => sum + point)
            / ring.Points.Length;
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
        DVec3 half = size * 0.5;
        DVec3 min = centre - half;
        DVec3 max = centre + half;
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
        List<EngineVisualTriangle> triangles = GetRegion(regions, partId, material).Triangles;
        AddQuad(triangles, points[0], points[3], points[2], points[1], -DVec3.UnitZ);
        AddQuad(triangles, points[4], points[5], points[6], points[7], DVec3.UnitZ);
        AddQuad(triangles, points[0], points[4], points[7], points[3], -DVec3.UnitX);
        AddQuad(triangles, points[1], points[2], points[6], points[5], DVec3.UnitX);
        AddQuad(triangles, points[0], points[1], points[5], points[4], -DVec3.UnitY);
        AddQuad(triangles, points[3], points[7], points[6], points[2], DVec3.UnitY);
    }

    private static void AddQuad(
        List<EngineVisualTriangle> triangles,
        DVec3 a,
        DVec3 b,
        DVec3 c,
        DVec3 d,
        DVec3 outward)
    {
        AddTriangle(triangles, a, b, c, outward);
        AddTriangle(triangles, a, c, d, outward);
    }

    private static void AddTriangle(
        List<EngineVisualTriangle> triangles,
        DVec3 a,
        DVec3 b,
        DVec3 c,
        DVec3 outward)
    {
        if (DVec3.Dot(DVec3.Cross(b - a, c - a), outward) < 0.0)
            (b, c) = (c, b);
        triangles.Add(new EngineVisualTriangle(a, b, c));
    }

    private static RegionBuilder GetRegion(
        Dictionary<string, RegionBuilder> regions,
        string partId,
        EngineVisualMaterial material)
    {
        if (regions.TryGetValue(partId, out RegionBuilder? region))
            return region;

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
