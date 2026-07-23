using Inferior.Game.Containers;
using Inferior.Rendering;
using Microsoft.Xna.Framework;

namespace Inferior.Game.StationGen;

public static partial class StationDecorator
{
    // ── Pass 1: Windows ───────────────────────────────────────────────────────

    private static float WindowProbability(string category) => category switch
    {
        "hab"        => 0.80f,
        "science"    => 0.70f,
        "docking"    => 0.40f,
        "core"       => 0.30f,
        "industrial" => 0.30f,
        "connector"  => 0.20f,
        "cargo"      => 0.20f,
        _            => 0.25f,
    };

    private static readonly Color WarmWhiteColor    = new(255, 250, 220);
    private static readonly Color NeutralWhiteColor = new(240, 240, 248);
    private static readonly Color CoolBlueColor     = new(210, 225, 255);
    private static readonly Color DimAmberColor     = new(200, 170, 100);
    private static readonly Color DarkWindowColor   = new( 31,  30,  26);  // WarmWhite × 0.12f

    private static Color PickWindowColor(string category, System.Random rng)
    {
        double r = rng.NextDouble();
        return category switch
        {
            "hab" => r < 0.55 ? WarmWhiteColor
                   : r < 0.70 ? NeutralWhiteColor
                   : r < 0.75 ? CoolBlueColor
                   : r < 0.90 ? DimAmberColor
                   :             DarkWindowColor,

            "science" => r < 0.20 ? WarmWhiteColor
                       : r < 0.40 ? NeutralWhiteColor
                       : r < 0.80 ? CoolBlueColor
                       : r < 0.85 ? DimAmberColor
                       :             DarkWindowColor,

            "industrial" => r < 0.15 ? WarmWhiteColor
                          : r < 0.35 ? NeutralWhiteColor
                          : r < 0.40 ? CoolBlueColor
                          : r < 0.80 ? DimAmberColor
                          :             DarkWindowColor,

            "cargo" => r < 0.10 ? WarmWhiteColor
                      : r < 0.50 ? NeutralWhiteColor
                      : r < 0.60 ? CoolBlueColor
                      : r < 0.80 ? DimAmberColor
                      :             DarkWindowColor,

            _ => r < 0.30 ? WarmWhiteColor
               : r < 0.60 ? NeutralWhiteColor
               : r < 0.75 ? CoolBlueColor
               : r < 0.90 ? DimAmberColor
               :             DarkWindowColor,
        };
    }

    // Brief "Absolute Window Sizing": spacing gets a ceiling alongside the existing floor,
    // so window SIZE stops scaling with face size — count scales with area instead (cols/
    // rows already derive from face.Width/gridW, so bounding gridW/gridH is the whole fix).
    // Below ~22m face width neither ceiling binds, so ordinary modules are unaffected by
    // construction — no mega-module branch needed, the rule is just true everywhere.
    // Values are an eye-tuned starting point (project convention: Code picks sensible
    // starts, Timo adjusts by eye), not derived:
    private const float MinWindowSpacing       = 2f;    // the pre-existing floor, now named
    private const float MaxWindowSpacingDense  = 4.5f;  // -> ~1.6-2.5m windows across the 3 sizeScale tiers
    private const float MaxWindowSpacingSparse = 7.5f;  // separate ceiling: a single shared one would
                                                         // clamp ordinary sparse faces too (e.g. a 24m
                                                         // cargo face: 8m -> 4.5m spacing) and erase the
                                                         // deliberate fewer-larger-windows sparse look.
    private const float MaxWindowSize          = 4.25f; // direct clamp on winW/winH — redundant with the
                                                         // spacing ceiling in normal cases (the sparse
                                                         // ceiling's own worst case is 7.5 * 0.55 sizeScale
                                                         // = 4.125m, so 4.25 never double-clips an ordinary
                                                         // module — measured, not assumed: an earlier 3.5m
                                                         // draft DID clip core/industrial/cargo's sparse,
                                                         // largest-tier windows a further 5-20%, exactly the
                                                         // "small module visibly changes size" case the brief
                                                         // says to fix by raising the constant, not
                                                         // special-casing) but still makes the absolute-size
                                                         // guarantee explicit rather than emergent, so a future
                                                         // sizeScale change can't silently reintroduce giant
                                                         // windows, and still clips mega faces hard (their
                                                         // cap-widened spacing routinely exceeds 13m).
    private const int   MaxWindowCountPerFace  = 300;   // safety cap against runaway geometry on very large
                                                         // faces; binds by widening spacing evenly (see
                                                         // below), never by truncating mid-grid.

    // Pure grid-sizing math, no rng/mesh side effects — split out so cols/rows/window size
    // can be measured directly in a test instead of estimated (same GraphicsDevice-free
    // testable-helper pattern as OffsetPaletteForVariant/SelectVariantIndex/etc. in
    // StationTextureRegistry). Called once from GenerateWindows, same as before extraction.
    internal static (float gridW, float gridH, int cols, int rows, float winW, float winH) ComputeWindowGrid(
        float faceWidth, float faceHeight, bool sparse, float sizeScale)
    {
        float gridW = Math.Clamp(faceWidth  / (sparse ? 3f : 5f), MinWindowSpacing, sparse ? MaxWindowSpacingSparse : MaxWindowSpacingDense);
        float gridH = Math.Clamp(faceHeight / (sparse ? 3f : 4f), MinWindowSpacing, sparse ? MaxWindowSpacingSparse : MaxWindowSpacingDense);

        int cols = Math.Max(1, (int)(faceWidth  / gridW));
        int rows = Math.Max(1, (int)(faceHeight / gridH));

        // Safety cap: widen spacing evenly (both axes, same factor) rather than truncating
        // the grid mid-populate, so a capped face still reads as an even distribution.
        if (cols * rows > MaxWindowCountPerFace)
        {
            float widen = MathF.Sqrt((cols * rows) / (float)MaxWindowCountPerFace);
            gridW *= widen;
            gridH *= widen;
            cols   = Math.Max(1, (int)(faceWidth  / gridW));
            rows   = Math.Max(1, (int)(faceHeight / gridH));
        }

        float winW = MathF.Min(gridW * sizeScale, MaxWindowSize);
        float winH = MathF.Min(gridH * sizeScale, MaxWindowSize);

        return (gridW, gridH, cols, rows, winW, winH);
    }

    private static void GenerateWindows(PlacedModule mod, FaceInfo face,
        System.Random rng, StationModuleMesh mesh, StationModuleMesh glassMesh,
        FaceOccupancy occupancy)
    {
        if (!face.IsExposed)  return;
        if (face.Width  < 3f) return;
        if (face.Height < 3f) return;
        if (rng.NextDouble() > WindowProbability(mod.Definition.Category)) return;
        if (rng.NextDouble() < 0.20) return;  // 20% blank face

        bool   sparse    = rng.NextDouble() < 0.30;
        double sizeTier   = rng.NextDouble();
        float  sizeScale  = sizeTier < 0.30 ? 0.55f : sizeTier < 0.70 ? 0.45f : 0.35f;
        var (gridW, gridH, cols, rows, winW, winH) = ComputeWindowGrid(face.Width, face.Height, sparse, sizeScale);

        float startU = -(cols - 1) * gridW * 0.5f;
        float startV = -(rows - 1) * gridH * 0.5f;

        bool  canPorthole = mod.Definition.Category is "hab" or "science";
        Color hullColor   = StationModuleRegistry.CategoryColor(mod.Definition.Category);
        Color frameColor  = Color.Lerp(hullColor, new Color(50, 48, 44), 0.20f);

        // Detect near-horizontal face in world space — gradient is wrong on ceiling/floor windows.
        mod.Transform.Decompose(out _, out Quaternion modRot, out _);
        Vector3 worldFaceN      = Vector3.TransformNormal(face.LocalNormal, Matrix.CreateFromQuaternion(modRot));
        bool    isHorizontalFace = MathF.Abs(worldFaceN.Y) > 0.8f;

        for (int row = 0; row < rows; row++)
        for (int col = 0; col < cols; col++)
        {
            if (rng.NextDouble() < 0.20) continue;

            float cu = startU + col * gridW;
            float cv = startV + row * gridH;
            if (!occupancy.TryOccupy(cu, cv, winW * 0.5f, winH * 0.5f)) continue;

            Color   winCol  = PickWindowColor(mod.Definition.Category, rng);
            Vector3 facePos = face.LocalCenter
                + face.LocalRight * cu
                + face.LocalUp    * cv;

            if (canPorthole && rng.NextDouble() < 0.20)
            {
                float portholeSize = MathF.Min(winW, winH);
                if (rng.NextDouble() < 0.25)
                    AddCupola(mesh, glassMesh, facePos, face.LocalNormal, face.LocalUp,
                              portholeSize, winCol, frameColor, isHorizontalFace);
                else
                    AddOctagonPorthole(mesh, glassMesh, facePos,
                                       face.LocalNormal, face.LocalUp, portholeSize,
                                       winCol, frameColor, isHorizontalFace);
            }
            else
            {
                // Frame backing quad — blended dark neutral, goes through lighting pass
                float frameBorder = Math.Clamp(MathF.Min(winW, winH) * 0.12f, 0.08f, 0.25f);
                mesh.AddQuad(facePos + face.LocalNormal * 0.01f, face.LocalNormal, face.LocalUp,
                             winW + frameBorder * 2f, winH + frameBorder * 2f, frameColor);

                // Glass quad proud of frame (+0.05m from face surface, emissive — no lighting pass)
                AddGlassQuad(glassMesh, facePos + face.LocalNormal * 0.05f,
                             face.LocalNormal, face.LocalUp, winW, winH, winCol, isHorizontalFace);

                if (rng.NextDouble() < 0.55)
                    AddWindowBraces(mesh, facePos, face.LocalNormal, face.LocalUp,
                                    winW, winH, frameColor);
            }
        }
    }

    // 8-sided porthole: frame backing ring in mesh (shaded), glass fan in glassMesh (emissive).
    // facePos is the un-offset face-surface position; offsets are applied internally.
    private static void AddOctagonPorthole(StationModuleMesh mesh, StationModuleMesh glassMesh,
        Vector3 facePos, Vector3 normal, Vector3 up, float size, Color color, Color frameColor,
        bool flatGlass)
    {
        Vector3 right = Vector3.Normalize(Vector3.Cross(up, normal));
        float r = size * 0.5f;

        // Frame backing polygon at +0.01m, scaled out by 1.15x (frameFraction = 0.15f)
        const float frameFraction = 0.15f;
        Vector3 frameCenter = facePos + normal * 0.01f;
        float   frameR      = r * (1f + frameFraction);
        var     framePts    = new Vector3[8];
        for (int i = 0; i < 8; i++)
        {
            float angle = MathF.PI / 8f + i * (MathF.PI / 4f);
            framePts[i] = frameCenter + right * (frameR * MathF.Cos(angle)) + up * (frameR * MathF.Sin(angle));
        }
        for (int i = 0; i < 8; i++)
            mesh.AddTriangle(frameCenter, framePts[i], framePts[(i + 1) % 8], frameColor);

        // Glass fan at +0.05m — emissive, gradient or flat
        Vector3 glassCenter = facePos + normal * 0.05f;
        var     glassPts    = new Vector3[8];
        for (int i = 0; i < 8; i++)
        {
            float angle = MathF.PI / 8f + i * (MathF.PI / 4f);
            glassPts[i] = glassCenter + right * (r * MathF.Cos(angle)) + up * (r * MathF.Sin(angle));
        }

        if (flatGlass)
        {
            for (int i = 0; i < 8; i++)
                glassMesh.AddTriangle(glassCenter, glassPts[i], glassPts[(i + 1) % 8], color);
        }
        else
        {
            // Gradient range: full radius along face.LocalUp
            float ctrProj  = Vector3.Dot(glassCenter, up);
            float minProj  = ctrProj - r;
            float maxProj  = ctrProj + r;
            Color ctrColor = GlassGradientColor(color, glassCenter, up, minProj, maxProj);
            for (int i = 0; i < 8; i++)
            {
                Color ci  = GlassGradientColor(color, glassPts[i],          up, minProj, maxProj);
                Color ci1 = GlassGradientColor(color, glassPts[(i + 1) % 8], up, minProj, maxProj);
                glassMesh.AddTriangleGradient(glassCenter, ctrColor, glassPts[i], ci,
                                              glassPts[(i + 1) % 8], ci1);
            }
        }
    }

    // Cross-pane dividers: raised thin quads 2cm proud of glass (+0.07m from face surface).
    // faceBase is the un-offset face-surface position of the window centre.
    private static void AddWindowBraces(StationModuleMesh mesh, Vector3 faceBase,
        Vector3 normal, Vector3 up, float winW, float winH, Color color)
    {
        const float barThick = 0.03f;
        Vector3 pos = faceBase + normal * 0.07f;
        mesh.AddQuad(pos, normal, up, winW,     barThick, color);
        mesh.AddQuad(pos, normal, up, barThick, winH,     color);
    }

    // Pyramid viewport: 4 triangular glass panels (glassMesh) + dark recess base + frame + braces (mesh).
    // facePos is the un-offset face-surface position; glass goes to +0.05m, frame to +0.01m.
    private static void AddCupola(StationModuleMesh mesh, StationModuleMesh glassMesh,
        Vector3 facePos, Vector3 normal, Vector3 up, float size, Color glassColor,
        Color frameColor, bool flatGlass)
    {
        Vector3 right = Vector3.Normalize(Vector3.Cross(up, normal));
        float hw = size * 0.5f;

        // Glass at +0.05m
        Vector3 glassBase = facePos + normal * 0.05f;
        Vector3 apex      = glassBase + normal * hw;
        Vector3[] base4 =
        [
            glassBase - right * hw - up * hw,  // BL
            glassBase - right * hw + up * hw,  // TL
            glassBase + right * hw + up * hw,  // TR
            glassBase + right * hw - up * hw,  // BR
        ];

        // Frame backing: for each triangle, scale vertices 1.15x from centroid, shifted to +0.01m
        const float frameFraction = 0.15f;
        float frameShift = 0.01f - 0.05f;  // -0.04m along normal
        for (int i = 0; i < 4; i++)
        {
            Vector3 a = base4[i];
            Vector3 b = apex;
            Vector3 c = base4[(i + 1) % 4];
            // Shift to +0.01m level
            Vector3 fa = a + normal * frameShift;
            Vector3 fb = b + normal * frameShift;
            Vector3 fc = c + normal * frameShift;
            Vector3 fCentroid = (fa + fb + fc) / 3f;
            Vector3 sfa = fCentroid + (fa - fCentroid) * (1f + frameFraction);
            Vector3 sfb = fCentroid + (fb - fCentroid) * (1f + frameFraction);
            Vector3 sfc = fCentroid + (fc - fCentroid) * (1f + frameFraction);
            mesh.AddTriangle(sfa, sfb, sfc, frameColor);
        }

        // Dark inner base behind frame, at face surface level
        Vector3 darkBase = facePos;
        Vector3[] darkCorners =
        [
            darkBase - right * hw - up * hw,
            darkBase - right * hw + up * hw,
            darkBase + right * hw + up * hw,
            darkBase + right * hw - up * hw,
        ];
        mesh.AddQuad(darkCorners[0], darkCorners[3], darkCorners[2], darkCorners[1], new Color(20, 22, 28));

        // Glass panels with gradient, and edge braces
        float minProj = Vector3.Dot(glassBase - up * hw, up);
        float maxProj = Vector3.Dot(apex, up);
        if (maxProj < minProj) (minProj, maxProj) = (maxProj, minProj);

        for (int i = 0; i < 4; i++)
        {
            Vector3 a = base4[i];
            Vector3 b = apex;
            Vector3 c = base4[(i + 1) % 4];

            // Glass triangle
            if (flatGlass)
            {
                glassMesh.AddTriangle(a, b, c, glassColor);
            }
            else
            {
                Color ca = GlassGradientColor(glassColor, a, up, minProj, maxProj);
                Color cb = GlassGradientColor(glassColor, b, up, minProj, maxProj);
                Color cc = GlassGradientColor(glassColor, c, up, minProj, maxProj);
                glassMesh.AddTriangleGradient(a, ca, b, cb, c, cc);
            }

            // Edge braces: one per edge, angled inward toward triangle interior
            AddTriangleBrace(mesh, a, b, c, normal, frameColor);  // edge a→b, third vert = c
            AddTriangleBrace(mesh, b, c, a, normal, frameColor);  // edge b→c, third vert = a
            AddTriangleBrace(mesh, c, a, b, normal, frameColor);  // edge c→a, third vert = b
        }
    }

    // Adds a single structural brace quad along edge A→B, leaning inward toward thirdVertex.
    private static void AddTriangleBrace(StationModuleMesh mesh,
        Vector3 a, Vector3 b, Vector3 thirdVertex, Vector3 faceNormal, Color color)
    {
        Vector3 edgeDir  = Vector3.Normalize(b - a);
        Vector3 inward   = Vector3.Normalize(Vector3.Cross(faceNormal, edgeDir));
        // Ensure inward points toward the triangle interior
        if (Vector3.Dot(inward, thirdVertex - a) < 0f) inward = -inward;

        const float braceWidth  = 0.03f;
        const float braceHeight = 0.02f;
        Vector3 v2 = b + inward * braceWidth + faceNormal * braceHeight;
        Vector3 v3 = a + inward * braceWidth + faceNormal * braceHeight;
        mesh.AddQuad(a, b, v2, v3, color);
    }


    // ── Glass gradient helpers ────────────────────────────────────────────────

    private static Color GlassTopColor(Color c) => Color.Lerp(c, Color.White, 0.18f);

    private static Color GlassBottomColor(Color c) => new Color(
        (byte)MathF.Min(c.R * 0.72f, 255f),
        (byte)MathF.Min(c.G * 0.72f, 255f),
        (byte)Math.Min((int)(c.B * 0.72f) + 8, 255),
        c.A);

    // Maps a vertex position along `upDir` to a gradient colour between bottom and top.
    private static Color GlassGradientColor(Color c, Vector3 v, Vector3 upDir, float minY, float maxY)
    {
        float t = maxY > minY ? (Vector3.Dot(v, upDir) - minY) / (maxY - minY) : 0.5f;
        return Color.Lerp(GlassBottomColor(c), GlassTopColor(c), Math.Clamp(t, 0f, 1f));
    }

    // Emits a glass quad into glassMesh with a vertical gradient (bottom darker/cooler, top lighter).
    // Skips the gradient on near-horizontal faces where up-direction is ambiguous.
    private static void AddGlassQuad(StationModuleMesh glassMesh,
        Vector3 center, Vector3 normal, Vector3 up,
        float width, float height, Color winCol, bool flatColor)
    {
        Vector3 right = Vector3.Normalize(Vector3.Cross(up, normal));
        float   hw    = width  * 0.5f;
        float   hh    = height * 0.5f;
        Vector3 bl    = center - right * hw - up * hh;
        Vector3 br    = center + right * hw - up * hh;
        Vector3 tr    = center + right * hw + up * hh;
        Vector3 tl    = center - right * hw + up * hh;

        if (flatColor)
        {
            glassMesh.AddQuad(bl, br, tr, tl, winCol);
            return;
        }

        // BL/BR share the bottom projection; TL/TR share the top.
        float minProj = Vector3.Dot(bl, up);
        float maxProj = Vector3.Dot(tl, up);
        glassMesh.AddQuadGradient(
            bl, GlassGradientColor(winCol, bl, up, minProj, maxProj),
            br, GlassGradientColor(winCol, br, up, minProj, maxProj),
            tr, GlassGradientColor(winCol, tr, up, minProj, maxProj),
            tl, GlassGradientColor(winCol, tl, up, minProj, maxProj));
    }

}
