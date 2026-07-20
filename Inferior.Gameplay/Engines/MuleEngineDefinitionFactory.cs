using Inferior.Core.Math;

namespace Inferior.Gameplay.Engines;

public static class MuleEngineDefinitionFactory
{
    public const string FamilyId = "mule";
    public const string H2VariantId = "mule.h2";

    public static EngineVariantDefinition CreateH2Variant()
        => new(H2VariantId, CreateDefinition(), EngineMountStandardIds.H2);

    public static EngineDefinition CreateDefinition()
    {
        var builders = new Dictionary<EngineVisualMaterial, List<EngineVisualTriangle>>();

        AddBox(builders, EngineVisualMaterial.Structural, new DVec3(0.0, 0.0, 0.0), new DVec3(1.60, 1.75, 4.70));
        AddBox(builders, EngineVisualMaterial.Casing, new DVec3(0.0, 0.05, -2.55), new DVec3(1.35, 1.50, 0.55));
        AddBox(builders, EngineVisualMaterial.Casing, new DVec3(0.0, 0.0, 2.45), new DVec3(1.75, 1.90, 0.85));
        AddBox(builders, EngineVisualMaterial.Nozzle, new DVec3(0.0, 0.0, 3.05), new DVec3(1.10, 1.20, 0.45));
        AddBox(builders, EngineVisualMaterial.Accent, new DVec3(0.91, 0.18, -0.25), new DVec3(0.22, 0.42, 2.90));
        AddBox(builders, EngineVisualMaterial.Structural, new DVec3(-0.32, -0.96, 0.55), new DVec3(0.62, 0.24, 1.35));

        var parts = builders
            .OrderBy(pair => pair.Key)
            .Select(pair => new EngineVisualMeshPart(
                $"mule.{pair.Key.ToString().ToLowerInvariant()}",
                pair.Key,
                Array.AsReadOnly(pair.Value.ToArray())))
            .ToArray();
        var geometry = new EngineVisualGeometry(
            "mule.geometry.01",
            new DVec3(-0.80, 0.0, 0.0),
            parts,
            [
                new EngineExhaustDefinition(
                    "mule.exhaust.main.01",
                    new DVec3(0.0, 0.0, 3.80),
                    DVec3.UnitZ,
                    RadiusMeters: 0.50),
            ],
            [
                new EngineLightDefinition(
                    "mule.light.service.01",
                    new DVec3(1.03, 0.18, -1.35),
                    DVec3.UnitX,
                    new DVec3(1.0, 0.72, 0.18),
                    GlowSizeMeters: 0.12,
                    Intensity: 1.0),
            ]);

        return new EngineDefinition(
            FamilyId,
            "Mule",
            new DVec3(2.10, 2.10, 6.55),
            dryMassKg: 2_400.0,
            forwardThrustN: 156_000.0,
            maneuveringThrustN: 78_000.0,
            rotationalTorqueNm: 250_000.0,
            geometry,
            new EngineDesignIntent(
                "cheap forgiving industrial utility engine",
                EngineIntentRating.Medium,
                EngineIntentRating.Medium,
                EngineIntentRating.Medium,
                EngineIntentRating.High,
                EngineIntentRating.Low,
                EngineIntentRating.High,
                EngineIntentRating.High,
                EngineIntentRating.Low,
                EngineIntentRating.Low,
                AlphaRedProduction: true),
            new EngineVisualDefinition(
                new DVec3(1.0, 0.24, 0.035),
                idleIntensity: 0.15f,
                thrustIntensity: 0.50f,
                velocityCorrectionIntensity: 0.80f,
                boostIntensity: 2.0f,
                instabilityAmount: 0.90f));
    }

    private static void AddBox(
        Dictionary<EngineVisualMaterial, List<EngineVisualTriangle>> builders,
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

        List<EngineVisualTriangle> triangles = GetBuilder(builders, material);
        AddQuad(triangles, v[0], v[3], v[2], v[1]);
        AddQuad(triangles, v[4], v[5], v[6], v[7]);
        AddQuad(triangles, v[0], v[4], v[7], v[3]);
        AddQuad(triangles, v[1], v[2], v[6], v[5]);
        AddQuad(triangles, v[0], v[1], v[5], v[4]);
        AddQuad(triangles, v[3], v[7], v[6], v[2]);
    }

    private static List<EngineVisualTriangle> GetBuilder(
        Dictionary<EngineVisualMaterial, List<EngineVisualTriangle>> builders,
        EngineVisualMaterial material)
    {
        if (!builders.TryGetValue(material, out var builder))
        {
            builder = [];
            builders.Add(material, builder);
        }
        return builder;
    }

    private static void AddQuad(
        List<EngineVisualTriangle> triangles,
        DVec3 a,
        DVec3 b,
        DVec3 c,
        DVec3 d)
    {
        triangles.Add(new EngineVisualTriangle(a, b, c));
        triangles.Add(new EngineVisualTriangle(a, c, d));
    }
}
