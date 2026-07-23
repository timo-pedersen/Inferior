using Inferior.Game.Containers;
using Inferior.Rendering;
using Microsoft.Xna.Framework;

namespace Inferior.Game.StationGen;

public static partial class StationDecorator
{
    // ── Pass 2: Hatches ───────────────────────────────────────────────────────

    private static void GenerateHatches(PlacedModule mod, FaceInfo face,
        System.Random rng, StationModuleMesh mesh, FaceOccupancy occupancy)
    {
        if (!face.IsExposed)  return;
        if (face.Width < 2f)  return;
        if (face.Height < 2f) return;
        if (MathF.Abs(face.LocalNormal.Y) > 0.5f) return;

        Color baseCol  = StationModuleRegistry.CategoryColor(mod.Definition.Category);
        Color hatchCol = DarkenColor(baseCol, 0.65f);

        int count = rng.Next(1, 4);
        for (int i = 0; i < count; i++)
        {
            float u  = (float)(rng.NextDouble() - 0.5) * (face.Width  - 1.5f);
            float v  = (float)(rng.NextDouble() - 0.5) * (face.Height - 1.5f);
            float hw = (float)(rng.NextDouble() * 0.3f + 0.4f);
            float hh = (float)(rng.NextDouble() * 0.5f + 0.5f);

            if (!occupancy.TryOccupy(u, v, hw, hh)) continue;

            Vector3 center = face.LocalCenter
                + face.LocalRight  * u
                + face.LocalUp     * v
                + face.LocalNormal * 0.3f;

            var t = new Matrix(
                face.LocalRight.X,  face.LocalRight.Y,  face.LocalRight.Z,  0,
                face.LocalUp.X,     face.LocalUp.Y,     face.LocalUp.Z,     0,
                face.LocalNormal.X, face.LocalNormal.Y, face.LocalNormal.Z, 0,
                center.X,           center.Y,           center.Z,           1
            );
            mesh.AddOrientedBox(t, new Vector3(hw * 2, hh * 2, 0.3f), hatchCol);
        }
    }


    // ── Pass 6a: Panel seam lines ─────────────────────────────────────────────

    // Chamfer depth now varies per module (mod.ChamferDepth, seeded — see
    // StationGenerator.ChamferDepthForSeed), not a shared constant — the flat hull panel
    // is inset by mod.ChamferDepth * 0.707f on each side to make room for the beveled
    // edge trim, so anything else that assumes it knows the panel's true extent (seams
    // here) needs the same inset or it runs past the flat surface and onto the bevel.

    private static void GeneratePanelSeams(PlacedModule mod, FaceInfo face,
        System.Random rng, StationModuleMesh mesh)
    {
        if (!face.IsExposed) return;
        if (face.Width * face.Height < 25f) return;

        Color baseCol   = StationModuleRegistry.CategoryColor(mod.Definition.Category);
        Color seamColor = DarkenColor(baseCol, 0.72f);
        const float seamWidth  = 0.038f;
        const float seamOffset = 0.028f;

        // Edge trim (Pass 6b) only insets standard box modules — custom-mesh modules
        // (octagonal etc.) have no box chamfer to stay clear of.
        float chamferInset = mod.Definition.MeshFactory == null
            ? mod.ChamferDepth * 0.707f
            : 0f;
        float hw = face.Width  * 0.5f - chamferInset;
        float hh = face.Height * 0.5f - chamferInset;
        float hs = seamWidth * 0.5f;

        // Defensive floor — an unusually narrow face can pass the 25m² area check above
        // while still being thin enough in one dimension that the inset consumes it entirely.
        if (hw <= 0f || hh <= 0f) return;

        int hSeams = rng.NextDouble() < 0.55 ? 2 : 1;
        for (int i = 0; i < hSeams; i++)
        {
            float t    = hSeams == 1 ? 0.5f : (i == 0 ? 0.33f : 0.67f);
            float vOff = -face.Height * 0.5f + face.Height * t
                       + ((float)rng.NextDouble() - 0.5f) * face.Height * 0.08f;

            AddSeamStrip(mesh, face, horizontal: true, vOff, hw, hs, seamOffset, seamColor, rng);
        }

        int vSeams = face.Width > 20f ? (rng.NextDouble() < 0.6 ? 2 : 1) : 1;
        for (int i = 0; i < vSeams; i++)
        {
            float t    = vSeams == 1 ? 0.5f : (i == 0 ? 0.33f : 0.67f);
            float uOff = -face.Width * 0.5f + face.Width * t
                       + ((float)rng.NextDouble() - 0.5f) * face.Width * 0.08f;

            AddSeamStrip(mesh, face, horizontal: false, uOff, hh, hs, seamOffset, seamColor, rng);
        }
    }

    // Subdivides a seam into ~1.5m segments with slightly varied brightness (±15%),
    // matching the existing wear-pattern philosophy of subtle per-region variation
    // rather than a single flat-colour line. Deterministic per station seed — uses the
    // same seeded rng GeneratePanelSeams was already given, not per-frame randomness.
    private static void AddSeamStrip(StationModuleMesh mesh, FaceInfo face,
        bool horizontal, float centerOffset, float halfLen, float halfWidth, float zOffset,
        Color baseColor, System.Random rng)
    {
        float totalLen = halfLen * 2f;
        int   segments = Math.Max(1, (int)(totalLen / 1.5f));  // ~1.5m per segment

        for (int s = 0; s < segments; s++)
        {
            float u0 = -halfLen + totalLen * s       / segments;
            float u1 = -halfLen + totalLen * (s + 1) / segments;

            float variation = 0.85f + (float)rng.NextDouble() * 0.30f;  // ±15% brightness
            Color segColor  = new Color(
                (byte)Math.Clamp(baseColor.R * variation, 0, 255),
                (byte)Math.Clamp(baseColor.G * variation, 0, 255),
                (byte)Math.Clamp(baseColor.B * variation, 0, 255),
                baseColor.A);

            Vector3 v0, v1, v2, v3;
            if (horizontal)
            {
                v0 = LocalPointAbs(face, u0, centerOffset - halfWidth, zOffset);
                v1 = LocalPointAbs(face, u1, centerOffset - halfWidth, zOffset);
                v2 = LocalPointAbs(face, u1, centerOffset + halfWidth, zOffset);
                v3 = LocalPointAbs(face, u0, centerOffset + halfWidth, zOffset);
            }
            else
            {
                v0 = LocalPointAbs(face, centerOffset - halfWidth, u0, zOffset);
                v1 = LocalPointAbs(face, centerOffset + halfWidth, u0, zOffset);
                v2 = LocalPointAbs(face, centerOffset + halfWidth, u1, zOffset);
                v3 = LocalPointAbs(face, centerOffset - halfWidth, u1, zOffset);
            }
            mesh.AddQuad(v0, v1, v2, v3, segColor);
        }
    }

    // ── Pass 6b: Edge trim strips ─────────────────────────────────────────────

    private static void GenerateEdgeTrimStrips(PlacedModule mod, StationModuleMesh mesh)
    {
        // Octagonal and other custom-mesh modules use their own geometry — no box chamfers
        if (mod.Definition.MeshFactory != null) return;

        Vector3 half = mod.Definition.BoundingBox * 0.5f;
        Color trimColor = LightenColor(
            StationModuleRegistry.CategoryColor(mod.Definition.Category), 1.12f);
        AddChamferEdgeTrim(mesh, half, mod.ChamferDepth, trimColor);
    }

    // Adds the 45°-bevel edge strips (12 edges) and corner-fill triangles (8 corners) for an
    // axis-aligned box of the given half-extents and chamfer depth. Shared by GenerateEdgeTrimStrips
    // (standard box modules) and DockingBayHull (a MeshFactory module that builds its own chamfered
    // hull and so bypasses GenerateEdgeTrimStrips' MeshFactory-guard entirely).
    internal static void AddChamferEdgeTrim(StationModuleMesh mesh, Vector3 half, float chamferDepth, Color trimColor)
    {
        float inset = chamferDepth * 0.707f;

        // Width of each strip = diagonal of the inset square (√2 × inset).
        // Strips are shortened at both ends by inset so adjacent strips don't overlap at corners.
        float stripWidth = inset * MathF.Sqrt(2f);

        foreach (var (faceA, faceB, edgeDir, cornerSign) in BoxEdgeInfos)
        {
            float edgeHalfLen = edgeDir.X != 0 ? half.X
                              : edgeDir.Y != 0 ? half.Y : half.Z;

            Vector3 edgeMid = new(
                edgeDir.X != 0 ? 0 : cornerSign.X * half.X,
                edgeDir.Y != 0 ? 0 : cornerSign.Y * half.Y,
                edgeDir.Z != 0 ? 0 : cornerSign.Z * half.Z);

            // Strip centre sits on the 45° bisector of the two face planes, inset from the edge.
            // Vertices land exactly on the inset edges of the hull face panels — no gap, no lift needed.
            Vector3 outwardNorm = Vector3.Normalize(faceA + faceB);
            Vector3 stripCenter = edgeMid - (faceA + faceB) * (inset * 0.5f);
            mesh.AddQuad(stripCenter, outwardNorm, edgeDir, stripWidth, (edgeHalfLen - inset) * 2f, trimColor);
        }

        // Corner triangles — fill the small triangular hole at each of the 8 corners where
        // the shortened strips and inset hull face panels leave a gap.
        (int sx, int sy, int sz)[] corners =
            [(1,1,1),(1,1,-1),(1,-1,1),(1,-1,-1),(-1,1,1),(-1,1,-1),(-1,-1,1),(-1,-1,-1)];

        foreach (var (sx, sy, sz) in corners)
        {
            // One vertex per adjacent edge strip endpoint, coinciding with the hull panel corner.
            Vector3 c1 = new(sx * (half.X - inset), sy * half.Y,             sz * (half.Z - inset));
            Vector3 c2 = new(sx * half.X,           sy * (half.Y - inset),    sz * (half.Z - inset));
            Vector3 c3 = new(sx * (half.X - inset), sy * (half.Y - inset),    sz * half.Z);

            // Cross(c2-c1, c3-c1) is inward when sx*sy*sz > 0 — swap v1/v2 to face outward.
            if (sx * sy * sz > 0)
                mesh.AddTriangle(c1, c3, c2, trimColor);
            else
                mesh.AddTriangle(c1, c2, c3, trimColor);
        }
    }

    // ── Pass 6c: Vent grilles ─────────────────────────────────────────────────

    private enum VentStyle { HorizontalBars, Louvered, ScreenMesh }

    private static VentStyle SelectVentStyle(System.Random rng) => rng.NextDouble() switch
    {
        < 0.45 => VentStyle.HorizontalBars,
        < 0.80 => VentStyle.Louvered,
        _      => VentStyle.ScreenMesh,
    };

    private static void GenerateVentGrilles(PlacedModule mod, FaceInfo face,
        System.Random rng, StationModuleMesh mesh, FaceOccupancy occupancy)
    {
        if (!face.IsExposed) return;
        if (face.Width * face.Height < 15f) return;

        float prob = mod.Definition.Category switch
        {
            "industrial" or "core" => 0.65f,
            "cargo"      or "fuel" => 0.45f,
            "connector"            => 0.35f,
            _                      => 0.20f,
        };
        if (rng.NextDouble() > prob) return;

        int remaining = rng.Next(1, 4);
        int attempts  = remaining * 4;
        float margin  = 1.2f;

        for (int i = 0; i < attempts && remaining > 0; i++)
        {
            float ventW = 0.8f  + (float)rng.NextDouble() * 1.4f;
            float ventH = 0.45f + (float)rng.NextDouble() * 0.7f;

            float cu = ((float)rng.NextDouble() - 0.5f) * (face.Width  - margin * 2 - ventW);
            float cv = ((float)rng.NextDouble() - 0.5f) * (face.Height - margin * 2 - ventH);

            if (!occupancy.TryOccupy(cu, cv, ventW * 0.5f, ventH * 0.5f)) continue;
            remaining--;

            switch (SelectVentStyle(rng))
            {
                case VentStyle.HorizontalBars:
                    AddHBarVentGrille(mod, face, cu, cv, ventW, ventH, rng, mesh);
                    break;
                case VentStyle.Louvered:
                    AddLouVentGrille(mod, face, cu, cv, ventW, ventH, rng, mesh);
                    break;
                case VentStyle.ScreenMesh:
                    AddScreenVentGrille(mod, face, cu, cv, ventW, ventH, rng, mesh);
                    break;
            }
        }
    }

    // Shared: dark recess behind grille opening.
    private static void AddVentBacking(FaceInfo face, float cu, float cv,
        float ventW, float ventH, Color col, StationModuleMesh mesh)
    {
        const float shadowOff = 0.018f;
        float hw = ventW * 0.5f, hh = ventH * 0.5f;
        mesh.AddQuad(
            LocalPointAbs(face, cu - hw, cv - hh, shadowOff),
            LocalPointAbs(face, cu + hw, cv - hh, shadowOff),
            LocalPointAbs(face, cu + hw, cv + hh, shadowOff),
            LocalPointAbs(face, cu - hw, cv + hh, shadowOff), col);
    }

    // Shared: thin raised border around a vent opening.
    private static void AddVentFrame(PlacedModule mod, FaceInfo face,
        float cu, float cv, float ventW, float ventH, StationModuleMesh mesh)
    {
        Color frameCol  = DarkenColor(StationModuleRegistry.CategoryColor(mod.Definition.Category), 0.58f);
        float hw = ventW * 0.5f, hh = ventH * 0.5f;
        const float fw = 0.12f;   // frame width
        const float fo = 0.025f;  // frame Z offset

        // Top bar
        mesh.AddQuad(
            LocalPointAbs(face, cu - hw - fw, cv + hh,      fo),
            LocalPointAbs(face, cu + hw + fw, cv + hh,      fo),
            LocalPointAbs(face, cu + hw + fw, cv + hh + fw, fo),
            LocalPointAbs(face, cu - hw - fw, cv + hh + fw, fo), frameCol);

        // Bottom bar
        mesh.AddQuad(
            LocalPointAbs(face, cu - hw - fw, cv - hh - fw, fo),
            LocalPointAbs(face, cu + hw + fw, cv - hh - fw, fo),
            LocalPointAbs(face, cu + hw + fw, cv - hh,      fo),
            LocalPointAbs(face, cu - hw - fw, cv - hh,      fo), frameCol);

        // Left bar
        mesh.AddQuad(
            LocalPointAbs(face, cu - hw - fw, cv - hh, fo),
            LocalPointAbs(face, cu - hw,      cv - hh, fo),
            LocalPointAbs(face, cu - hw,      cv + hh, fo),
            LocalPointAbs(face, cu - hw - fw, cv + hh, fo), frameCol);

        // Right bar
        mesh.AddQuad(
            LocalPointAbs(face, cu + hw,      cv - hh, fo),
            LocalPointAbs(face, cu + hw + fw, cv - hh, fo),
            LocalPointAbs(face, cu + hw + fw, cv + hh, fo),
            LocalPointAbs(face, cu + hw,      cv + hh, fo), frameCol);
    }

    // Existing grille style: horizontal or vertical bars.
    private static void AddHBarVentGrille(PlacedModule mod, FaceInfo face,
        float cu, float cv, float ventW, float ventH, System.Random rng,
        StationModuleMesh mesh)
    {
        Color baseCol  = StationModuleRegistry.CategoryColor(mod.Definition.Category);
        Color barCol   = DarkenColor(baseCol, 0.45f);

        AddVentBacking(face, cu, cv, ventW, ventH, new Color(12, 12, 14), mesh);

        bool horizontal = rng.NextDouble() < 0.6;
        int  barCount   = rng.Next(3, 8);
        float hw = ventW * 0.5f, hh = ventH * 0.5f;
        const float barThick = 0.04f;
        const float barOff   = 0.030f;

        for (int b = 0; b < barCount; b++)
        {
            float t   = (b + 0.5f) / barCount;
            float pos = horizontal
                ? cv - hh + ventH * t
                : cu - hw + ventW * t;

            float b0u = horizontal ? cu - hw  : pos - barThick * 0.5f;
            float b0v = horizontal ? pos - barThick * 0.5f : cv - hh;
            float b1u = horizontal ? cu + hw  : pos + barThick * 0.5f;
            float b1v = horizontal ? pos + barThick * 0.5f : cv + hh;

            mesh.AddQuad(
                LocalPointAbs(face, b0u, b0v, barOff),
                LocalPointAbs(face, b1u, b0v, barOff),
                LocalPointAbs(face, b1u, b1v, barOff),
                LocalPointAbs(face, b0u, b1v, barOff), barCol);
        }

        AddVentFrame(mod, face, cu, cv, ventW, ventH, mesh);
    }

    // Louvered vent: angled slats like venetian blinds.
    private static void AddLouVentGrille(PlacedModule mod, FaceInfo face,
        float cu, float cv, float ventW, float ventH, System.Random rng,
        StationModuleMesh mesh)
    {
        Color baseCol  = StationModuleRegistry.CategoryColor(mod.Definition.Category);
        Color slatCol  = DarkenColor(baseCol, 0.50f);

        AddVentBacking(face, cu, cv, ventW, ventH, new Color(10, 10, 12), mesh);

        int   slats     = rng.Next(4, 8);
        float slatsH    = ventH / slats;
        const float slabThick = 0.045f;
        float hh = ventH * 0.5f, hw = ventW * 0.5f;

        for (int s = 0; s < slats; s++)
        {
            float vCentre = cv - hh + slatsH * (s + 0.5f);
            float vBot    = vCentre - slatsH * 0.3f;
            float vTop    = vCentre + slatsH * 0.3f;

            // Slat slopes in the normal direction from bottom to top — gives angled appearance.
            Vector3 s0 = LocalPointAbs(face, cu - hw, vBot, 0.022f);
            Vector3 s1 = LocalPointAbs(face, cu + hw, vBot, 0.022f);
            Vector3 s2 = LocalPointAbs(face, cu + hw, vTop, 0.022f + slabThick);
            Vector3 s3 = LocalPointAbs(face, cu - hw, vTop, 0.022f + slabThick);
            mesh.AddQuad(s0, s1, s2, s3, slatCol);
        }

        AddVentFrame(mod, face, cu, cv, ventW, ventH, mesh);
    }

    // Screen mesh vent: fine grid of thin bars in both directions.
    private static void AddScreenVentGrille(PlacedModule mod, FaceInfo face,
        float cu, float cv, float ventW, float ventH, System.Random rng,
        StationModuleMesh mesh)
    {
        Color baseCol  = StationModuleRegistry.CategoryColor(mod.Definition.Category);
        Color wireCol  = DarkenColor(baseCol, 0.45f);

        AddVentBacking(face, cu, cv, ventW, ventH, new Color(8, 8, 10), mesh);

        const float wireThick = 0.025f;
        const float wireOff   = 0.026f;
        float hw = ventW * 0.5f, hh = ventH * 0.5f;

        int hCount = Math.Max(1, (int)(ventW / 0.35f));
        int vCount = Math.Max(1, (int)(ventH / 0.35f));

        // Horizontal wires
        for (int i = 1; i < vCount; i++)
        {
            float vPos = cv - hh + ventH * ((float)i / vCount);
            float hs   = wireThick * 0.5f;
            mesh.AddQuad(
                LocalPointAbs(face, cu - hw, vPos - hs, wireOff),
                LocalPointAbs(face, cu + hw, vPos - hs, wireOff),
                LocalPointAbs(face, cu + hw, vPos + hs, wireOff),
                LocalPointAbs(face, cu - hw, vPos + hs, wireOff), wireCol);
        }

        // Vertical wires (offset slightly forward so they cross over horizontals)
        for (int i = 1; i < hCount; i++)
        {
            float uPos = cu - hw + ventW * ((float)i / hCount);
            float hs   = wireThick * 0.5f;
            mesh.AddQuad(
                LocalPointAbs(face, uPos - hs, cv - hh, wireOff + 0.005f),
                LocalPointAbs(face, uPos + hs, cv - hh, wireOff + 0.005f),
                LocalPointAbs(face, uPos + hs, cv + hh, wireOff + 0.005f),
                LocalPointAbs(face, uPos - hs, cv + hh, wireOff + 0.005f), wireCol);
        }

        AddVentFrame(mod, face, cu, cv, ventW, ventH, mesh);
    }

}
