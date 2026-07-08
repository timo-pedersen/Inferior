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
    // Wall thickness, seeded per module (20-50cm) — same pattern as StationGenerator.ChamferDepthForSeed.
    // Only consumed here (baked into geometry once), so it doesn't need to live on PlacedModule.
    // (StationModuleRegistry.CreateDockingBay uses a fixed nominal value instead, for the envelope
    // used during attachment/collision — this is the real, seeded value used for the actual mesh.)
    private static float WallThicknessForSeed(int seed)
        => 0.20f + (float)new System.Random(seed ^ 0x444F434B).NextDouble() * 0.30f;

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

        // ── Chamfer bevel — all 12 edges + 8 corners. The door hole sits well inside the -Z
        // face, so the box's outer silhouette (and this bevel) is completely unaffected by it.
        StationDecorator.AddChamferEdgeTrim(mesh, h, chamfer, trimColor);

        // ── Interior walls — the hollow cavity, open at -Z (the door; ships pass straight
        // through, no wall there). Side walls span the full interior length from the door
        // threshold to the inset back wall; the back wall (+Z) closes the far end.
        float cx = h.X - t, cy = h.Y - t;
        float backZ = h.Z - t;

        AddWoundQuad(mesh, new(-cx,-cy,backZ), new(cx,-cy,backZ), new(cx,cy,backZ), new(-cx,cy,backZ),
                      -Vector3.UnitZ, interiorColor);                                          // back wall
        AddWoundQuad(mesh, new(h.X-t,-cy,-h.Z), new(h.X-t,cy,-h.Z), new(h.X-t,cy,backZ), new(h.X-t,-cy,backZ),
                      -Vector3.UnitX, interiorColor);                                          // +X side
        AddWoundQuad(mesh, new(-(h.X-t),-cy,-h.Z), new(-(h.X-t),cy,-h.Z), new(-(h.X-t),cy,backZ), new(-(h.X-t),-cy,backZ),
                      Vector3.UnitX, interiorColor);                                           // -X side
        AddWoundQuad(mesh, new(-cx,h.Y-t,-h.Z), new(cx,h.Y-t,-h.Z), new(cx,h.Y-t,backZ), new(-cx,h.Y-t,backZ),
                      -Vector3.UnitY, interiorColor);                                          // +Y side
        AddWoundQuad(mesh, new(-cx,-(h.Y-t),-h.Z), new(cx,-(h.Y-t),-h.Z), new(cx,-(h.Y-t),backZ), new(-cx,-(h.Y-t),backZ),
                      Vector3.UnitY, interiorColor);                                           // -Y side

        // BaseFaceCount intentionally left at 0 (the default). StationDecorator.ComputeFaces
        // and ApplyAmbientOcclusion both key off BaseFaceCount for MeshFactory modules — leaving
        // it unset makes every face-iterating decoration pass a no-op for this module, which is
        // exactly the "no decoration" MVP scope.
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

        // 4 corners — sx/sy pick which quadrant. Each corner = the outer square minus the small
        // triangular notch the chamfer cuts off, built as a quad (bulk of the corner) plus a
        // triangle (the wedge reaching the two chamfer vertices).
        AddDoorCorner(mesh, +1, +1, flatW, flatH, doorHalfW, doorHalfH, outerX, outerY, z, normal, color);
        AddDoorCorner(mesh, -1, +1, flatW, flatH, doorHalfW, doorHalfH, outerX, outerY, z, normal, color);
        AddDoorCorner(mesh, -1, -1, flatW, flatH, doorHalfW, doorHalfH, outerX, outerY, z, normal, color);
        AddDoorCorner(mesh, +1, -1, flatW, flatH, doorHalfW, doorHalfH, outerX, outerY, z, normal, color);
    }

    private static void AddDoorCorner(StationModuleMesh mesh, float sx, float sy,
        float flatW, float flatH, float doorHalfW, float doorHalfH,
        float outerX, float outerY, float z, Vector3 normal, Color color)
    {
        AddWoundQuad(mesh,
            new(sx * outerX, sy * flatH,  z), new(sx * flatW, sy * flatH,  z),
            new(sx * flatW,  sy * outerY, z), new(sx * outerX, sy * outerY, z),
            normal, color);
        AddWoundTriangle(mesh,
            new(sx * flatW, sy * flatH,     z),
            new(sx * flatW, sy * doorHalfH, z),
            new(sx * doorHalfW, sy * flatH, z),
            normal, color);
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

    // Same auto-winding discipline as AddWoundQuad, for the door corner triangles.
    private static void AddWoundTriangle(StationModuleMesh mesh, Vector3 v0, Vector3 v1, Vector3 v2,
                                          Vector3 expectedNormal, Color color)
    {
        Vector3 n = Vector3.Cross(v1 - v0, v2 - v0);
        if (Vector3.Dot(n, expectedNormal) < 0)
            mesh.AddTriangle(v0, v2, v1, color);
        else
            mesh.AddTriangle(v0, v1, v2, color);
    }
}
