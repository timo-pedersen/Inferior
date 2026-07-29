using Inferior.Game.Containers;
using Inferior.Rendering;
using Microsoft.Xna.Framework;

namespace Inferior.Game.StationGen;

public static partial class StationDecorator
{
    // ── Pass 3: Antennas ─────────────────────────────────────────────────────

    private static float AntennaHeight(System.Random rng)
    {
        double tier = rng.NextDouble();
        if (tier < 0.50) return (float)(rng.NextDouble() * 3.5 + 1.0);   // 1–4.5 m
        if (tier < 0.85) return (float)(rng.NextDouble() * 6.0 + 4.5);   // 4.5–10.5 m
        return (float)(rng.NextDouble() * 5.0 + 10.5);                    // 10.5–15.5 m
    }

    // Brief Z2 Part 2: heavy (default false, unchanged behaviour) is CommsArray's "weight
    // the existing pass toward antennas/masts — heavily": skips the 35% placement gate
    // (near-certain instead of occasional) and rolls more instances per call. Nothing else
    // about antenna placement/appearance changes — same colours, same Yagi/spike mix.
    // Brief Z4 Fix 2: explicitCount (only ever set alongside heavy, from RunZonePasses'
    // CommsArray case) replaces the flat rng.Next(2,5) with an area-scaled count so a large
    // CommsArray zone reads as more than a fixed 2-4 masts. Non-CommsArray/non-heavy callers
    // are entirely unaffected — the parameter defaults to null and old behaviour is unchanged.
    private static void GenerateAntennas(PlacedModule mod, FaceInfo face,
        System.Random rng, StationModuleMesh mesh, List<StationLightInfo> lights,
        FaceOccupancy occupancy, List<PlacedGreebleInfo> placements, bool heavy = false,
        int? explicitCount = null)
    {
        if (!face.IsExposed)           return;
        if (face.LocalNormal.Y < -0.3f) return;
        if (!heavy && rng.NextDouble() > 0.35) return;

        Color baseCol    = StationModuleRegistry.CategoryColor(mod.Definition.Category);
        Color antennaCol = DarkenColor(baseCol, 0.45f);

        // Maritime white — roughly 1-in-4 antenna clusters are painted white like
        // the superstructure masts on an ocean-going vessel.
        bool faceIsWhite = rng.NextDouble() < 0.25;
        if (faceIsWhite) antennaCol = new Color(228, 232, 230);

        int count = explicitCount ?? (heavy ? rng.Next(2, 5) : rng.Next(1, 3));
        for (int i = 0; i < count; i++)
        {
            float u = (float)(rng.NextDouble() - 0.5) * face.Width  * 0.5f;
            float v = (float)(rng.NextDouble() - 0.5) * face.Height * 0.5f;

            Vector3 basePos = face.LocalCenter
                + face.LocalRight * u
                + face.LocalUp    * v;

            float plateSize;
            if (rng.NextDouble() < 0.30)
            {
                var yp = YagiAntennaParams.Generate(new System.Random(rng.Next()), faceIsWhite);
                StationYagiAntenna.Build(yp, basePos, face.LocalNormal, mesh);
                plateSize = 0.30f;
            }
            else
            {
                float length = AntennaHeight(rng);
                float radius = (float)(rng.NextDouble() * 0.12 + 0.04);

                mesh.AddSpike(basePos, face.LocalNormal, length, radius, antennaCol);

                if (length > 2.5f)
                {
                    Vector3 tipLocal = basePos + face.LocalNormal * length;
                    lights.Add(new StationLightInfo(
                        WorldPosition: Vector3.Transform(tipLocal, mod.Transform),
                        Colour:        new Color(220, 25, 25),
                        Type:          GlowType.AviationWarning,
                        BaseIntensity: 1.0f,
                        Rate:          0.65f,
                        Phase:         (float)rng.NextDouble(),
                        Pattern:       LightPattern.Strobe));
                }

                if (rng.NextDouble() < 0.4)
                {
                    const float stemLen = 1.2f;
                    const float stemW   = 0.18f;
                    Vector3 stemCenter = basePos + face.LocalNormal * (length + stemLen * 0.5f);
                    mesh.AddOrientedBox(stemCenter, face.LocalNormal, stemLen, stemW, stemW, antennaCol);

                    float   dishSize  = radius * 5f;
                    Vector3 tipCenter = basePos + face.LocalNormal * (length + stemLen + 0.2f);
                    mesh.AddBox(tipCenter, new Vector3(dishSize, dishSize * 0.3f, dishSize), antennaCol);
                }

                plateSize = MathF.Max(0.22f, radius * 3.5f);
            }

            // Base mount plate — makes the cable connection look intentional
            float   plateH = 0.09f;
            var     plateT = FaceLocalTransform(face, basePos + face.LocalNormal * (plateH * 0.5f));
            mesh.AddOrientedBox(plateT, new Vector3(plateSize * 2, plateSize * 2, plateH), new Color(50, 50, 55));
            occupancy.Occupy(u - plateSize, v - plateSize, u + plateSize, v + plateSize);
            placements.Add(new PlacedGreebleInfo(
                new Vector2(u, v),
                new Vector2(plateSize * 2, plateSize * 2),
                isConnectable: true));
        }
    }

    // Places one landmark antenna on the most sun-upward exposed science/core face.
    private static void PlaceLandmarkAntenna(IReadOnlyList<PlacedModule> modules, System.Random rng)
    {
        FaceInfo?     bestFace = null;
        PlacedModule? bestMod  = null;
        float         bestDot  = float.MinValue;

        foreach (var mod in modules)
        {
            if (mod.Definition.Category is not ("science" or "core")) continue;
            mod.Transform.Decompose(out _, out Quaternion rot, out _);
            foreach (var face in ComputeFaces(mod))
            {
                if (!face.IsExposed) continue;
                float dot = Vector3.Transform(face.LocalNormal, rot).Y;
                if (dot > bestDot) { bestDot = dot; bestFace = face; bestMod = mod; }
            }
        }

        if (bestMod == null || bestFace == null || bestDot < 0.5f) return;

        var f = bestFace.Value;
        bestMod.Mesh ??= new StationModuleMesh();
        bestMod.Mesh.CurrentDecorClass = DecorClass.Antennas;

        Color baseCol    = StationModuleRegistry.CategoryColor(bestMod.Definition.Category);
        Color antennaCol = DarkenColor(baseCol, 0.45f);

        float height = (float)(rng.NextDouble() * 9.0 + 18.0); // 18–27 m
        bestMod.Mesh.AddSpike(f.LocalCenter, f.LocalNormal, height, 0.15f, antennaCol);

        const float stemLen = 1.5f;
        Vector3 stemCenter = f.LocalCenter + f.LocalNormal * (height + stemLen * 0.5f);
        bestMod.Mesh.AddOrientedBox(stemCenter, f.LocalNormal, stemLen, 0.25f, 0.25f, antennaCol);
        Vector3 tipCenter = f.LocalCenter + f.LocalNormal * (height + stemLen + 0.3f);
        bestMod.Mesh.AddBox(tipCenter, new Vector3(2.0f, 0.6f, 2.0f), antennaCol);

        // Aviation warning light — landmark antennas are always tall enough to warrant it.
        Vector3 tipLocal = f.LocalCenter + f.LocalNormal * (height + stemLen + 0.6f);
        bestMod.GlowLights.Add(new StationLightInfo(
            WorldPosition: Vector3.Transform(tipLocal, bestMod.Transform),
            Colour:        new Color(220, 25, 25),
            Type:          GlowType.AviationWarning,
            BaseIntensity: 1.0f,
            Rate:          0.65f,
            Phase:         (float)rng.NextDouble(),
            Pattern:       LightPattern.Strobe));
    }

    // ── Pass 3b: Parabolic dishes ────────────────────────────────────────────

    private static readonly HashSet<string> DishCategories = ["science", "core", "military", "connector"];

    private static Color DishSurfaceColor(float radius) => radius > 3f
        ? new Color(252, 250, 244)   // large: near-white, faint warm tint
        : new Color(248, 246, 242);  // small/medium: near-white, slightly cooler

    private static readonly Color DishStructureColor = new Color(58, 55, 52);

    private static void GenerateDishes(PlacedModule mod, FaceInfo face,
        System.Random rng, StationModuleMesh mesh, FaceOccupancy occupancy,
        List<PlacedGreebleInfo> placements)
    {
        if (!face.IsExposed) return;
        if (!DishCategories.Contains(mod.Definition.Category)) return;

        mod.Transform.Decompose(out _, out Quaternion rot, out _);
        Vector3 worldNormal = Vector3.Normalize(Vector3.Transform(face.LocalNormal, rot));
        if (worldNormal.Y < -0.25f) return;

        float faceArea = face.Width * face.Height;

        // ── Medium dishes ───────────────────────────────────────────────────
        float medProb = mod.Definition.Category switch
        {
            "science"  => 0.55f,
            "military" => 0.30f,
            "core"     => 0.20f,
            _          => 0.10f,
        };
        if (faceArea > 60f && rng.NextDouble() < medProb)
        {
            float r      = 1.0f + (float)rng.NextDouble() * 1.8f;
            float maxOff = MathF.Max(0.01f, face.Width  * 0.5f - r * 1.5f);
            float maxOffV= MathF.Max(0.01f, face.Height * 0.5f - r * 1.5f);
            float cu = ((float)(rng.NextDouble() - 0.5)) * 2f * maxOff;
            float cv = ((float)(rng.NextDouble() - 0.5)) * 2f * maxOffV;
            if (occupancy.TryOccupy(cu, cv, r * 1.4f, r * 1.4f))
            {
                AddParabolicDish(mesh, LocalPointAbs(face, cu, cv, 0f), face.LocalNormal, r,
                    tiltDegrees:    15f + (float)rng.NextDouble() * 30f,
                    bearingDegrees: (float)rng.NextDouble() * 360f,
                    sides:          11,
                    dishColor:      DishSurfaceColor(r),
                    structureColor: DishStructureColor);
                var medPlateT = FaceLocalTransform(face, LocalPointAbs(face, cu, cv, 0.04f));
                mesh.AddOrientedBox(medPlateT, new Vector3(0.60f, 0.60f, 0.08f), new Color(50, 50, 55));
                placements.Add(new PlacedGreebleInfo(
                    new Vector2(cu, cv),
                    new Vector2(r * 2.8f, r * 2.8f),
                    isConnectable: true));
            }
        }

        // ── Small dishes ────────────────────────────────────────────────────
        int smallCount = mod.Definition.Category == "science" ? rng.Next(1, 4) : rng.Next(0, 2);
        for (int i = 0; i < smallCount; i++)
        {
            float r      = 0.35f + (float)rng.NextDouble() * 0.55f;
            float maxOff = MathF.Max(0.01f, face.Width  * 0.5f - r * 1.25f);
            float maxOffV= MathF.Max(0.01f, face.Height * 0.5f - r * 1.25f);
            float cu = ((float)(rng.NextDouble() - 0.5)) * 2f * maxOff;
            float cv = ((float)(rng.NextDouble() - 0.5)) * 2f * maxOffV;
            if (!occupancy.TryOccupy(cu, cv, r * 1.2f, r * 1.2f)) continue;
            AddParabolicDish(mesh, LocalPointAbs(face, cu, cv, 0f), face.LocalNormal, r,
                tiltDegrees:    10f + (float)rng.NextDouble() * 50f,
                bearingDegrees: (float)rng.NextDouble() * 360f,
                sides:          9,
                dishColor:      DishSurfaceColor(r),
                structureColor: DishStructureColor);
            var smPlateT = FaceLocalTransform(face, LocalPointAbs(face, cu, cv, 0.04f));
            mesh.AddOrientedBox(smPlateT, new Vector3(0.35f, 0.35f, 0.06f), new Color(50, 50, 55));
            placements.Add(new PlacedGreebleInfo(
                new Vector2(cu, cv),
                new Vector2(r * 2.4f, r * 2.4f),
                isConnectable: true));
        }
    }

    // Finds the largest eligible exposed face and places a single landmark large dish.
    private static void RunLargeDishPass(IReadOnlyList<PlacedModule> modules, System.Random rng)
    {
        bool qualified = modules.Any(m => m.Definition.Category is "science" or "military");
        if (!qualified || rng.NextDouble() >= 0.22) return;

        PlacedModule? bestMod  = null;
        FaceInfo?     bestFace = null;
        float         bestArea = 180f;

        foreach (var mod in modules)
        {
            if (!DishCategories.Contains(mod.Definition.Category)) continue;
            mod.Transform.Decompose(out _, out Quaternion rot, out _);
            foreach (var face in ComputeFaces(mod))
            {
                if (!face.IsExposed) continue;
                Vector3 worldN = Vector3.Normalize(Vector3.Transform(face.LocalNormal, rot));
                if (worldN.Y < -0.25f) continue;
                float area = face.Width * face.Height;
                if (area > bestArea) { bestArea = area; bestFace = face; bestMod = mod; }
            }
        }

        if (bestMod == null || bestFace == null) return;

        var   f = bestFace.Value;
        float r = 4.0f + (float)rng.NextDouble() * 4.0f;
        bestMod.Mesh ??= new StationModuleMesh();
        bestMod.Mesh.CurrentDecorClass = DecorClass.Dishes;
        AddParabolicDish(bestMod.Mesh, f.LocalCenter, f.LocalNormal, r,
            tiltDegrees:    8f + (float)rng.NextDouble() * 28f,
            bearingDegrees: (float)rng.NextDouble() * 360f,
            sides:          13,
            dishColor:      DishSurfaceColor(r),
            structureColor: DishStructureColor);
    }


    private static Vector3[] GenerateDishRing(Vector3 centre, Vector3 dishAxis,
        Vector3 right, Vector3 up, float radius, float depth, int sides)
    {
        var ring = new Vector3[sides];
        for (int i = 0; i < sides; i++)
        {
            float   angle  = i * MathF.Tau / sides;
            Vector3 radial = right * MathF.Cos(angle) + up * MathF.Sin(angle);
            ring[i] = centre + radial * radius + dishAxis * depth;
        }
        return ring;
    }

    private static void AddParabolicDish(StationModuleMesh mesh,
        Vector3 mountPoint, Vector3 faceNormal,
        float radius, float tiltDegrees, float bearingDegrees,
        int sides, Color dishColor, Color structureColor)
    {
        float maxDepth  = radius * 0.28f;
        float armLength = radius * 0.85f + 0.6f;
        const float shellThick = 0.015f;

        // Build tilted dish axis
        Vector3 arbitrary = MathF.Abs(faceNormal.Y) < 0.85f ? Vector3.UnitY : Vector3.UnitX;
        Vector3 tiltAxis  = Vector3.Normalize(Vector3.Cross(faceNormal, arbitrary));
        tiltAxis  = Vector3.Transform(tiltAxis,
            Quaternion.CreateFromAxisAngle(faceNormal, bearingDegrees * MathF.PI / 180f));
        Vector3 dishAxis = Vector3.Normalize(Vector3.Transform(faceNormal,
            Quaternion.CreateFromAxisAngle(tiltAxis, tiltDegrees * MathF.PI / 180f)));

        // dishBack: where the support arm ends (the back/deepest point of the dish)
        // dishCenter: the rim centre — maxDepth further out along dishAxis from dishBack
        // The bowl opens in the +dishAxis direction (toward the receiver / space)
        Vector3 dishBack   = mountPoint + faceNormal * armLength;
        Vector3 dishCenter = dishBack + dishAxis * maxDepth;

        Vector3 dishArb   = MathF.Abs(dishAxis.Y) < 0.85f ? Vector3.UnitY : Vector3.UnitX;
        Vector3 dishRight = Vector3.Normalize(Vector3.Cross(dishAxis, dishArb));
        Vector3 dishUp    = Vector3.Normalize(Vector3.Cross(dishRight, dishAxis));

        // Depths from dishCenter (rim plane) going BACKWARD (-dishAxis) into the bowl.
        float[] ringRadii  = [radius, radius * 0.62f, radius * 0.28f];
        float[] ringDepths = [0f, -(maxDepth * 0.38f), -(maxDepth * 0.86f)];
        var rings = new Vector3[3][];
        for (int ri = 0; ri < 3; ri++)
            rings[ri] = GenerateDishRing(dishCenter, dishAxis, dishRight, dishUp,
                                          ringRadii[ri], ringDepths[ri], sides);
        Vector3 dishCentreTip = dishBack;  // = dishCenter - dishAxis * maxDepth

        // Back shell rings: same positions shifted by shellThick in -dishAxis
        var backRings = new Vector3[3][];
        for (int ri = 0; ri < 3; ri++)
        {
            backRings[ri] = new Vector3[sides];
            for (int i = 0; i < sides; i++)
                backRings[ri][i] = rings[ri][i] - dishAxis * shellThick;
        }
        Vector3 backCentreTip = dishCentreTip - dishAxis * shellThick;

        Color rimColor = DarkenColor(dishColor, 0.68f);

        // Front surface: concave inner bowl, faces +dishAxis (toward receiver)
        for (int ri = 0; ri < 2; ri++)
            for (int i = 0; i < sides; i++)
            {
                int next = (i + 1) % sides;
                mesh.AddQuad(rings[ri][i], rings[ri][next],
                             rings[ri+1][next], rings[ri+1][i], dishColor);
            }
        for (int i = 0; i < sides; i++)
            mesh.AddTriangle(dishCentreTip, rings[2][i], rings[2][(i+1)%sides], dishColor);

        // Back surface: convex outer bowl, reversed winding → faces -dishAxis
        for (int ri = 0; ri < 2; ri++)
            for (int i = 0; i < sides; i++)
            {
                int next = (i + 1) % sides;
                mesh.AddQuad(backRings[ri][next], backRings[ri][i],
                             backRings[ri+1][i], backRings[ri+1][next], dishColor);
            }
        for (int i = 0; i < sides; i++)
            mesh.AddTriangle(backCentreTip, backRings[2][(i+1)%sides], backRings[2][i], dishColor);

        // Rim strip: connects front rim ring to back rim ring, faces radially outward
        for (int i = 0; i < sides; i++)
        {
            int next = (i + 1) % sides;
            mesh.AddQuad(rings[0][i], backRings[0][i], backRings[0][next], rings[0][next], rimColor);
        }

        // Support arm from mount to dish back
        mesh.AddPrismPipe(mountPoint, dishBack, 0.06f + radius * 0.018f, 4, structureColor);

        // Diagonal brace for medium and large dishes: low on arm → rim centre
        if (radius > 1.5f)
        {
            Vector3 braceRoot = mountPoint + faceNormal * armLength * 0.28f;
            mesh.AddPrismPipe(braceRoot, dishCenter, 0.045f, 4, structureColor);
        }

        AddDishFeedAssembly(mesh, rings[0], dishCentreTip, dishAxis, sides, structureColor);
    }

    // Feed mast, feed box, and struts from rim to focal point.
    private static void AddDishFeedAssembly(StationModuleMesh mesh,
        Vector3[] rimRing, Vector3 dishTip, Vector3 dishAxis, int sides,
        Color structureColor)
    {
        Vector3 rimCenter = Vector3.Zero;
        foreach (var v in rimRing) rimCenter += v;
        rimCenter /= sides;

        float   mastLength = Vector3.Distance(rimCenter, dishTip) * 0.55f;
        Vector3 feedTip    = rimCenter + dishAxis * mastLength;

        mesh.AddPrismPipe(dishTip, feedTip, 0.03f, 4, structureColor);
        mesh.AddOrientedBox(feedTip, dishAxis, 0.12f, 0.14f, 0.14f, structureColor);

        int strutCount = sides >= 11 ? 4 : 3;
        for (int s = 0; s < strutCount; s++)
        {
            int rimIdx = (int)((float)s / strutCount * sides);
            mesh.AddPrismPipe(rimRing[rimIdx], feedTip, 0.025f, 4, structureColor);
        }
    }

}
