using Microsoft.Xna.Framework;

namespace Inferior.Game.StationGen;

// Builds the complete hull for the "docking-bay" module — the first hollow station module:
// 5 solid exterior walls, a chamfered-rectangle (octagon-by-construction) framed opening on the
// -Z face, and interior wall surfaces so the cavity is visible from inside. MeshFactory modules
// own their entire hull (see HabBlockOctagonal) — there is no separate box-hull draw path for
// them (SystemSpaceState.cs skips BuildHullMesh whenever Definition.MeshFactory != null).
//
// Envelope and door size come from the DockingBayLayout computed once in
// StationModuleRegistry.CreateDockingBay (pad-mix driven) — this factory just builds geometry
// from those numbers, it doesn't decide them.
internal static class DockingBayHull
{
    // Wall thickness, seeded per module (0.5-1.5m — revised spec; the original 20-50cm range
    // read as no thickness at all at this module's scale) — same pattern as
    // StationGenerator.ChamferDepthForSeed. Only consumed here (baked into geometry once), so
    // it doesn't need to live on PlacedModule. (StationModuleRegistry.CreateDockingBay uses a
    // fixed nominal value instead, for the envelope used during attachment/collision — this is
    // the real, seeded value used for the actual mesh.)
    private static float WallThicknessForSeed(int seed)
        => 0.5f + (float)new System.Random(seed ^ 0x444F434B).NextDouble() * 1.0f;

    public static StationModuleMesh Build(int seed, DockingBayLayout layout)
    {
        var mesh = new StationModuleMesh();

        float chamfer = StationGenerator.ChamferDepthForSeed(seed);
        float t       = WallThicknessForSeed(seed);
        float si      = chamfer * 0.707f;   // same inset convention as BuildHullMesh (Stations.cs)

        Vector3 h = new(
            layout.CavityWidth  * 0.5f + t,
            layout.CavityHeight * 0.5f + t,
            layout.CavityDepth  * 0.5f + t);

        float doorHalfW = layout.DoorWidth  * 0.5f;
        float doorHalfH = layout.DoorHeight * 0.5f;

        // Corner chamfer as a % of door height (seeded once per station, see DockingBayLayout),
        // clamped well short of degenerate so an extreme seed can't invert the octagon.
        float doorChamfer = System.Math.Min(layout.ChamferFraction * layout.DoorHeight,
                                             System.Math.Min(doorHalfW, doorHalfH) * 0.9f);

        Color hullColor     = StationModuleRegistry.CategoryColor("docking-bay");
        Color interiorColor = Color.Lerp(hullColor, Color.Black, 0.15f);
        Color trimColor     = StationDecorator.LightenColor(hullColor, 1.12f);

        // ── 5 solid exterior walls (everything but the door face, -Z) ──────────────────────
        // Same per-face inset math as BuildHullMesh: face panel inset by si on its two lateral
        // axes so the chamfer strip along each edge isn't hidden behind the panel.

        // +Z — back wall, opposite the door
        mesh.AddQuad(new(-h.X+si,-h.Y+si,+h.Z), new(+h.X-si,-h.Y+si,+h.Z),
                     new(+h.X-si,+h.Y-si,+h.Z), new(-h.X+si,+h.Y-si,+h.Z), hullColor);
        // -X
        mesh.AddQuad(new(-h.X,-h.Y+si,-h.Z+si), new(-h.X,-h.Y+si,+h.Z-si),
                     new(-h.X,+h.Y-si,+h.Z-si), new(-h.X,+h.Y-si,-h.Z+si), hullColor);
        // +X
        mesh.AddQuad(new(+h.X,-h.Y+si,+h.Z-si), new(+h.X,-h.Y+si,-h.Z+si),
                     new(+h.X,+h.Y-si,-h.Z+si), new(+h.X,+h.Y-si,+h.Z-si), hullColor);
        // +Y
        mesh.AddQuad(new(-h.X+si,+h.Y,+h.Z-si), new(+h.X-si,+h.Y,+h.Z-si),
                     new(+h.X-si,+h.Y,-h.Z+si), new(-h.X+si,+h.Y,-h.Z+si), hullColor);
        // -Y
        mesh.AddQuad(new(-h.X+si,-h.Y,-h.Z+si), new(+h.X-si,-h.Y,-h.Z+si),
                     new(+h.X-si,-h.Y,+h.Z-si), new(-h.X+si,-h.Y,+h.Z-si), hullColor);

        // BaseFaceCount = exactly these 5 solid walls — StationDecorator.ComputeFaces and
        // ApplyAmbientOcclusion both key off this for MeshFactory modules, and everything
        // after this point (door frame, chamfer bevel, interior walls) must NOT be treated
        // as a decoratable base face. This is what turns standard exterior decoration
        // (windows, greebles, vents, panel seams, edge trim, AO) back on for this module —
        // the MVP pass deliberately left it at 0 to skip decoration entirely; that's no
        // longer wanted now that the exterior needs to look like every other module.
        mesh.BaseFaceCount = mesh.FaceCount;

        // ── Door frame at -Z: an octagon-by-construction hole (rectangle chamfered at all 4
        // corners) between the door opening and the inset outer rectangle (flush with the other
        // 5 panels' bevel). No inset on the door's own edges — that boundary stays crisp per the
        // brief, unlike every other edge on this hull. Both an outer- and inner-facing surface,
        // separated by the module's own wall thickness — same treatment as the other 5 walls.
        float outerX = h.X - si, outerY = h.Y - si;
        AddDoorFrame(mesh, doorHalfW, doorHalfH, doorChamfer, outerX, outerY,
                     -h.Z,     -Vector3.UnitZ, hullColor);
        AddDoorFrame(mesh, doorHalfW, doorHalfH, doorChamfer, outerX, outerY,
                     -h.Z + t,  Vector3.UnitZ, interiorColor);

        // ── Door throat: the frame's outer and inner faces share the exact same door-hole
        // octagon, so nothing above actually fills the gap between them — looking through the
        // opening (or from inside) showed straight through with no surface revealing depth,
        // which read as zero wall thickness no matter what `t` was set to. These 8 quads (4 flat
        // sides + 4 chamfer diagonals, tracing the same octagon perimeter AddDoorFrame uses) wall
        // in that gap, giving the opening an actual visible tunnel.
        AddDoorThroat(mesh, doorHalfW, doorHalfH, doorChamfer, -h.Z, -h.Z + t, interiorColor);

        // ── Chamfer bevel — all 12 edges + 8 corners. The door hole sits well inside the -Z
        // face, so the box's outer silhouette (and this bevel) is completely unaffected by it.
        StationDecorator.AddChamferEdgeTrim(mesh, h, chamfer, trimColor);

        // ── Interior walls — the hollow cavity, open at -Z (the door; ships pass straight
        // through, no wall there). The back wall (+Z) closes the far end as a single quad. Side
        // walls span the full interior length from the door threshold to the inset back wall,
        // but are subdivided into segments along Z: a single quad per wall gives exactly one
        // flat-shaded brightness value for its entire length, so the door-proximity term in
        // StationGenerator.BoostAmbientForFaceRange had nowhere to actually vary — this is why
        // the gradient wasn't visible. Segment count scales with depth so small bays don't get
        // needlessly cut up and large ones still read as a gradient rather than 2-3 big steps.
        float cx = h.X - t, cy = h.Y - t;
        float backZ = h.Z - t;
        float doorZ = -h.Z;
        float depth = backZ - doorZ;

        int interiorFaceStart = mesh.FaceCount;

        AddWoundQuad(mesh, new(-cx,-cy,backZ), new(cx,-cy,backZ), new(cx,cy,backZ), new(-cx,cy,backZ),
                      -Vector3.UnitZ, interiorColor);                                          // back wall

        int segments = System.Math.Clamp((int)MathF.Round(depth / 20f), 3, 8);
        for (int s = 0; s < segments; s++)
        {
            float z0 = doorZ + depth * s       / segments;
            float z1 = doorZ + depth * (s + 1) / segments;

            AddWoundQuad(mesh, new(h.X-t,-cy,z0), new(h.X-t,cy,z0), new(h.X-t,cy,z1), new(h.X-t,-cy,z1),
                          -Vector3.UnitX, interiorColor);                                      // +X side
            AddWoundQuad(mesh, new(-(h.X-t),-cy,z0), new(-(h.X-t),cy,z0), new(-(h.X-t),cy,z1), new(-(h.X-t),-cy,z1),
                          Vector3.UnitX, interiorColor);                                       // -X side
            AddWoundQuad(mesh, new(-cx,h.Y-t,z0), new(cx,h.Y-t,z0), new(cx,h.Y-t,z1), new(-cx,h.Y-t,z1),
                          -Vector3.UnitY, interiorColor);                                      // +Y side
            AddWoundQuad(mesh, new(-cx,-(h.Y-t),z0), new(cx,-(h.Y-t),z0), new(cx,-(h.Y-t),z1), new(-cx,-(h.Y-t),z1),
                          Vector3.UnitY, interiorColor);                                       // -Y side
        }

        // Flags these faces for the door-proximity/overhead-cue/corner-noise formula in
        // StationGenerator.BoostAmbientForFaceRange (see StationGenerator.BakeLighting for where
        // it's applied), so the cavity reads with depth and orientation instead of baking
        // near-black or flat just because the sun isn't reaching it directly. Still a disposable
        // approximation on top of the existing pre-baked lighting, not a new lighting model —
        // requires no unwinding once real interior-light-fixture baking happens.
        mesh.AmbientOverrideFaceStart = interiorFaceStart;
        mesh.AmbientOverrideFaceCount = mesh.FaceCount - interiorFaceStart;

        return mesh;
    }

    // Builds one face of the door frame (either the outer or inner surface) at a fixed z plane:
    // 4 shortened strips spanning the octagon's flat edges, plus 4 corner fills (a quad + a
    // triangle each) connecting the sharp outer rectangle corners to the chamfered door corners.
    // Same 2D decomposition StationDecorator.AddChamferEdgeTrim uses for the hull's 3D edges,
    // one dimension down.
    private static void AddDoorFrame(StationModuleMesh mesh, float doorHalfW, float doorHalfH,
        float dc, float outerX, float outerY, float z, Vector3 normal, Color color)
    {
        float flatW = doorHalfW - dc;
        float flatH = doorHalfH - dc;

        // Top strip (flat portion of the top edge)
        AddWoundQuad(mesh, new(flatW, doorHalfH, z), new(-flatW, doorHalfH, z),
                     new(-flatW, outerY, z), new(flatW, outerY, z), normal, color);
        // Bottom strip
        AddWoundQuad(mesh, new(flatW, -outerY, z), new(-flatW, -outerY, z),
                     new(-flatW, -doorHalfH, z), new(flatW, -doorHalfH, z), normal, color);
        // Left strip (flat portion of the left edge)
        AddWoundQuad(mesh, new(-doorHalfW, -flatH, z), new(-outerX, -flatH, z),
                     new(-outerX, flatH, z), new(-doorHalfW, flatH, z), normal, color);
        // Right strip
        AddWoundQuad(mesh, new(outerX, -flatH, z), new(doorHalfW, -flatH, z),
                     new(doorHalfW, flatH, z), new(outerX, flatH, z), normal, color);

        // 4 corners — sx/sy pick which quadrant.
        AddDoorCorner(mesh, +1, +1, flatW, flatH, doorHalfW, doorHalfH, outerX, outerY, z, normal, color);
        AddDoorCorner(mesh, -1, +1, flatW, flatH, doorHalfW, doorHalfH, outerX, outerY, z, normal, color);
        AddDoorCorner(mesh, -1, -1, flatW, flatH, doorHalfW, doorHalfH, outerX, outerY, z, normal, color);
        AddDoorCorner(mesh, +1, -1, flatW, flatH, doorHalfW, doorHalfH, outerX, outerY, z, normal, color);
    }

    // Fills the pentagon between the sharp outer corner and the chamfered door corner — the
    // outer boundary square minus the small triangular notch the chamfer cuts off. Two quads,
    // split along Y = doorHalfH: an upper block (full width, above the door's flat-top edge)
    // and a lower wedge (between the chamfer diagonal and the outer edge). A previous version
    // of this used a quad spanning the full corner square plus a separate triangle — that quad
    // wrongly extended past the chamfer diagonal into the door's own open hole, and the
    // triangle then overlapped part of the same area (the reported z-fighting/flicker at the
    // corner). This decomposition has no overlap and doesn't need a triangle at all.
    private static void AddDoorCorner(StationModuleMesh mesh, float sx, float sy,
        float flatW, float flatH, float doorHalfW, float doorHalfH,
        float outerX, float outerY, float z, Vector3 normal, Color color)
    {
        AddWoundQuad(mesh,
            new(sx * outerX, sy * doorHalfH, z), new(sx * flatW, sy * doorHalfH, z),
            new(sx * flatW,  sy * outerY,    z), new(sx * outerX, sy * outerY,   z),
            normal, color);
        AddWoundQuad(mesh,
            new(sx * flatW,  sy * doorHalfH, z), new(sx * doorHalfW, sy * flatH, z),
            new(sx * outerX, sy * flatH,     z), new(sx * outerX,    sy * doorHalfH, z),
            normal, color);
    }

    // Walls in the gap between the door frame's outer (-Z) and inner (+Z) faces, tracing the
    // same octagon perimeter AddDoorFrame builds its picture-frame from (4 flat sides + 4
    // chamfer diagonals, closing back to the start). Each segment becomes one quad spanning
    // from zOuter to zInner at fixed (x,y) — a straight prism, since both frame layers share the
    // exact same door-hole shape. Normal points inward (toward the opening's own center) via a
    // per-segment radial approximation, just enough to seed AddWoundQuad's winding check.
    private static void AddDoorThroat(StationModuleMesh mesh, float doorHalfW, float doorHalfH,
        float dc, float zOuter, float zInner, Color color)
    {
        float flatW = doorHalfW - dc;
        float flatH = doorHalfH - dc;

        Span<Vector2> ring =
        [
            new(-flatW,      doorHalfH),   // top edge, left end
            new( flatW,      doorHalfH),   // top edge, right end
            new( doorHalfW,  flatH),        // top-right chamfer end / right edge, top
            new( doorHalfW, -flatH),        // right edge, bottom
            new( flatW,     -doorHalfH),    // bottom-right chamfer end / bottom edge, right
            new(-flatW,     -doorHalfH),    // bottom edge, left
            new(-doorHalfW, -flatH),        // bottom-left chamfer end / left edge, bottom
            new(-doorHalfW,  flatH),        // left edge, top (closes back to top-left chamfer)
        ];

        for (int i = 0; i < ring.Length; i++)
        {
            Vector2 a = ring[i];
            Vector2 b = ring[(i + 1) % ring.Length];
            Vector2 mid = (a + b) * 0.5f;
            Vector3 inward = mid == Vector2.Zero ? Vector3.UnitZ : new Vector3(-mid.X, -mid.Y, 0f);

            AddWoundQuad(mesh,
                new(a.X, a.Y, zOuter), new(b.X, b.Y, zOuter),
                new(b.X, b.Y, zInner), new(a.X, a.Y, zInner),
                inward, color);
        }
    }

    // Adds a quad, reversing vertex order if the naive winding doesn't match the expected
    // normal — same discipline as ChamferedBox.WindFace/StationModuleMesh.AddDiscCap, rather
    // than hand-deriving winding per face.
    private static void AddWoundQuad(StationModuleMesh mesh, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3,
                                       Vector3 expectedNormal, Color color)
    {
        Vector3 n = Vector3.Cross(v1 - v0, v2 - v0);
        if (Vector3.Dot(n, expectedNormal) < 0)
            mesh.AddQuad(v0, v3, v2, v1, color);
        else
            mesh.AddQuad(v0, v1, v2, v3, color);
    }
}
