using Inferior.Game.Containers;
using Inferior.Rendering;
using Microsoft.Xna.Framework;

namespace Inferior.Game.StationGen;

public static partial class StationDecorator
{
    // ── Pass 5: Pipes & Conduits ──────────────────────────────────────────────

    private static readonly (int a, int b)[] BoxEdges =
    [
        (0,1),(1,2),(2,3),(3,0),
        (4,5),(5,6),(6,7),(7,4),
        (0,4),(1,5),(2,6),(3,7),
    ];

    // Per-edge: faceA normal, faceB normal, axis direction, corner signs (0 on axis dimension).
    private static readonly (Vector3 faceA, Vector3 faceB, Vector3 edgeDir, Vector3 cornerSign)[] BoxEdgeInfos =
    [
        // X-axis edges
        (-Vector3.UnitY, -Vector3.UnitZ,  Vector3.UnitX, new Vector3( 0, -1, -1)),
        ( Vector3.UnitY, -Vector3.UnitZ,  Vector3.UnitX, new Vector3( 0, +1, -1)),
        (-Vector3.UnitY,  Vector3.UnitZ,  Vector3.UnitX, new Vector3( 0, -1, +1)),
        ( Vector3.UnitY,  Vector3.UnitZ,  Vector3.UnitX, new Vector3( 0, +1, +1)),
        // Y-axis edges
        ( Vector3.UnitX, -Vector3.UnitZ,  Vector3.UnitY, new Vector3(+1,  0, -1)),
        (-Vector3.UnitX, -Vector3.UnitZ,  Vector3.UnitY, new Vector3(-1,  0, -1)),
        ( Vector3.UnitX,  Vector3.UnitZ,  Vector3.UnitY, new Vector3(+1,  0, +1)),
        (-Vector3.UnitX,  Vector3.UnitZ,  Vector3.UnitY, new Vector3(-1,  0, +1)),
        // Z-axis edges
        ( Vector3.UnitX, -Vector3.UnitY,  Vector3.UnitZ, new Vector3(+1, -1,  0)),
        (-Vector3.UnitX, -Vector3.UnitY,  Vector3.UnitZ, new Vector3(-1, -1,  0)),
        ( Vector3.UnitX,  Vector3.UnitY,  Vector3.UnitZ, new Vector3(+1, +1,  0)),
        (-Vector3.UnitX,  Vector3.UnitY,  Vector3.UnitZ, new Vector3(-1, +1,  0)),
    ];

    private static int PipeSides(System.Random rng) => rng.NextDouble() switch
    {
        < 0.40 => 4,
        < 0.75 => 6,
        _      => 8,
    };

    // Brief P1 Fix B: docking-bay previously wasn't in GeneratePipes' category filter at
    // all (bays never got edge pipes), so this tier never had a chance to run before now.
    // Sized chunkier than industrial's (up to 0.80) — a bay's edges run 100-238m, dwarfing
    // a hab-block's — with the same institutional grey/blue family as SurfacePipeColour's
    // new bay entries, for a consistent bay palette between edge and surface pipes.
    private static (float radius, int sides, Color colour) PipeSpec(string category, System.Random rng)
    {
        double roll = rng.NextDouble();
        return category switch
        {
            "industrial" or "fuel" => roll < 0.20
                ? (0.80f, 8, new Color(80,  80,  80))
                : roll < 0.55
                ? (0.45f, 6, new Color(95,  90,  85))
                : (0.22f, 6, new Color(120, 120, 120)),

            "core" => roll < 0.15
                ? (0.90f, 8, new Color(75,  75,  80))
                : roll < 0.50
                ? (0.35f, 6, new Color(100, 100, 110))
                : (0.18f, 4, new Color(125, 125, 130)),

            "cargo" => roll < 0.30
                ? (0.50f, 6, new Color(155, 100, 50))
                : (0.28f, 4, new Color(165, 110, 55)),

            "docking-bay" => roll < 0.20
                ? (1.20f, 8, new Color(90,  95,  100))
                : roll < 0.55
                ? (0.65f, 6, new Color(70,  110, 130))
                : (0.35f, 6, new Color(150, 150, 145)),

            _ => roll < 0.25
                ? (0.22f, 6, new Color(120, 120, 120))
                : (0.10f, 4, new Color(135, 135, 140)),
        };
    }

    private static void GeneratePipes(PlacedModule mod, FaceInfo[] faces,
        System.Random rng, StationModuleMesh mesh)
    {
        // Brief P1 Fix B: docking-bay added — D-Greeble found it excluded outright, so bays
        // never got edge pipes at all. mod.Definition.BoundingBox is the bay's real envelope
        // (set in StationModuleRegistry.CreateDockingBay), so the edge-corner math below
        // needs no bay-specific branch beyond this filter and PipeSpec's own bay tier.
        if (mod.Definition.Category is not ("industrial" or "cargo" or "connector" or "core" or "docking-bay"))
            return;

        Vector3 bb   = mod.Definition.BoundingBox;
        Vector3 half = bb * 0.5f;

        Span<Vector3> corners = stackalloc Vector3[8]
        {
            new(-half.X, -half.Y, -half.Z),
            new(+half.X, -half.Y, -half.Z),
            new(+half.X, +half.Y, -half.Z),
            new(-half.X, +half.Y, -half.Z),
            new(-half.X, -half.Y, +half.Z),
            new(+half.X, -half.Y, +half.Z),
            new(+half.X, +half.Y, +half.Z),
            new(-half.X, +half.Y, +half.Z),
        };

        int edgeCount = rng.Next(2, 5);
        Span<int> edgeOrder = stackalloc int[12];
        for (int i = 0; i < 12; i++) edgeOrder[i] = i;
        for (int i = 0; i < edgeCount; i++)
        {
            int j = rng.Next(i, 12);
            (edgeOrder[i], edgeOrder[j]) = (edgeOrder[j], edgeOrder[i]);
        }

        for (int ei = 0; ei < edgeCount; ei++)
        {
            var (radius, sides, pipeColor) = PipeSpec(mod.Definition.Category, rng);

            var (ai, bi) = BoxEdges[edgeOrder[ei]];
            Vector3 a = corners[ai];
            Vector3 b = corners[bi];

            Vector3 mid = (a + b) * 0.5f;
            Vector3 dir = b - a;
            float   len = dir.Length();
            if (len < 0.5f) continue;

            Vector3 outward = Vector3.Normalize(new Vector3(
                MathF.Abs(mid.X) > 0.1f ? MathF.Sign(mid.X) : 0f,
                MathF.Abs(mid.Y) > 0.1f ? MathF.Sign(mid.Y) : 0f,
                MathF.Abs(mid.Z) > 0.1f ? MathF.Sign(mid.Z) : 0f
            ));
            if (outward == Vector3.Zero) outward = Vector3.UnitY;

            Vector3 pipeDir = Vector3.Normalize(dir);
            Vector3 center  = mid + outward * (radius + 0.05f);
            mesh.AddPrismPipe(center - pipeDir * (len * 0.5f),
                              center + pipeDir * (len * 0.5f),
                              radius, sides, pipeColor);

            if (len > 6f)
            {
                int   brackets    = (int)(len / 4f);
                float bracketSize = radius * 3.6f;  // 1.8× diameter
                for (int k = 1; k <= brackets; k++)
                {
                    float   t          = (float)k / (brackets + 1);
                    Vector3 bracketPos = a + dir * t + outward * (radius + 0.02f);
                    mesh.AddOrientedBox(bracketPos, pipeDir,
                        radius * 1.2f, bracketSize, bracketSize,
                        DarkenColor(pipeColor, 0.8f));
                }
            }
        }
    }

    // ── Surface pipe runs ─────────────────────────────────────────────────────

    // Brief P1 Fix B: docking-bay previously fell to the flat (118,118,118) default in
    // every sample (D-Greeble measured this directly) — a real entry gives bay surface
    // pipes the same category-appropriate variety every other category already has.
    // Institutional greys/blues (fuel/coolant-line reads) rather than industrial's
    // rust/orange — a bay is a logistics structure, not a refinery.
    private static Color SurfacePipeColour(string category, System.Random rng) => category switch
    {
        "industrial" or "fuel" => rng.NextDouble() < 0.5
            ? new Color(160, 105, 50)
            : new Color(85,  85,  85),
        "science"      => new Color(100, 130, 160),
        "cargo"        => new Color(155, 100, 50),
        "docking-bay"  => rng.NextDouble() switch
        {
            < 0.40 => new Color(90,  95,  100),
            < 0.75 => new Color(70,  110, 130),
            _      => new Color(150, 150, 145),
        },
        _              => new Color(118, 118, 118),
    };

    private static void GenerateSurfacePipes(PlacedModule mod, FaceInfo face,
        System.Random rng, StationModuleMesh mesh)
    {
        if (!face.IsExposed) return;
        if (face.Width * face.Height < 40f) return;
        if (rng.NextDouble() > 0.45) return;

        int runCount = rng.Next(1, 4);

        for (int i = 0; i < runCount; i++)
        {
            bool    horizontal  = rng.NextDouble() < 0.5;
            Vector3 runDir      = horizontal ? face.LocalRight : face.LocalUp;
            Vector3 perpDir     = horizontal ? face.LocalUp    : face.LocalRight;
            float   runSpan     = horizontal ? face.Width  : face.Height;
            float   perpSpan    = horizontal ? face.Height : face.Width;

            float maxPerpOff = (perpSpan - 3f) * 0.5f;
            if (maxPerpOff <= 0f) continue;
            float perpOff = (float)(rng.NextDouble() - 0.5) * 2f * maxPerpOff;

            float runHalfLen = runSpan * 0.5f - 1.5f;
            if (runHalfLen <= 0.5f) continue;

            double sizeRoll = rng.NextDouble();
            float  radius   = sizeRoll < 0.35 ? 0.10f : sizeRoll < 0.70 ? 0.22f : 0.40f;
            int    sides    = PipeSides(rng);
            Color  colour   = SurfacePipeColour(mod.Definition.Category, rng);
            float  bracketH = radius + 0.35f + (float)rng.NextDouble() * 0.45f;

            Vector3 pipeCtr   = face.LocalCenter + perpDir * perpOff + face.LocalNormal * bracketH;
            Vector3 pipeStart = pipeCtr - runDir * runHalfLen;
            Vector3 pipeEnd   = pipeCtr + runDir * runHalfLen;

            // Both ends sit mid-face with margin — genuinely floating, not touching
            // anything (unlike GeneratePipes' full-edge-length runs, which continue
            // past the module edge).
            mesh.AddPrismPipe(pipeStart, pipeEnd, radius, sides, colour, capStart: true, capEnd: true);
            AddPipeBrackets(mesh, pipeStart, pipeEnd, runDir, perpDir,
                            face.LocalNormal, radius, bracketH, colour, rng);
        }
    }

    private static void AddPipeBrackets(StationModuleMesh mesh,
        Vector3 pipeStart, Vector3 pipeEnd,
        Vector3 runDir,    Vector3 perpDir, Vector3 faceNormal,
        float pipeRadius,  float bracketHeight, Color pipeColour,
        System.Random rng)
    {
        const float legThick = 0.055f;
        float   legHeight  = MathF.Max(0.1f, bracketHeight - pipeRadius);
        float   runLength  = Vector3.Distance(pipeStart, pipeEnd);
        float   spacing    = 3.5f + (float)rng.NextDouble() * 2f;
        int     count      = Math.Max(2, (int)(runLength / spacing));
        Color   col        = DarkenColor(pipeColour, 0.65f);

        for (int b = 0; b <= count; b++)
        {
            float   t         = (float)b / count;
            Vector3 pipePos   = Vector3.Lerp(pipeStart, pipeEnd, t);
            Vector3 basePoint = pipePos - faceNormal * bracketHeight;  // ~face surface

            // Left leg
            Vector3 lBase = basePoint - perpDir * pipeRadius;
            mesh.AddOrientedBox(lBase + faceNormal * (legHeight * 0.5f),
                faceNormal, legHeight, legThick, legThick, col);

            // Right leg
            Vector3 rBase = basePoint + perpDir * pipeRadius;
            mesh.AddOrientedBox(rBase + faceNormal * (legHeight * 0.5f),
                faceNormal, legHeight, legThick, legThick, col);

            // Crossbar connecting leg tops
            Vector3 crossCenter = basePoint + faceNormal * legHeight;
            mesh.AddOrientedBox(crossCenter, perpDir,
                pipeRadius * 2f + legThick, legThick, legThick, col);
        }
    }

}
