using Inferior.Core.Math;
using Inferior.Core.Random;
using Inferior.Game.StationGen;
using Inferior.Rendering;
using Microsoft.Xna.Framework;
using System.Text;

namespace Inferior.Game.Containers;

/// <summary>
/// Deterministic container mesh builder. All geometry uses StationModuleMesh so lighting,
/// winding conventions, and text helpers are identical to station decoration.
/// The final mesh is extracted as CPU arrays (no GraphicsDevice needed at generation time).
/// </summary>
public static class ShippingContainerFactory
{
    // Physical constants — metres
    private const float Lx      = 6.0f;
    private const float Ly      = 2.5f;
    private const float Lz      = 2.5f;
    private const float Chamfer = 0.20f;

    private const float HLx = Lx * 0.5f;
    private const float HLy = Ly * 0.5f;
    private const float HLz = Lz * 0.5f;

    private const float InsetZoneHalfLen       = 2.0f;
    private const float FaceWidthAfterChamfer  = Ly - 2f * Chamfer;              // 2.1 m
    private const float FaceLengthAfterChamfer = Lx - 2f * Chamfer;              // 5.6 m
    private const float HLxAfterChamfer        = FaceLengthAfterChamfer * 0.5f;  // 2.8 m
    private const float PlainEndLen            = HLxAfterChamfer - InsetZoneHalfLen; // 0.8 m

    // ── Public API ────────────────────────────────────────────────────────────

    public static ShippingContainer Generate(
        Color color,
        float wear,
        int sidePatternSeed,
        string? text = null,
        LockGrade lockGrade = LockGrade.Civilian)
    {
        string manufacturerText = text ?? GenerateManufacturerName(sidePatternSeed);
        var (verts, indices) = GenerateVertices(color, wear, sidePatternSeed, manufacturerText, lockGrade);

        return new ShippingContainer
        {
            Id               = $"CTR-{(uint)sidePatternSeed:X8}",
            PrimaryColor     = color,
            Wear             = wear,
            SidePatternSeed  = sidePatternSeed,
            ManufacturerText = manufacturerText,
            Lock             = lockGrade,
            IsLocked         = lockGrade != LockGrade.None,
            WorldPosition    = DVec3.Zero,
            Orientation      = Quaternion.Identity,
            Vertices         = verts,
            Indices          = indices,
        };
    }

    // Geometry-only entry point — shared by the standalone/debug-spawn path above and
    // by StationDecorator, so station-placed greeble containers get the exact same
    // chamfer/inset/fastener/text/wear geometry as standalone ones, instead of a
    // separately hand-maintained reimplementation that had drifted (mirrored text).
    internal static (VertexPositionNormalColorTexture[] verts, short[] indices) GenerateVertices(
        Color color, float wear, int sidePatternSeed, string? text, LockGrade lockGrade)
    {
        var mesh = new StationModuleMesh { Texture = SurfaceTexture.CleanPanel };

        BuildChamferedBox  (mesh, color);
        BuildFasteners     (mesh, color);
        BuildLongFaceInsets(mesh, color, sidePatternSeed);
        BuildEndDoors      (mesh, color);
        AddContainerText   (mesh, text ?? GenerateManufacturerName(sidePatternSeed));

        ApplyWear(mesh, wear, sidePatternSeed);

        return mesh.ToArraysWithNormals();
    }

    public static ShippingContainer[] Generate(
        int count,
        int masterSeed,
        Color[] colors,
        (float min, float max) wearRange,
        int[]? sidePatternSeeds = null)
    {
        var rng        = new SeededRandom(masterSeed);
        var containers = new ShippingContainer[count];
        for (int i = 0; i < count; i++)
        {
            Color color       = rng.Pick(colors);
            float wear        = rng.NextFloat(wearRange.min, wearRange.max);
            int   patternSeed = sidePatternSeeds != null
                ? rng.Pick(sidePatternSeeds)
                : rng.NextInt(int.MinValue, int.MaxValue);
            containers[i] = Generate(color, wear, patternSeed);
        }
        return containers;
    }

    // ── Manufacturer name ─────────────────────────────────────────────────────

    public static string GenerateManufacturerName(int seed)
    {
        var rng = new SeededRandom(seed ^ 0x4C4F4744);

        string[] prefixes =
        [
            "Intergalactic", "Galactic", "Interstellar", "Deep Space", "Rapid", "Swift",
            "Heavy", "Standard", "Universal", "Colonial", "Hyperspatial",
            "Outer Rim", "Femtometer", "Quantum",
            "Nova", "Stellar", "Cosmic", "Astro", "Orbital", "Solar",
            "Lunar", "Andromedan", "Sirius",
            "Brothers", "Sisters", "Partners", "International", "Global",
            "United", "Federated", "Imperial", "Dynastic",
            "Red", "Blue", "Green", "Yellow", "Black", "White",
            "Crimson", "Azure", "Emerald", "Golden", "Silver", "Onyx",
            "Swift", "Rapid", "Heavy", "Standard", "Universal", "Colonial", "Hyperspatial",
            "Benevolent", "Merciless", "Pious", "Ruthless", "Noble", "Infamous",
            "Valiant", "Cunning", "Brave", "Fearless", "Mighty", "Sly",
            "Understated", "Overpriced", "Reliable", "Shoddy", "Fast", "Slow",
            "Ring", "Truested", "True", "Nect Day", "NextGen", "Future", "Legacy", "Prime", "Pioneer", "Vanguard",
        ];
        string[] coreNouns =
        [
            "Shipping", "Transport", "Transportation", "Cargo", "Freight",
            "Haulage", "Logistics", "Forwarding",
            "Transit", "Lines", "Carriers", "Movers", "Express", "Freighters",
            "Transports", "Shippers", "Logistix", "Freightlines", "Cargoes",
            "Manufacturing", "Production", "Assembly", "Fabrication",
            "Engineering", "Technologies", "Tech", "Innovations", "Works",
            "Resources", "Materials", "Metals", "Minerals", "Commodities",
            "Energy", "Power", "Dynamics", "Solutions", "Systems",
        ];
        string[] suffixes =
        [
            "Company", "Ltd", "Corp", "Group",
            "Holdings", "Associates", "Alliance",
            "Consortium", "Syndicate", "Collective", "Union",
            "Conglomerate", "Cartel",
            "Securities", "Dynamics", "Industries", "Systems",
            "Solutions", "Enterprises", "Logistics", "Freightways",
            "Lines", "Carriers", "Express", "Freighters",
        ];

        var sb = new StringBuilder();
        if (rng.NextBool(0.60)) { sb.Append(rng.Pick(prefixes)); sb.Append(' '); }
        sb.Append(rng.Pick(coreNouns));
        if (rng.NextBool(0.40)) { sb.Append(" of "); sb.Append(GeneratePlaceName(rng)); }
        sb.Append(' ');
        sb.Append(rng.Pick(suffixes));
        return sb.ToString();
    }

    private static string GeneratePlaceName(SeededRandom rng)
    {
        string[] starts = ["And", "Veth", "Kal", "Thes", "Mor", "Hex", "Zel", "Bren", "Cor", "Del"];
        string[] mids   = ["or", "eth", "al", "es", "el", "en"];
        string[] ends   = ["min", "us", "rix", "und", "sel", "vak", "is", "on", "ax", "um", "ar", "or"];

        var sb = new StringBuilder();
        sb.Append(rng.Pick(starts));
        if (rng.NextBool(0.55)) sb.Append(rng.Pick(mids));
        sb.Append(rng.Pick(ends));
        return sb.ToString();
    }

    // ── Chamfered box ─────────────────────────────────────────────────────────
    // Container centred at origin. Long axis = X. Geometry comes from the generic
    // ChamferedBox helper — winding is verified automatically there, not hand-derived.

    private static void BuildChamferedBox(StationModuleMesh mesh, Color color)
    {
        var box = ChamferedBox.Build(new Vector3(HLx, HLy, HLz), Chamfer);

        // MainFaces intentionally not drawn — every one of the 6 main-face areas is
        // fully covered by more specific decoration elsewhere (BuildLongFaceInsets,
        // BuildEndDoors). Drawing them here too was an exactly-coincident redundant
        // layer, not a partial one — hence flicker on all 6 faces, not just some.
        foreach (var chamfer in box.EdgeChamfers)
            AddFaceToMesh(mesh, box.Vertices, chamfer, color);
        foreach (var tri in box.CornerTriangles)
            AddFaceToMesh(mesh, box.Vertices, tri, color);
    }

    private static void AddFaceToMesh(StationModuleMesh mesh, Vector3[] verts, ChamferedBox.Face face, Color color)
    {
        if (face.Indices.Length == 4)
            mesh.AddQuad(verts[face.Indices[0]], verts[face.Indices[1]],
                         verts[face.Indices[2]], verts[face.Indices[3]], color);
        else
            mesh.AddTriangle(verts[face.Indices[0]], verts[face.Indices[1]], verts[face.Indices[2]], color);
    }

    // ── Fasteners ─────────────────────────────────────────────────────────────

    private static void BuildFasteners(StationModuleMesh mesh, Color color)
    {
        Color surroundColor = DarkenColor(color, 0.70f);
        Color innerColor    = DarkenColor(color, 0.45f);

        float x1 = -HLx + Lx / 3f;
        float x2 = -HLx + 2f * Lx / 3f;

        // 4 long edges × 2 fasteners each
        foreach (float x in new[] { x1, x2 })
        {
            AddLongEdgeFastener(mesh, x, +HLy, +HLz, surroundColor, innerColor);
            AddLongEdgeFastener(mesh, x, +HLy, -HLz, surroundColor, innerColor);
            AddLongEdgeFastener(mesh, x, -HLy, +HLz, surroundColor, innerColor);
            AddLongEdgeFastener(mesh, x, -HLy, -HLz, surroundColor, innerColor);
        }

        // 8 short edges × 1 fastener at midpoint
        AddEndEdgeFastener(mesh, +HLx, +HLy, 0f,    surroundColor, innerColor);
        AddEndEdgeFastener(mesh, +HLx, -HLy, 0f,    surroundColor, innerColor);
        AddEndEdgeFastener(mesh, +HLx, 0f,   +HLz,  surroundColor, innerColor);
        AddEndEdgeFastener(mesh, +HLx, 0f,   -HLz,  surroundColor, innerColor);
        AddEndEdgeFastener(mesh, -HLx, +HLy, 0f,    surroundColor, innerColor);
        AddEndEdgeFastener(mesh, -HLx, -HLy, 0f,    surroundColor, innerColor);
        AddEndEdgeFastener(mesh, -HLx, 0f,   +HLz,  surroundColor, innerColor);
        AddEndEdgeFastener(mesh, -HLx, 0f,   -HLz,  surroundColor, innerColor);
    }

    private static void AddLongEdgeFastener(StationModuleMesh mesh,
        float x, float yCorner, float zCorner, Color surroundColor, Color innerColor)
    {
        float sy = MathF.Sign(yCorner), sz = MathF.Sign(zCorner);
        // Centre of the chamfer strip in YZ: midway between inner and outer edge
        float yc = yCorner - sy * Chamfer * 0.5f;
        float zc = zCorner - sz * Chamfer * 0.5f;

        const float FW = 0.10f, FH = 0.08f, FD = 0.015f;
        const float SurroundRaise = 0.005f; // pulls the surround off the chamfer strip's plane

        var normal = Vector3.Normalize(new Vector3(0f, sy, sz));
        var right  = Vector3.UnitX;
        var up     = Vector3.Cross(right, normal);
        var centre = new Vector3(x, yc, zc);

        AddFastenerQuads(mesh, centre, normal, right, up, FW, FH, FD, SurroundRaise,
            surroundColor, innerColor);
    }

    private static void AddEndEdgeFastener(StationModuleMesh mesh,
        float x, float yCorner, float zCorner, Color surroundColor, Color innerColor)
    {
        float xn = MathF.Sign(x);
        // The short chamfer strip this fastener sits on tilts from the true end-face
        // corner (x = xn*HLx) inward to the long-face corner (x = xn*(HLx-Chamfer)) —
        // its own X extent, not a single X value. Same "pull back from the corner by
        // half the chamfer" treatment already applied to yc/zc below, just along X:
        // without it the fastener sits flush with the door edge instead of centred on
        // the strip.
        float xc = x - xn * Chamfer * 0.5f;
        bool  isZEdge = MathF.Abs(yCorner) < 0.01f;
        bool  isYEdge = MathF.Abs(zCorner) < 0.01f;

        float yc, zc;
        Vector3 normal, right;
        if (isZEdge)
        {
            float sz = MathF.Sign(zCorner);
            zc = zCorner - sz * Chamfer * 0.5f;
            yc = 0f;
            normal = Vector3.Normalize(new Vector3(xn, 0f, sz));
            right  = Vector3.UnitY;
        }
        else
        {
            float sy = MathF.Sign(yCorner);
            yc = yCorner - sy * Chamfer * 0.5f;
            zc = 0f;
            normal = Vector3.Normalize(new Vector3(xn, sy, 0f));
            right  = Vector3.UnitZ;
        }

        const float FW = 0.08f, FH = 0.08f, FD = 0.015f;
        const float SurroundRaise = 0.005f;
        var up     = Vector3.Cross(right, normal);
        var centre = new Vector3(xc, yc, zc);

        AddFastenerQuads(mesh, centre, normal, right, up, FW, FH, FD, SurroundRaise,
            surroundColor, innerColor);
    }

    // Shared by both fastener types — builds the surround, inner, and recess-wall
    // geometry explicitly from right/up, avoiding the (center, normal, up, width,
    // height) convenience overload's easy-to-misread parameter order (its third
    // argument is "up", not "right" — passing right into that slot silently swaps
    // which axis becomes width vs height, and for end-chamfer fasteners the
    // resulting direction has no relationship to the actual geometry at all).
    private static void AddFastenerQuads(StationModuleMesh mesh,
        Vector3 centre, Vector3 normal, Vector3 right, Vector3 up,
        float width, float height, float depth, float surroundRaise,
        Color surroundColor, Color innerColor)
    {
        Vector3 outerCentre = centre + normal * surroundRaise;
        Vector3 innerCentre = centre - normal * depth;

        // Winding: with up = Cross(right, normal), Cross(right, up) = -normal (up and
        // right are perpendicular unit vectors, so this reduces to the vector triple
        // product identity Cross(A, Cross(A, N)) = -N). A "right then up" (BL,BR,TR,TL)
        // vertex order gives a face normal of Cross(right, up) = -normal — inward-facing,
        // hence invisible from outside. "Up then right" (BL,TL,TR,BR) gives Cross(up,
        // right) = +normal instead, matching the intended outward-facing surface.
        Vector3 hwOuter = right * ((width  + 0.04f) * 0.5f);
        Vector3 hhOuter = up    * ((height + 0.04f) * 0.5f);
        mesh.AddQuad(outerCentre - hwOuter - hhOuter, outerCentre - hwOuter + hhOuter,
                     outerCentre + hwOuter + hhOuter, outerCentre + hwOuter - hhOuter,
                     surroundColor);

        Vector3 hwInner = right * (width  * 0.5f);
        Vector3 hhInner = up    * (height * 0.5f);
        mesh.AddQuad(innerCentre - hwInner - hhInner, innerCentre - hwInner + hhInner,
                     innerCentre + hwInner + hhInner, innerCentre + hwInner - hhInner,
                     innerColor);

        AddRecessWalls(mesh, centre, innerCentre, normal, right, width, height, innerColor);
    }

    private static void AddRecessWalls(StationModuleMesh mesh,
        Vector3 outer, Vector3 inner,
        Vector3 normal, Vector3 right,
        float width, float height, Color color)
    {
        var up = Vector3.Cross(right, normal);
        var hw = right * width  * 0.5f;
        var hh = up    * height * 0.5f;

        // Top
        mesh.AddQuad(outer + hh - hw, outer + hh + hw, inner + hh + hw, inner + hh - hw, color);
        // Bottom
        mesh.AddQuad(outer - hh + hw, outer - hh - hw, inner - hh - hw, inner - hh + hw, color);
        // Left
        mesh.AddQuad(outer - hw - hh, outer - hw + hh, inner - hw + hh, inner - hw - hh, color);
        // Right
        mesh.AddQuad(outer + hw + hh, outer + hw - hh, inner + hw - hh, inner + hw + hh, color);
    }

    // ── Long face inset pattern ───────────────────────────────────────────────

    private static void BuildLongFaceInsets(StationModuleMesh mesh, Color color, int seed)
    {
        var rng   = new SeededRandom(seed);
        int   cols  = rng.NextInt(1, 4);
        int   rows  = rng.NextInt(1, 8);
        float depth = rng.NextFloat(0.03f, 0.05f);
        float groove = rng.NextFloat(0.01f, 0.03f);

        Color wallColor  = DarkenColor(color, 0.75f);
        Color floorColor = DarkenColor(color, 0.82f);

        // Y+/Y- faces carry the manufacturer text (see AddContainerText), raised only
        // ~1 cm above the surface — the inset grid there z-fights with the text quads.
        // Good enough for now: skip the inset pattern on these two faces and use a
        // single flat panel instead. A proper fix needs the inset layout and text
        // placement to share one design. Z+/Z- keep the inset pattern unchanged.
        AddFlatRect(mesh, color, Vector3.UnitY, Vector3.UnitX, -Vector3.UnitZ,
            new Vector3(0, HLy, 0), FaceLengthAfterChamfer, FaceWidthAfterChamfer);
        AddFlatRect(mesh, color, -Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ,
            new Vector3(0, -HLy, 0), FaceLengthAfterChamfer, FaceWidthAfterChamfer);

        // Z+ face: normal +Z, long axis −X, cross axis +Y
        BuildFaceInsets(mesh, color, wallColor, floorColor,
            origin:    new Vector3(0, 0, HLz),
            normal:    Vector3.UnitZ,
            longAxis:  -Vector3.UnitX,
            crossAxis: Vector3.UnitY,
            insetDir:  -Vector3.UnitZ,
            cols, rows, depth, groove);

        // Z- face: normal −Z, long axis +X, cross axis +Y
        BuildFaceInsets(mesh, color, wallColor, floorColor,
            origin:    new Vector3(0, 0, -HLz),
            normal:    -Vector3.UnitZ,
            longAxis:  Vector3.UnitX,
            crossAxis: Vector3.UnitY,
            insetDir:  Vector3.UnitZ,
            cols, rows, depth, groove);
    }

    private static void BuildFaceInsets(StationModuleMesh mesh,
        Color surface, Color wall, Color floor,
        Vector3 origin, Vector3 normal, Vector3 longAxis, Vector3 crossAxis, Vector3 insetDir,
        int cols, int rows, float depth, float groove)
    {
        float faceWidth = FaceWidthAfterChamfer; // 2.1 m
        float zoneLen   = InsetZoneHalfLen * 2f;  // 4.0 m

        // Plain end zones
        AddFlatRect(mesh, surface, normal, longAxis, crossAxis,
            origin - longAxis * (HLxAfterChamfer - PlainEndLen * 0.5f), PlainEndLen, faceWidth);
        AddFlatRect(mesh, surface, normal, longAxis, crossAxis,
            origin + longAxis * (HLxAfterChamfer - PlainEndLen * 0.5f), PlainEndLen, faceWidth);

        // Inset zone border grooves (4 thin flat strips around the zone)
        float borderX = zoneLen * 0.5f - groove * 0.5f;
        float borderW = faceWidth * 0.5f - groove * 0.5f;
        AddFlatRect(mesh, surface, normal, longAxis, crossAxis,
            origin - longAxis * borderX, groove, faceWidth);
        AddFlatRect(mesh, surface, normal, longAxis, crossAxis,
            origin + longAxis * borderX, groove, faceWidth);
        AddFlatRect(mesh, surface, normal, longAxis, crossAxis,
            origin - crossAxis * borderW, zoneLen, groove);
        AddFlatRect(mesh, surface, normal, longAxis, crossAxis,
            origin + crossAxis * borderW, zoneLen, groove);

        // Body inside the border
        float bodyLen   = zoneLen   - groove * 2f;
        float bodyWidth = faceWidth - groove * 2f;

        float cellLen   = (bodyLen   - groove * (rows - 1)) / rows;
        float cellWidth = (bodyWidth - groove * (cols - 1)) / cols;

        Vector3 bodyStart = origin
            - longAxis  * bodyLen   * 0.5f
            - crossAxis * bodyWidth * 0.5f;

        for (int row = 0; row < rows; row++)
        {
            float rowOff = row * (cellLen + groove) + cellLen * 0.5f;
            for (int col = 0; col < cols; col++)
            {
                float colOff = col * (cellWidth + groove) + cellWidth * 0.5f;
                Vector3 cc = bodyStart + longAxis * rowOff + crossAxis * colOff;
                Vector3 fc = cc + insetDir * depth; // floor centre

                // Inset floor
                AddFlatRect(mesh, floor, normal, longAxis, crossAxis, fc, cellLen, cellWidth);

                // Four inset walls
                AddRecessWalls(mesh, cc, fc, normal, crossAxis, cellWidth, cellLen, wall);

                // Inter-row ridge
                if (row < rows - 1)
                    AddFlatRect(mesh, surface, normal, longAxis, crossAxis,
                        bodyStart + longAxis * (rowOff + cellLen * 0.5f + groove * 0.5f)
                                  + crossAxis * colOff,
                        groove, cellWidth);

                // Inter-col ridge
                if (col < cols - 1)
                    AddFlatRect(mesh, surface, normal, longAxis, crossAxis,
                        bodyStart + longAxis * rowOff
                                  + crossAxis * (colOff + cellWidth * 0.5f + groove * 0.5f),
                        cellLen, groove);
            }
        }
    }

    // Flat rectangle on a surface. Winding derived from cross(longAxis, crossAxis).
    private static void AddFlatRect(StationModuleMesh mesh, Color color,
        Vector3 normal, Vector3 longAxis, Vector3 crossAxis,
        Vector3 centre, float lenSize, float widthSize)
    {
        var hl = longAxis  * lenSize   * 0.5f;
        var hw = crossAxis * widthSize * 0.5f;
        var n  = Vector3.Cross(longAxis, crossAxis);
        if (Vector3.Dot(n, normal) > 0)
            mesh.AddQuad(centre - hl - hw, centre + hl - hw, centre + hl + hw, centre - hl + hw, color);
        else
            mesh.AddQuad(centre - hl + hw, centre + hl + hw, centre + hl - hw, centre - hl - hw, color);
    }

    // ── End face doors ────────────────────────────────────────────────────────

    private static void BuildEndDoors(StationModuleMesh mesh, Color color)
    {
        Color panel  = new Color((byte)(color.R * 0.92f), (byte)(color.G * 0.92f), (byte)(color.B * 0.92f));
        Color latch  = new Color(
            (byte)Math.Min(color.R * 0.50f + 90, 255),
            (byte)Math.Min(color.G * 0.50f + 80, 255),
            (byte)Math.Min(color.B * 0.50f + 60, 255));
        Color hinge  = DarkenColor(color, 0.45f);
        Color gap    = DarkenColor(color, 0.25f);

        BuildEndDoor(mesh, panel, latch, hinge, gap, +HLx);
        BuildEndDoor(mesh, panel, latch, hinge, gap, -HLx);
    }

    private static void BuildEndDoor(StationModuleMesh mesh,
        Color panel, Color latch, Color hinge, Color gap, float x)
    {
        float xn  = MathF.Sign(x);
        float ny  = HLy - Chamfer;
        float nz  = HLz - Chamfer;
        float gapW = 0.025f;
        float panW = nz - gapW * 0.5f;
        float recess = 0.015f;
        float barDp  = 0.020f + recess;

        var normal = new Vector3(xn, 0f, 0f);
        var up     = Vector3.UnitY;
        var right  = xn > 0 ? Vector3.UnitZ : -Vector3.UnitZ;

        Vector3 leftCtr  = new Vector3(x - xn * recess, 0f, -panW * 0.5f - gapW * 0.25f);
        Vector3 rightCtr = new Vector3(x - xn * recess, 0f,  panW * 0.5f + gapW * 0.25f);

        // Panels
        mesh.AddQuad(leftCtr,  normal, up, panW, ny * 2f, panel);
        mesh.AddQuad(rightCtr, normal, up, panW, ny * 2f, panel);

        // Centre gap
        mesh.AddQuad(new Vector3(x, 0f, 0f), normal, up, gapW, ny * 2f, gap);

        // Latch bars (upper and lower on each panel)
        float barLen = panW * 0.65f;
        float barH   = 0.05f;
        foreach (var ctr in new[] { leftCtr, rightCtr })
        {
            mesh.AddQuad(ctr + new Vector3(xn * barDp,  ny * 0.55f, 0f),
                         normal, right, barLen, barH, latch);
            mesh.AddQuad(ctr + new Vector3(xn * barDp, -ny * 0.55f, 0f),
                         normal, right, barLen, barH, latch);
        }

        // Hinge strips on outer edges
        float hingeW = 0.055f;
        mesh.AddQuad(leftCtr  - right * (panW * 0.5f - hingeW * 0.5f) + new Vector3(xn * barDp, 0f, 0f),
                     normal, up, hingeW, ny * 1.8f, hinge);
        mesh.AddQuad(rightCtr + right * (panW * 0.5f - hingeW * 0.5f) + new Vector3(xn * barDp, 0f, 0f),
                     normal, up, hingeW, ny * 1.8f, hinge);

        // Central latch handle
        mesh.AddQuad(new Vector3(x + xn * (recess + 0.025f), ny * 0.1f, 0f),
                     normal, up, 0.045f, 0.18f, latch);
    }

    // ── Text ─────────────────────────────────────────────────────────────────

    private static void AddContainerText(StationModuleMesh mesh, string text)
    {
        // Bright contrast text on Y+ and Y- faces
        Color textColor = new Color(230, 225, 200);

        int   charCount = Math.Max(1, text.Length);
        float pixelSize = Math.Clamp(
            InsetZoneHalfLen * 2f * 0.85f / (charCount * (BitmapFonts.CharW + 1)),
            0.018f, 0.080f);

        float textW = charCount * (BitmapFonts.CharW + 1) * pixelSize;
        float textH = BitmapFonts.CharH * pixelSize;
        float raise = 0.012f;

        // Y+ face: text origin at lower quarter, centred in X
        // Face width (Z direction) = FaceWidthAfterChamfer; lower = toward +Z (viewer side)
        float zOffset = -(FaceWidthAfterChamfer * 0.5f - textH * 1.5f);
        var originYPlus = new Vector3(-textW * 0.5f, HLy + raise, zOffset);
        AddTextGeometry(mesh, text, originYPlus,
            textRight: Vector3.UnitX, textUp: -Vector3.UnitZ, textNormal: Vector3.UnitY,
            pixelSize, textColor);

        // Y- face: mirror image (text reads correctly from below)
        var originYMinus = new Vector3(textW * 0.5f, -(HLy + raise), zOffset);
        AddTextGeometry(mesh, text, originYMinus,
            textRight: -Vector3.UnitX, textUp: -Vector3.UnitZ, textNormal: -Vector3.UnitY,
            pixelSize, textColor);
    }

    // internal: reused by StationDecorator for the docking-bay's door signage — same
    // per-pixel bitmap-font geometry technique, no need for a second implementation.
    internal static void AddTextGeometry(StationModuleMesh mesh,
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
                float py = (BitmapFonts.CharH - row - 0.5f) * pixelSize;
                mesh.AddQuad(origin + textRight * px + textUp * py,
                             textNormal, textUp, pixelSize * 0.88f, pixelSize * 0.88f, textColor);
            }
            cx += (BitmapFonts.CharW + 1) * pixelSize;
        }
    }

    // ── Wear ─────────────────────────────────────────────────────────────────

    private static void ApplyWear(StationModuleMesh mesh, float wear, int seed)
    {
        if (wear < 0.2f) return;

        // APPROXIMATE, NOT REDESIGNED (flagged, not fixed, per the container-chamfer
        // brief): BuildChamferedBox used to emit a contiguous "main faces" block
        // (indices 0-5) that a mainMul multiplier targeted here. It no longer does —
        // main-face coverage now comes entirely from BuildFasteners / BuildLongFaceInsets
        // / BuildEndDoors / AddContainerText, interleaved with each other and with no
        // contiguous index range left to call "main" faces. Dropped mainMul entirely
        // rather than guess a wrong target. Hardcoded face-index wear targeting is
        // fragile in general — a proper fix would have each Build* function tag which
        // face indices it added, so ApplyWear can target semantic groups instead of
        // guessing numbers.
        float edgeMul = wear < 0.5f ? 1.05f : wear < 0.8f ? 1.10f : 1.15f;

        int faceCount = mesh.FaceCount;

        // Edge chamfers + corner triangles: BuildChamferedBox emits exactly these first
        // now (12 edge-chamfer quads + 8 corner triangles = 20 faces), starting at index 0.
        for (int f = 0; f < Math.Min(20, faceCount); f++) mesh.MultiplyFaceColor(f, edgeMul);

        if (wear >= 0.8f)
        {
            var rng = new SeededRandom(seed + 1);
            int streaks = rng.NextInt(2, 5);
            for (int s = 0; s < streaks; s++)
            {
                int f = rng.NextInt(0, 5);
                if (f < faceCount) mesh.MultiplyFaceColor(f, 0.72f);
            }
        }
    }

    // ── Utility ───────────────────────────────────────────────────────────────

    private static Color DarkenColor(Color c, float f) =>
        new((byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f), c.A);
}
