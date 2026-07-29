using Inferior.Game.Containers;
using Inferior.Rendering;
using Microsoft.Xna.Framework;

namespace Inferior.Game.StationGen;

public static partial class StationDecorator
{
    // ── Pass 6e: Storage tanks ────────────────────────────────────────────────

    // Returns body colour, stripe colour, stripe count, and palette index (0-6).
    private static (Color body, Color stripe, int stripes, int idx) TankPalette(int seed)
    {
        var rng = new System.Random(seed);
        int idx = rng.Next(7);
        return idx switch
        {
            0 => (new Color(205, 48,  42),  new Color(220, 190, 28),  2, 0),  // red/yellow — fuel
            1 => (new Color(218, 188, 28),  new Color(20,  20,  20),  3, 1),  // yellow/black — hazard
            2 => (new Color(225, 223, 218), new Color(55,  95,  200), 1, 2),  // white/blue — LOX
            3 => (new Color(50,  95,  195), new Color(220, 220, 215), 2, 3),  // blue/white — coolant
            4 => (new Color(218, 118, 38),  new Color(220, 220, 215), 2, 4),  // orange — industrial
            5 => (new Color(65,  128, 68),  new Color(220, 220, 215), 1, 5),  // green — chemical
            6 => (new Color(198, 198, 192), new Color(75,  75,  75),  2, 6),  // silver — high-pressure
            _ => (new Color(198, 198, 192), new Color(75,  75,  75),  2, 6),
        };
    }

    private static string PickSubstanceName(int paletteIdx, System.Random rng)
    {
        string[] names = paletteIdx switch
        {
            0 => ["JP-5", "RP-1", "CH4", "LH2"],
            1 => ["N204", "MMH", "HAZ"],
            2 => ["LOX", "LO2", "OX"],
            3 => ["H2O", "CLT", "COOL"],
            4 => ["PROC", "FEED", "REAC"],
            5 => ["N2H4", "HYD", "CHEM"],
            6 => ["N2", "HPA", "GAS"],
            _ => ["TANK"],
        };
        return names[rng.Next(names.Length)];
    }

    // Returns start and end cross-section rings for an N-sided prism.
    // Mirrors AddPrismPipe's ring generation so caps align perfectly with the body.
    private static (Vector3[] start, Vector3[] end) GetPrismRings(
        Vector3 start, Vector3 end, float radius, int sides)
    {
        Vector3 dir       = Vector3.Normalize(end - start);
        Vector3 arbitrary = MathF.Abs(dir.Y) < 0.85f ? Vector3.UnitY : Vector3.UnitX;
        Vector3 right     = Vector3.Normalize(Vector3.Cross(dir, arbitrary));
        Vector3 up        = Vector3.Normalize(Vector3.Cross(right, dir));

        var startRing = new Vector3[sides];
        var endRing   = new Vector3[sides];
        for (int i = 0; i < sides; i++)
        {
            float   a      = i * MathF.Tau / sides;
            Vector3 offset = right * MathF.Cos(a) * radius + up * MathF.Sin(a) * radius;
            startRing[i] = start + offset;
            endRing[i]   = end   + offset;
        }
        return (startRing, endRing);
    }

    // Truncated-pyramid cap for one end of a tank body.
    // Both caps: pass the appropriate ring + flipLaterals=true.
    //   Start cap: reversed startRing so the tip triangles wind outward toward -axis;
    //              flipLaterals=true because reversing the ring gives it the same
    //              winding orientation (CW from outside) as endRing, so the lateral
    //              quads need the same flip to produce outward-facing normals.
    //   End cap:   non-reversed endRing + flipLaterals=true.
    // The tip face winding is the same for both ends.
    private static void AddTankCap(StationModuleMesh mesh,
        Vector3[] bodyRing, Vector3 outDir, float tipRadius, float capDepth, Color color,
        bool flipLaterals = false)
    {
        int     N          = bodyRing.Length;
        Vector3 bodyCenter = Vector3.Zero;
        foreach (var v in bodyRing) bodyCenter += v;
        bodyCenter /= N;

        Vector3 tipCenter = bodyCenter + outDir * capDepth;

        var tipRing = new Vector3[N];
        for (int i = 0; i < N; i++)
        {
            Vector3 outward = Vector3.Normalize(bodyRing[i] - bodyCenter);
            tipRing[i] = tipCenter + outward * tipRadius;
        }

        for (int i = 0; i < N; i++)
        {
            int next = (i + 1) % N;
            if (flipLaterals)
                mesh.AddQuad(bodyRing[next], bodyRing[i], tipRing[i], tipRing[next], color);
            else
                mesh.AddQuad(bodyRing[i], bodyRing[next], tipRing[next], tipRing[i], color);
        }

        for (int i = 0; i < N; i++)
            mesh.AddTriangle(tipCenter, tipRing[(i + 1) % N], tipRing[i], color);
    }

    // Pixel-art text rendered as tiny raised quads on a planar surface.
    private static void AddTextGeometry(StationModuleMesh mesh,
        string text, Vector3 origin, Vector3 textRight, Vector3 textUp, Vector3 textNormal,
        float pixelSize, Color textColor)
    {
        float cx = 0f;
        foreach (char ch in text.ToUpperInvariant())
        {
            if (!BitmapFonts.HasGlyph(ch)) { cx += (BitmapFonts.CharW + 1) * pixelSize; continue; }

            for (int row = 0; row < BitmapFonts.CharH; row++)
            for (int col = 0; col < BitmapFonts.CharW; col++)
            {
                if (!BitmapFonts.IsLit(ch, col, row)) continue;
                float px = cx + (col + 0.5f) * pixelSize;
                float py = (BitmapFonts.CharH - row - 0.5f) * pixelSize;  // row 0 = top in font → flip Y
                mesh.AddQuad(origin + textRight * px + textUp * py,
                             textNormal, textUp, pixelSize * 0.88f, pixelSize * 0.88f, textColor);
            }
            cx += (BitmapFonts.CharW + 1) * pixelSize;
        }
    }

    // Metal plate label (substance name + 2-digit ID) on the outward face of a tank.
    private static void AddTankLabel(StationModuleMesh mesh,
        Vector3 tankMidPoint, Vector3 labelNormal, Vector3 labelUp,
        float tankRadius, string substance, int tankId,
        Color textColor, Color plateColor)
    {
        if (tankRadius < 0.45f) return;

        Vector3 labelRight = Vector3.Normalize(Vector3.Cross(labelUp, labelNormal));

        // Fit substance name to one octagon face width (~0.765 × radius)
        float faceW     = 2f * tankRadius * MathF.Sin(MathF.PI / 8f);
        float pixelSize = Math.Clamp(
            faceW * 0.68f / MathF.Max(1, substance.Length * (BitmapFonts.CharW + 1)),
            0.028f, 0.18f);

        float  nameW = substance.Length      * (BitmapFonts.CharW + 1) * pixelSize;
        float  lineH = BitmapFonts.CharH    * pixelSize;
        float  idSz  = pixelSize * 0.68f;
        string idStr = $"{tankId:D2}";
        float  idW   = idStr.Length * (BitmapFonts.CharW + 1) * idSz;

        float plateW = MathF.Max(nameW, idW) + pixelSize * 3f;
        float plateH = lineH + BitmapFonts.CharH * idSz + pixelSize * 4f;

        float   plateOff = tankRadius + 0.012f;
        Vector3 plateCtr = tankMidPoint + labelNormal * plateOff;
        mesh.AddQuad(plateCtr, labelNormal, labelUp, plateW, plateH, plateColor);

        // Top border strip
        mesh.AddQuad(plateCtr + labelUp * (plateH * 0.5f + pixelSize * 0.2f),
                     labelNormal, labelUp, plateW * 1.07f, pixelSize * 0.6f,
                     DarkenColor(plateColor, 0.60f));

        // Substance name (upper line)
        const float raise = 0.014f;
        Vector3 nameOrig = plateCtr + labelNormal * raise
                         + labelRight * (-nameW * 0.5f)
                         + labelUp    * (pixelSize * 1.5f);
        AddTextGeometry(mesh, substance, nameOrig, labelRight, labelUp, labelNormal, pixelSize, textColor);

        // 2-digit ID (lower line, smaller)
        Vector3 idOrig = plateCtr + labelNormal * raise
                       + labelRight * (-idW * 0.5f)
                       - labelUp    * (lineH + pixelSize * 0.8f);
        AddTextGeometry(mesh, idStr, idOrig, labelRight, labelUp, labelNormal,
                        idSz, DarkenColor(textColor, 0.70f));
    }

    // Junction boxes, valve wheels, and pipe stubs on the tank barrel.
    private static void AddTankGreebles(StationModuleMesh mesh,
        Vector3 tankStart, Vector3 tankEnd, float radius,
        Vector3 outNormal, Color baseColor, Color pipeColor, System.Random rng)
    {
        Vector3 axis    = Vector3.Normalize(tankEnd - tankStart);
        Vector3 csRight = Vector3.Normalize(Vector3.Cross(axis,
                              MathF.Abs(Vector3.Dot(axis, outNormal)) < 0.85f
                              ? outNormal : Vector3.UnitY));
        Vector3 csUp    = Vector3.Normalize(Vector3.Cross(csRight, axis));

        float outAngle = MathF.Atan2(Vector3.Dot(csUp, outNormal),
                                      Vector3.Dot(csRight, outNormal));
        Color boxCol  = DarkenColor(baseColor, 0.56f);
        Color darkCol = DarkenColor(baseColor, 0.37f);

        int count = rng.Next(2, 5);
        for (int i = 0; i < count; i++)
        {
            float   t      = 0.1f + (float)rng.NextDouble() * 0.8f;
            float   angle  = outAngle + ((float)rng.NextDouble() - 0.5f) * MathF.PI * 0.75f;
            float   boxSz  = radius * (0.10f + (float)rng.NextDouble() * 0.12f);
            Vector3 ctrPt  = Vector3.Lerp(tankStart, tankEnd, t);
            Vector3 surfPt = ctrPt + csRight * MathF.Cos(angle) * radius
                                   + csUp    * MathF.Sin(angle) * radius;
            Vector3 outDir = Vector3.Normalize(surfPt - ctrPt);

            switch (rng.Next(3))
            {
                case 0: // Junction box + indicator LED
                    mesh.AddOrientedBox(surfPt + outDir * (boxSz * 0.5f),
                        outDir, boxSz * 0.5f, boxSz * 1.4f, boxSz, boxCol);
                    mesh.AddOrientedBox(surfPt + outDir * (boxSz + 0.01f),
                        outDir, 0.02f, boxSz * 0.22f, boxSz * 0.22f, new Color(20, 200, 40));
                    break;

                case 1: // Pipe stub with elbow cap
                    Vector3 stubEnd = surfPt + outDir * (radius * 0.32f);
                    mesh.AddPrismPipe(surfPt, stubEnd, boxSz * 0.32f, 6, pipeColor);
                    mesh.AddOrientedBox(stubEnd, outDir,
                        boxSz * 0.22f, boxSz * 0.65f, boxSz * 0.65f, darkCol);
                    break;

                case 2: // Valve wheel (stem + two crossed spokes)
                    Vector3 stemEnd = surfPt + outDir * (boxSz * 0.85f);
                    mesh.AddOrientedBox((surfPt + stemEnd) * 0.5f, outDir,
                        boxSz * 0.85f, boxSz * 0.14f, boxSz * 0.14f, darkCol);
                    mesh.AddOrientedBox(stemEnd, csRight,
                        boxSz * 1.35f, boxSz * 0.10f, boxSz * 0.10f, darkCol);
                    mesh.AddOrientedBox(stemEnd, csUp,
                        boxSz * 1.35f, boxSz * 0.10f, boxSz * 0.10f, darkCol);
                    break;
            }
        }
    }

    // Radius picker biased toward small/medium with rare large tanks. Brief Z2 Part 2:
    // cap threaded down from GenerateTanks for CommsArray's "a small greeble tank or 5" —
    // same distribution, just clamped, so a CommsArray tank never rolls into the
    // medium/large/rare tiers regardless of which one the roll landed in.
    private static float PickTankRadius(System.Random rng, float? cap = null)
    {
        float r = rng.NextDouble() switch
        {
            < 0.55 => 0.30f + (float)rng.NextDouble() * 0.55f,   // 0.30–0.85 m: common small
            < 0.80 => 0.85f + (float)rng.NextDouble() * 0.85f,   // 0.85–1.70 m: medium
            < 0.93 => 1.70f + (float)rng.NextDouble() * 0.80f,   // 1.70–2.50 m: large
            _      => 2.50f + (float)rng.NextDouble() * 2.50f,   // 2.50–5.00 m: rare
        };
        return cap.HasValue ? MathF.Min(r, cap.Value) : r;
    }

    // Brief Z4 Fix 3: dedicated "large tank" radius range for TankFarm's explicit size mix —
    // distinct from PickTankRadius's own natural "rare" 2.50-5.00m tier (left completely
    // untouched, still reachable by ordinary/Machinery/CommsArray tanks exactly as before).
    // TankFarm explicitly ROLLS a fraction of its clusters into this range rather than
    // relying on the natural tier's low (7%) probability. Centred on Timo's own "~5m
    // diameter" starting size (2.0-3.2m radius = 4.0-6.4m diameter).
    private static float PickLargeTankRadius(System.Random rng) => 2.0f + (float)rng.NextDouble() * 1.2f;

    // Brief Z4 Fix 3: tessellation tracks world size. A 5m tank with the same 8-sided
    // cross-section as a 1m tank reads as a puffed-up small tank — silhouette smoothness is
    // itself a size cue. Linear in radius, clamped to a sane range: small/medium tanks
    // (0.3-0.85m) land at 7-9 sides, close to today's flat 8 (a deliberate near-match, not a
    // coincidence — this mapping is only ever used where Z4 opts in, never on the ordinary
    // per-face path, so nothing here needs to reproduce the old constant exactly); the new
    // large tier (2.0-3.2m) reads at 14-19 sides; PickTankRadius's own rare ceiling (5.0m)
    // caps at 24.
    private static int TankSidesForRadius(float radius)
        => Math.Clamp((int)MathF.Round(6f + radius * 4f), 6, 24);

    // Brief Z4 Fix 3: sides is now a parameter (was a hardcoded local const) so tessellation
    // can track world size — callers not opting into that (every pre-Z4 call site) pass the
    // same literal 8 the constant used to be, so their geometry is unchanged.
    private static void AddTank(StationModuleMesh mesh,
        Vector3 start, Vector3 end, float bodyRadius,
        Color bodyColor, Color stripeColor, int stripeCount,
        Color pipeColor, Vector3 attachPoint,
        Vector3 labelNormal, Vector3 labelUp,
        string substanceName, int tankId,
        System.Random rng, int sides = 8)
    {
        mesh.AddPrismPipe(start, end, bodyRadius, sides, bodyColor);

        var (startRing, endRing) = GetPrismRings(start, end, bodyRadius, sides);
        float   tipRadius = bodyRadius * 0.28f;
        float   capDepth  = bodyRadius * 0.50f;
        Vector3 axis      = Vector3.Normalize(end - start);

        // Reverse startRing so the tip triangles wind outward for the start cap.
        // Both caps use flipLaterals=true: after reversal, startRingRev is CW from
        // outside (like endRing from its outside), so the same lateral flip applies.
        var startRingRev = new Vector3[sides];
        for (int i = 0; i < sides; i++) startRingRev[i] = startRing[sides - 1 - i];

        AddTankCap(mesh, startRingRev, -axis, tipRadius, capDepth, bodyColor, flipLaterals: true);
        AddTankCap(mesh, endRing,       axis, tipRadius, capDepth, bodyColor, flipLaterals: true);

        float stripeW = MathF.Max(bodyRadius * 0.08f, 0.04f);
        for (int s = 1; s <= stripeCount; s++)
        {
            float   t   = (float)s / (stripeCount + 1);
            Vector3 ctr = Vector3.Lerp(start, end, t);
            mesh.AddPrismPipe(ctr - axis * stripeW, ctr + axis * stripeW,
                              bodyRadius * 1.04f, sides, stripeColor);
        }

        // Connecting pipe from nearest cap tip to module surface, in stripe colour
        bool    useStart = Vector3.Distance(start, attachPoint) < Vector3.Distance(end, attachPoint);
        Vector3 capTip   = useStart ? start - axis * capDepth : end + axis * capDepth;
        mesh.AddPrismPipe(capTip, attachPoint, bodyRadius * 0.18f, 6, stripeColor);

        // Label plate + pixel text
        AddTankLabel(mesh, (start + end) * 0.5f, labelNormal, labelUp,
                     bodyRadius, substanceName, tankId, stripeColor,
                     DarkenColor(bodyColor, 0.62f));

        // Surface greebles
        AddTankGreebles(mesh, start, end, bodyRadius, labelNormal, bodyColor, stripeColor, rng);
    }

    // Brief Z4 Fix 3: scaledTessellation/preferLarge (both default false, unchanged
    // behaviour) and a bool return (whether anything was actually placed) — see
    // GenerateTanks' and GenerateTankFarmContent's own comments for how these are used.
    private static bool PlaceTankRow(PlacedModule mod, FaceInfo face,
        StationModuleMesh mesh, FaceOccupancy occupancy, System.Random rng,
        string substance, Color bodyColor, Color stripeColor, int stripes, float? sizeCap,
        bool scaledTessellation = false, bool preferLarge = false)
    {
        int   maxCount = mod.Definition.Category is "fuel" or "industrial" or "military" ? 6 : 4;
        int   count    = 2 + rng.Next(maxCount - 1);
        float radius   = preferLarge ? PickLargeTankRadius(rng) : PickTankRadius(rng, sizeCap);
        int   sides    = scaledTessellation ? TankSidesForRadius(radius) : 8;
        float length   = radius * 2.0f + (float)rng.NextDouble() * radius * 2.5f;
        float gap      = radius * 0.14f;
        float step     = radius * 2 + gap;

        // Clamp count so the row fits the face width
        int maxFit = Math.Max(2, (int)((face.Width * 0.88f + gap) / step));
        count = Math.Min(count, maxFit);
        float totalU = count * step - gap;
        if (totalU > face.Width) return false;

        float vOff = -face.Height * 0.5f + radius + 0.8f;
        if (!occupancy.TryOccupy(0, vOff, totalU * 0.5f + 0.3f, radius + 0.4f)) return false;

        Color pipeColor  = DarkenColor(stripeColor, 0.75f);
        Color strutColor = new Color(80, 75, 70);
        float startU     = -totalU * 0.5f + radius;

        var cuArr = new float[count];
        for (int i = 0; i < count; i++)
        {
            float   cu        = startU + i * step;
            cuArr[i]          = cu;
            Vector3 centre    = LocalPointAbs(face, cu, vOff, radius * 0.5f);
            Vector3 tankStart = centre - face.LocalRight * (length * 0.5f);
            Vector3 tankEnd   = centre + face.LocalRight * (length * 0.5f);
            AddTank(mesh, tankStart, tankEnd, radius,
                    bodyColor, stripeColor, stripes, pipeColor,
                    LocalPointAbs(face, cu, vOff, 0),
                    face.LocalNormal, face.LocalUp,
                    substance, i + 1, rng, sides);
        }

        // Banding straps across the whole row (top and bottom)
        float cu0 = cuArr[0], cu1 = cuArr[count - 1];
        foreach (float vStrap in new[] { vOff + radius * 0.62f, vOff - radius * 0.62f })
        {
            mesh.AddPrismPipe(
                LocalPointAbs(face, cu0 - radius, vStrap, radius + 0.04f),
                LocalPointAbs(face, cu1 + radius, vStrap, radius + 0.04f),
                radius * 0.042f, 4, strutColor);
        }

        // Diagonal cross-braces between adjacent tanks for larger clusters
        if (count >= 3 && radius >= 0.55f)
        {
            for (int i = 0; i < count - 1; i++)
            {
                Vector3 topA = LocalPointAbs(face, cuArr[i],     vOff + radius * 0.58f, radius + 0.05f);
                Vector3 botB = LocalPointAbs(face, cuArr[i + 1], vOff - radius * 0.58f, radius + 0.05f);
                mesh.AddPrismPipe(topA, botB, radius * 0.032f, 4, strutColor);
            }
        }
        return true;
    }

    private static bool PlaceSingleTank(PlacedModule mod, FaceInfo face,
        StationModuleMesh mesh, FaceOccupancy occupancy, System.Random rng,
        string substance, Color bodyColor, Color stripeColor, int stripes, float? sizeCap,
        bool scaledTessellation = false, bool preferLarge = false)
    {
        float radius = preferLarge ? PickLargeTankRadius(rng) : MathF.Max(0.80f, PickTankRadius(rng, sizeCap));
        int   sides  = scaledTessellation ? TankSidesForRadius(radius) : 8;
        float length = radius * 1.8f + (float)rng.NextDouble() * radius * 3f;
        float cu     = ((float)rng.NextDouble() - 0.5f) * MathF.Max(0.1f, face.Width  - radius * 2.5f);
        float cv     = ((float)rng.NextDouble() - 0.5f) * MathF.Max(0.1f, face.Height - radius * 2.5f);

        if (!occupancy.TryOccupy(cu, cv, radius * 1.3f, radius * 1.3f)) return false;

        Color pipeColor = DarkenColor(stripeColor, 0.75f);
        AddTank(mesh,
                LocalPointAbs(face, cu, cv, 0.2f),
                LocalPointAbs(face, cu, cv, length + 0.2f),
                radius, bodyColor, stripeColor, stripes, pipeColor,
                LocalPointAbs(face, cu, cv, 0),
                face.LocalRight, face.LocalUp,            // label on the side of the silo
                substance, 1, rng, sides);
        return true;
    }

    private static bool PlaceTankPair(PlacedModule mod, FaceInfo face,
        StationModuleMesh mesh, FaceOccupancy occupancy, System.Random rng,
        string substance, Color bodyColor, Color stripeColor, int stripes, float? sizeCap,
        bool scaledTessellation = false, bool preferLarge = false)
    {
        float radius  = preferLarge ? PickLargeTankRadius(rng) : PickTankRadius(rng, sizeCap);
        int   sides   = scaledTessellation ? TankSidesForRadius(radius) : 8;
        float length  = radius * 2.5f + (float)rng.NextDouble() * radius * 2.5f;
        float spacing = radius * 2.4f;

        if (spacing + radius > face.Width * 0.5f) return false;
        if (!occupancy.TryOccupy(0, 0, spacing * 0.5f + radius + 0.3f, length * 0.5f + 0.4f)) return false;

        Color pipeColor = DarkenColor(stripeColor, 0.75f);

        for (int side = -1; side <= 1; side += 2)
        {
            float   cu     = side * spacing * 0.5f;
            Vector3 centre = LocalPointAbs(face, cu, 0, radius * 0.5f);
            AddTank(mesh,
                    centre - face.LocalUp * (length * 0.5f),
                    centre + face.LocalUp * (length * 0.5f),
                    radius, bodyColor, stripeColor, stripes, pipeColor,
                    LocalPointAbs(face, cu, 0, 0),
                    face.LocalNormal, face.LocalUp,
                    substance, side < 0 ? 1 : 2, rng, sides);
        }

        // Cross-pipe in stripe colour between the two tanks
        mesh.AddPrismPipe(
            LocalPointAbs(face, -spacing * 0.5f, 0, radius),
            LocalPointAbs(face, +spacing * 0.5f, 0, radius),
            radius * 0.14f, 6, stripeColor);
        return true;
    }

    // Brief Z4 Fix 3: scaledTessellation/preferLarge threaded through to whichever
    // arrangement gets rolled; returns whether the cluster actually placed anything (the
    // occupancy check inside Row/Single/Pair can silently reject a cluster) — consumed by
    // GenerateTankFarmContent's requested-vs-produced tracking.
    private static bool PlaceTankCluster(PlacedModule mod, FaceInfo face,
        StationModuleMesh mesh, FaceOccupancy occupancy, System.Random rng, int clusterIdx, float? sizeCap,
        bool scaledTessellation = false, bool preferLarge = false)
    {
        int paletteSeed = mod.Seed ^ (0xAB12 + clusterIdx * 0x2F1B);
        var (bodyColor, stripeColor, stripes, paletteIdx) = TankPalette(paletteSeed);
        string substance = PickSubstanceName(paletteIdx, new System.Random(paletteSeed ^ 0x5C3A));

        double typeRoll = rng.NextDouble();
        if (face.Width * face.Height > 100f && typeRoll < 0.28)
            return PlaceSingleTank(mod, face, mesh, occupancy, rng, substance, bodyColor, stripeColor, stripes, sizeCap, scaledTessellation, preferLarge);
        else if (typeRoll < 0.65)
            return PlaceTankRow   (mod, face, mesh, occupancy, rng, substance, bodyColor, stripeColor, stripes, sizeCap, scaledTessellation, preferLarge);
        else
            return PlaceTankPair  (mod, face, mesh, occupancy, rng, substance, bodyColor, stripeColor, stripes, sizeCap, scaledTessellation, preferLarge);
    }

    // Brief Z2 Part 2: sizeCap (default null = unlimited, unchanged behaviour) lets
    // CommsArray zones request "a small greeble tank or 5" — the SAME cluster-type/count/
    // probability logic, just with PickTankRadius clamped down the whole way through.
    // Brief Z3 Fix B: guaranteed (default false, unchanged behaviour) mirrors GenerateWindows'
    // F1 Fix 4 bypass — a zone allocated as TankFarm/Machinery is a commitment, not a
    // suggestion, so it skips only the FIRST-cluster gate (does this zone get any tanks at
    // all). The extra-cluster decay below stays untouched either way — that's density (how
    // MANY clusters), explicitly deferred to the next brief, not "any at all."
    private static void GenerateTanks(PlacedModule mod, FaceInfo face,
        StationModuleMesh mesh, FaceOccupancy occupancy, System.Random rng,
        float? sizeCap = null, bool guaranteed = false)
    {
        if (!face.IsExposed) return;
        if (face.Width * face.Height < 12f) return;

        if (!guaranteed)
        {
            float firstProb = mod.Definition.Category switch
            {
                "fuel"     or "military"  => 0.97f,
                "industrial"              => 0.90f,
                "cargo"    or "core"      => 0.75f,
                "science"  or "connector" => 0.45f,
                "hab"                     => 0.28f,
                _                         => 0.20f,
            };
            if (rng.NextDouble() > firstProb) return;
        }

        int maxClusters = mod.Definition.Category switch
        {
            "fuel"     or "military"  => 3,
            "industrial"              => 3,
            "cargo"    or "core"      => 2,
            _                         => 1,
        };

        // Brief Z4 Fix 3: scaledTessellation follows guaranteed — zone-committed tank
        // content (Machinery/TankFarm) is also properly tessellated for whatever radius it
        // happens to roll; the ordinary per-face path and CommsArray's unguaranteed tank
        // call (guaranteed stays false there) keep the old flat 8-sided look, unchanged.
        PlaceTankCluster(mod, face, mesh, occupancy, rng, 0, sizeCap, scaledTessellation: guaranteed);

        float nextProb = 0.55f;
        for (int extra = 1; extra < maxClusters; extra++)
        {
            if (rng.NextDouble() > nextProb) break;
            nextProb *= 0.65f;
            PlaceTankCluster(mod, face, mesh, occupancy, rng, extra, sizeCap, scaledTessellation: guaranteed);
        }
    }

    // Brief Z4 Fix 2+3: TankFarm's dedicated composition — differs from Machinery's generic
    // GenerateTanks call in KIND, not just amount (Timo: "TankFarm and Machinery differ in
    // kind, not only amount"). Cluster count scales with zone area (ZoneContentDensity.
    // TankFarmClustersPerSqm); each cluster independently rolls "large" (~5m diameter,
    // properly tessellated) vs "small" (today's normal PickTankRadius range) — the SIZE MIX
    // is what reads as a farm, not cluster count alone. A light, fixed-odds pass of
    // supporting vents/greebles reads as "a few small pipes, cables, and boxes" without
    // competing hard with tanks for occupancy the way a second guaranteed pass would.
    // Tracks requested/produced-by-size on the module for the D-Z2-style dump (Brief Z4
    // verification: "report requested-vs-produced... broken down by tank size").
    private static void GenerateTankFarmContent(PlacedModule mod, FaceInfo zone,
        StationModuleMesh mesh, FaceOccupancy occupancy, System.Random rng)
    {
        if (!zone.IsExposed) return;
        if (zone.Width * zone.Height < 12f) return;

        float area = zone.Width * zone.Height;
        int targetClusters = Math.Clamp(
            (int)MathF.Round(area * ZoneContentDensity.TankFarmClustersPerSqm),
            1, ZoneContentDensity.TankFarmMaxClusters);

        for (int i = 0; i < targetClusters; i++)
        {
            bool large = rng.NextDouble() < ZoneContentDensity.TankFarmLargeClusterFraction;
            bool placed = PlaceTankCluster(mod, zone, mesh, occupancy, rng, i, sizeCap: null,
                scaledTessellation: true, preferLarge: large);

            if (large) { mod.TankFarmLargeRequested++; if (placed) mod.TankFarmLargeProduced++; }
            else       { mod.TankFarmSmallRequested++; if (placed) mod.TankFarmSmallProduced++; }
        }

        // Supporting hardware — deliberately NOT guaranteed: "a few" small pipes/cables/
        // boxes, not a second dense pass competing with tanks for the same occupancy.
        GenerateVentGrilles(mod, zone, rng, mesh, occupancy);
        GenerateGreebles(mod, zone, rng, mesh, occupancy, []);
    }

    // Build a transform matrix with Z aligned to face.LocalNormal, positioned at `center`.
    private static Matrix FaceLocalTransform(FaceInfo face, Vector3 center) => new(
        face.LocalRight.X,  face.LocalRight.Y,  face.LocalRight.Z,  0,
        face.LocalUp.X,     face.LocalUp.Y,     face.LocalUp.Z,     0,
        face.LocalNormal.X, face.LocalNormal.Y, face.LocalNormal.Z, 0,
        center.X,           center.Y,           center.Z,           1);

}
