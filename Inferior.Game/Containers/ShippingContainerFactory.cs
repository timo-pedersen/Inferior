using Inferior.Core.Math;
using Inferior.Core.Random;
using Inferior.Game.StationGen;
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

    private const float InsetZoneHalfLen    = 2.0f;
    private const float PlainEndLen         = HLx - InsetZoneHalfLen; // 1.0 m
    private const float FaceWidthAfterChamfer = Ly - 2f * Chamfer;   // 2.1 m

    // ── Public API ────────────────────────────────────────────────────────────

    public static ShippingContainer Generate(
        Color color,
        float wear,
        int sidePatternSeed,
        string? text = null)
    {
        string manufacturerText = text ?? GenerateManufacturerName(sidePatternSeed);

        var mesh = new StationModuleMesh { Texture = SurfaceTexture.CleanPanel };

        BuildChamferedBox      (mesh, color);
        BuildFasteners         (mesh, color);
        BuildLongFaceInsets    (mesh, color, sidePatternSeed);
        BuildEndDoors          (mesh, color);
        AddContainerText       (mesh, manufacturerText);

        mesh.ApplyLighting(Matrix.Identity,
            SceneLighting.SunDirection,
            SceneLighting.Ambient,
            SceneLighting.SunColour);

        ApplyWear(mesh, wear, sidePatternSeed);

        var (verts, indices) = mesh.ToArrays();

        return new ShippingContainer
        {
            Id               = $"CTR-{(uint)sidePatternSeed:X8}",
            PrimaryColor     = color,
            Wear             = wear,
            SidePatternSeed  = sidePatternSeed,
            ManufacturerText = manufacturerText,
            Lock             = LockGrade.Civilian,
            IsLocked         = false,
            WorldPosition    = DVec3.Zero,
            Orientation      = Quaternion.Identity,
            Vertices         = verts,
            Indices          = indices,
        };
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
    // Container centred at origin. Long axis = X.
    // Winding: CW from outside (same convention as StationModuleMesh.AddOrientedBox).

    private static void BuildChamferedBox(StationModuleMesh mesh, Color color)
    {
        float c  = Chamfer;
        float hx = HLx, hy = HLy, hz = HLz;

        // 4 long main faces
        AddMainFaceY(mesh, color, +hy, hx, hz - c, inward: false);
        AddMainFaceY(mesh, color, -hy, hx, hz - c, inward: true);
        AddMainFaceZ(mesh, color, +hz, hx, hy - c, inward: false);
        AddMainFaceZ(mesh, color, -hz, hx, hy - c, inward: true);

        // 2 end main faces
        AddMainFaceX(mesh, color, +hx, hy - c, hz - c, inward: false);
        AddMainFaceX(mesh, color, -hx, hy - c, hz - c, inward: true);

        // 4 long chamfer strips (run full length along X)
        AddLongChamfer(mesh, color, +hy, +hz);
        AddLongChamfer(mesh, color, +hy, -hz);
        AddLongChamfer(mesh, color, -hy, +hz);
        AddLongChamfer(mesh, color, -hy, -hz);

        // 8 end chamfer strips on X+ face corners
        AddEndChamfer(mesh, color, +hx, +hy, +hz);
        AddEndChamfer(mesh, color, +hx, +hy, -hz);
        AddEndChamfer(mesh, color, +hx, -hy, +hz);
        AddEndChamfer(mesh, color, +hx, -hy, -hz);
        // 8 end chamfer strips on X- face corners
        AddEndChamfer(mesh, color, -hx, +hy, +hz);
        AddEndChamfer(mesh, color, -hx, +hy, -hz);
        AddEndChamfer(mesh, color, -hx, -hy, +hz);
        AddEndChamfer(mesh, color, -hx, -hy, -hz);

        // 8 corner triangles
        AddCornerTri(mesh, color, +hx, +hy, +hz);
        AddCornerTri(mesh, color, +hx, +hy, -hz);
        AddCornerTri(mesh, color, +hx, -hy, +hz);
        AddCornerTri(mesh, color, +hx, -hy, -hz);
        AddCornerTri(mesh, color, -hx, +hy, +hz);
        AddCornerTri(mesh, color, -hx, +hy, -hz);
        AddCornerTri(mesh, color, -hx, -hy, +hz);
        AddCornerTri(mesh, color, -hx, -hy, -hz);
    }

    // Y+/-  main face. inward=false → +Y face (normal +Y), inward=true → -Y (normal -Y).
    private static void AddMainFaceY(StationModuleMesh mesh, Color c,
        float y, float hx, float hzTrim, bool inward)
    {
        float x0 = -hx, x1 = hx, z0 = -hzTrim, z1 = hzTrim;
        if (!inward)
            mesh.AddQuad(new(x0,y,z1), new(x1,y,z1), new(x1,y,z0), new(x0,y,z0), c);
        else
            mesh.AddQuad(new(x0,y,z0), new(x1,y,z0), new(x1,y,z1), new(x0,y,z1), c);
    }

    // Z+/- main face.
    private static void AddMainFaceZ(StationModuleMesh mesh, Color c,
        float z, float hx, float hyTrim, bool inward)
    {
        float x0 = -hx, x1 = hx, y0 = -hyTrim, y1 = hyTrim;
        if (!inward) // +Z, normal +Z: CW from +Z side
            mesh.AddQuad(new(x1,y0,z), new(x0,y0,z), new(x0,y1,z), new(x1,y1,z), c);
        else         // -Z, normal -Z
            mesh.AddQuad(new(x0,y0,z), new(x1,y0,z), new(x1,y1,z), new(x0,y1,z), c);
    }

    // X+/- end face.
    private static void AddMainFaceX(StationModuleMesh mesh, Color c,
        float x, float hyTrim, float hzTrim, bool inward)
    {
        float y0 = -hyTrim, y1 = hyTrim, z0 = -hzTrim, z1 = hzTrim;
        if (!inward) // +X, normal +X
            mesh.AddQuad(new(x,y0,z0), new(x,y0,z1), new(x,y1,z1), new(x,y1,z0), c);
        else         // -X, normal -X
            mesh.AddQuad(new(x,y0,z1), new(x,y0,z0), new(x,y1,z0), new(x,y1,z1), c);
    }

    // Long chamfer strip. Runs full X length. Corner at (yCorner, zCorner).
    // Inner edge is at (yCorner - sign(yCorner)*Chamfer, zCorner) and
    // (yCorner, zCorner - sign(zCorner)*Chamfer).
    private static void AddLongChamfer(StationModuleMesh mesh, Color color,
        float yCorner, float zCorner)
    {
        float sy = MathF.Sign(yCorner), sz = MathF.Sign(zCorner);
        float yi = yCorner - sy * Chamfer; // inner Y (on main face plane)
        float zi = zCorner - sz * Chamfer; // inner Z
        float x0 = -HLx, x1 = HLx;

        // Quad: inner-left, outer-left, outer-right, inner-right
        // "inner" = where the chamfer meets the main face; "outer" = the chamfer's far edge
        // Outward normal points diagonally (sy, sz) in YZ plane
        // CW from outside: we need to ensure cross(v1-v0, v2-v0) points outward
        var a = new Vector3(x0, yi, zCorner - sz * Chamfer); // inner bottom-left  (on chamfer)
        var b = new Vector3(x1, yi, zCorner - sz * Chamfer); // inner bottom-right
        var c2 = new Vector3(x1, yCorner - sy * Chamfer, zi); // inner top-right
        var d = new Vector3(x0, yCorner - sy * Chamfer, zi); // inner top-left

        // Wait, the strip is a flat quad between two parallel lines.
        // One line: (x, yCorner-c, zCorner-c) across x  — the "outer" edge where all three faces meet
        // Other line: where the chamfer meets the two main faces.
        // The chamfer strip is actually triangular in cross-section (two parallel edges, both on the 45° plane).
        //
        // The chamfer is flat — a simple quad:
        // outer edge (the bevelled corner): y = yCorner-sy*c, z = zCorner-sz*c  (this IS the strip)
        // inner edges: one at y = yCorner-sy*c ON the Z-face, one at z = zCorner-sz*c ON the Y-face.
        //
        // So strip is: from (x, yi, zCorner-sz*c) to (x, yCorner-sy*c, zi) across the entire X length.

        var v00 = new Vector3(x0, yi,                  zCorner - sz * Chamfer);
        var v10 = new Vector3(x1, yi,                  zCorner - sz * Chamfer);
        var v11 = new Vector3(x1, yCorner - sy * Chamfer, zi);
        var v01 = new Vector3(x0, yCorner - sy * Chamfer, zi);

        // Normal = normalize(sy, sz, 0) projected outward.
        // Cross(v10-v00, v01-v00) should equal outward normal.
        // v10-v00 = (Lx, 0, 0); v01-v00 = (0, sy*c, -sz*c)
        // cross = (0*(0)-0*(-sz*c), 0*Lx - Lx*(-sz*c), Lx*(sy*c) - 0*Lx)
        //       = (0, Lx*sz*c, Lx*sy*c)  — that's (0, sz, sy) direction.
        // We want (0, sy, sz). So if sy and sz are both +, cross gives (0, sz, sy) = (0,+,+) ✓
        // if sy=+1, sz=-1: cross gives (0, -1, +1) — that's pointing inward for the Y+/Z- corner.
        // Hmm, let me reconsider the winding.

        // For Y+/Z+ corner: outward normal is (0,+,+). Cross(v10-v00, v01-v00) = (0,+c*sz,+c*sy)
        // With sy=sz=+1: (0, c, c) → direction (0,1,1) ✓
        // For Y+/Z- corner: sy=+1,sz=-1: cross gives (0, c*(-1), c*(+1)) = (0,-c,c) → direction (0,-1,1)
        // But we want outward = (0,+1,-1). So cross gives INWARD.
        // Need to flip winding for Y+/Z- and Y-/Z+ cases.

        if (sy * sz > 0) // both same sign: (++), (--) → cross product already outward
            mesh.AddQuad(v00, v10, v11, v01, color);
        else             // opposite signs: (+−), (−+) → flip
            mesh.AddQuad(v01, v11, v10, v00, color);
    }

    // End-face chamfer strip for the X end face.
    // Creates a quad on the 45° chamfer of the end face edge.
    private static void AddEndChamfer(StationModuleMesh mesh, Color color,
        float x, float yCorner, float zCorner)
    {
        float sy = MathF.Sign(yCorner), sz = MathF.Sign(zCorner);
        float xn = MathF.Sign(x);

        float yChamf = yCorner - sy * Chamfer;
        float zChamf = zCorner - sz * Chamfer;

        // The end chamfer strip is the quad between:
        // (x, yChamf, zCorner-sz*c) and (x, yCorner-sy*c, zChamf)
        // This is a small quad on the end face at the corner.

        var v0 = new Vector3(x, yChamf,          zCorner - sz * Chamfer);
        var v1 = new Vector3(x, yCorner - sy * Chamfer, zChamf);

        // Outward normal has a component in the X direction and 45° in YZ.
        // Two triangles, using these two edge vertices and the outer corner.
        // Actually the end chamfer strip is just two verts meeting the long strip's end cap.
        // Simplification: treat as single quad between inner edges of adjacent strips.
        var v2 = new Vector3(x, yChamf,          zChamf);  // the shared chamfer corner

        // Wind CW from outside. For X+ corner (xn=+1, sy=+1, sz=+1):
        // Outward normal ~ (+1,+1,+1), normalised.
        // Cross(v1-v0, v2-v0) should point outward.
        // v1-v0 = (0, sy*c, -sz*c); v2-v0 = (0, 0, -sz*c + sz*c)... wait v2=(x,yChamf,zChamf)
        // Hmm these might be degenerate. Let me pick better.

        // The end chamfer quad has 4 verts:
        // - on end face, at (x, yChamf, zChamf) - the chamfer outer corner
        // - long chamfer strip ends at x: (x, yChamf, zCorner-sz*c) and (x, yCorner-sy*c, zChamf)
        // - on end face plain surface: (x, yChamf, zChamf) — same as outer corner
        // Actually: the end chamfer strip is the quad between:
        //   (x, yChamf, zCorner-sz*c)  → edge where end chamfer meets Z-adjacent long strip
        //   (x, yCorner-sy*c, zChamf)  → edge where end chamfer meets Y-adjacent long strip
        // But these two points plus the outer corner form a triangle, not a quad.
        // So end chamfers are TRIANGLES:

        // Use the triangle: v0, v1, outer_corner
        var outerCorner = new Vector3(x, yChamf, zChamf);

        if (xn > 0)
        {
            if (sy * sz > 0)
                mesh.AddTriangle(v0, outerCorner, v1, color);
            else
                mesh.AddTriangle(v0, v1, outerCorner, color);
        }
        else
        {
            if (sy * sz > 0)
                mesh.AddTriangle(v0, v1, outerCorner, color);
            else
                mesh.AddTriangle(v0, outerCorner, v1, color);
        }
    }

    // Corner triangle: fills the gap at box vertex where three chamfer strips meet.
    private static void AddCornerTri(StationModuleMesh mesh, Color color,
        float cx, float cy, float cz)
    {
        float sx = MathF.Sign(cx), sy = MathF.Sign(cy), sz = MathF.Sign(cz);

        // The three vertices lie on the chamfer-inset edges of adjacent strips
        var vX = new Vector3(cx,              cy - sy * Chamfer, cz - sz * Chamfer);
        var vY = new Vector3(cx - sx * Chamfer, cy,              cz - sz * Chamfer);
        var vZ = new Vector3(cx - sx * Chamfer, cy - sy * Chamfer, cz);

        // Outward normal = normalise(sx, sy, sz).
        // Wind CW from outside: cross(v1-v0, v2-v0) must match (sx, sy, sz).
        // For all-positive: cross(vY-vX, vZ-vX) = cross((-c,c,0),(-c,0,c))
        //   = (c*c - 0, 0 - (-c*c), 0 - (-c*c)) = (c², c², c²) → direction (1,1,1) ✓
        // For (+,+,-): outward = (1,1,-1).
        //   vX=(cx, cy-c, cz+c), vY=(cx-c, cy, cz+c), vZ=(cx-c, cy-c, cz)
        //   cross(vY-vX, vZ-vX) = cross((-c,c,0),(-c,0,-c))
        //   = (c*(-c)-0*0, 0*(-c)-(-c)*(-c), (-c)*0-c*(-c)) = (-c²,-c²,c²) → (-1,-1,1) WRONG.
        // Need to swap: wind as vX, vZ, vY for that case.
        // Pattern: when sx*sy*sz < 0, swap vY and vZ.
        if (sx * sy * sz > 0)
            mesh.AddTriangle(vX, vY, vZ, color);
        else
            mesh.AddTriangle(vX, vZ, vY, color);
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

        var  normal = Vector3.Normalize(new Vector3(0f, sy, sz));
        var  right  = Vector3.UnitX;
        var  up     = Vector3.Normalize(Vector3.Cross(right, normal));
        var  centre = new Vector3(x, yc, zc);

        mesh.AddQuad(centre,                normal, right, FW + 0.04f, FH + 0.04f, surroundColor);
        mesh.AddQuad(centre - normal * FD,  normal, right, FW,         FH,         innerColor);
        AddRecessWalls(mesh, centre, centre - normal * FD, normal, right, FW, FH, innerColor);
    }

    private static void AddEndEdgeFastener(StationModuleMesh mesh,
        float x, float yCorner, float zCorner, Color surroundColor, Color innerColor)
    {
        float xn = MathF.Sign(x);
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
        var centre = new Vector3(x, yc, zc);

        mesh.AddQuad(centre,               normal, right, FW + 0.04f, FH + 0.04f, surroundColor);
        mesh.AddQuad(centre - normal * FD, normal, right, FW,         FH,         innerColor);
        AddRecessWalls(mesh, centre, centre - normal * FD, normal, right, FW, FH, innerColor);
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

        // Y+ face: normal +Y, long axis +X, cross axis −Z (so "cross" runs from back to front = −Z)
        BuildFaceInsets(mesh, color, wallColor, floorColor,
            origin:    new Vector3(0, HLy, 0),
            normal:    Vector3.UnitY,
            longAxis:  Vector3.UnitX,
            crossAxis: -Vector3.UnitZ,
            insetDir:  -Vector3.UnitY,
            cols, rows, depth, groove);

        // Y- face: normal -Y, long axis +X, cross axis +Z
        BuildFaceInsets(mesh, color, wallColor, floorColor,
            origin:    new Vector3(0, -HLy, 0),
            normal:    -Vector3.UnitY,
            longAxis:  Vector3.UnitX,
            crossAxis: Vector3.UnitZ,
            insetDir:  Vector3.UnitY,
            cols, rows, depth, groove);

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
            origin - longAxis * (HLx - PlainEndLen * 0.5f), PlainEndLen, faceWidth);
        AddFlatRect(mesh, surface, normal, longAxis, crossAxis,
            origin + longAxis * (HLx - PlainEndLen * 0.5f), PlainEndLen, faceWidth);

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

                // Four inset walls (N/S in longAxis direction, E/W in crossAxis direction)
                float hl = cellLen   * 0.5f;
                float hw = cellWidth * 0.5f;

                // +longAxis wall (S face of cell)
                mesh.AddQuad(
                    cc + longAxis * hl - crossAxis * hw,
                    cc + longAxis * hl + crossAxis * hw,
                    fc + longAxis * hl + crossAxis * hw,
                    fc + longAxis * hl - crossAxis * hw, wall);

                // -longAxis wall (N face)
                mesh.AddQuad(
                    cc - longAxis * hl + crossAxis * hw,
                    cc - longAxis * hl - crossAxis * hw,
                    fc - longAxis * hl - crossAxis * hw,
                    fc - longAxis * hl + crossAxis * hw, wall);

                // +crossAxis wall (E)
                mesh.AddQuad(
                    cc + crossAxis * hw + longAxis * hl,
                    cc + crossAxis * hw - longAxis * hl,
                    fc + crossAxis * hw - longAxis * hl,
                    fc + crossAxis * hw + longAxis * hl, wall);

                // -crossAxis wall (W)
                mesh.AddQuad(
                    cc - crossAxis * hw - longAxis * hl,
                    cc - crossAxis * hw + longAxis * hl,
                    fc - crossAxis * hw + longAxis * hl,
                    fc - crossAxis * hw - longAxis * hl, wall);

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

        float mainMul, edgeMul;
        if      (wear < 0.5f) { mainMul = 0.92f; edgeMul = 1.05f; }
        else if (wear < 0.8f) { mainMul = 0.80f; edgeMul = 1.10f; }
        else                  { mainMul = 0.65f; edgeMul = 1.15f; }

        int faceCount = mesh.FaceCount;

        // Main faces are the first 6 (AddMainFaceY ×2, AddMainFaceZ ×2, AddMainFaceX ×2)
        for (int f = 0;  f < Math.Min(6,  faceCount); f++) mesh.MultiplyFaceColor(f, mainMul);
        // Chamfer strips are faces 6–29 (12 long + 8 end = 20, but end chamfers are triangles = 8 faces)
        // Corner triangles follow: 8 faces. Total edge region ≈ faces 6..33.
        for (int f = 6;  f < Math.Min(34, faceCount); f++) mesh.MultiplyFaceColor(f, edgeMul);

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
