using Microsoft.Xna.Framework;

namespace Inferior.Game.StationGen;

// Builds the complete hull for the "docking-bay" module — the first hollow station module:
// 5 solid exterior walls, a framed opening on the -Z face (no solid panel there), and interior
// wall surfaces so the cavity is visible from inside. MeshFactory modules own their entire hull
// (see HabBlockOctagonal) — there is no separate box-hull draw path for them (SystemSpaceState.cs
// skips BuildHullMesh whenever Definition.MeshFactory != null).
internal static class DockingBayHull
{
    // Wall thickness, seeded per module (20-50cm) — same pattern as StationGenerator.ChamferDepthForSeed.
    // Only consumed here (baked into geometry once), so it doesn't need to live on PlacedModule.
    private static float WallThicknessForSeed(int seed)
        => 0.20f + (float)new System.Random(seed ^ 0x444F434B).NextDouble() * 0.30f;

    public static StationModuleMesh Build(int seed, Vector2 doorOpening)
    {
        var mesh = new StationModuleMesh();

        float chamfer = StationGenerator.ChamferDepthForSeed(seed);
        float t       = WallThicknessForSeed(seed);
        float si      = chamfer * 0.707f;   // same inset convention as BuildHullMesh (Stations.cs)

        Vector3 h = new(24f, 16f, 50f);     // half of the 48x32x100 bounding box
        float doorHalfW = doorOpening.X * 0.5f, doorHalfH = doorOpening.Y * 0.5f;

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

        // ── Door frame at -Z: 4 strips between the inset outer rectangle (flush with the
        // other 5 panels' bevel) and the door opening. No inset on the door's own edges —
        // that boundary stays crisp per the brief, unlike every other edge on this hull.
        // Each strip gets both an outer- and inner-facing surface, separated by the module's
        // own wall thickness t — the same treatment already applied to the other 5 walls
        // (outer panel + AddInwardQuad interior panel below). The original MVP pass only
        // built the outer half here, leaving the frame invisible from inside the bay.
        float outerX = h.X - si, outerY = h.Y - si;
        float outerZ = -h.Z;
        float innerZ = -h.Z + t;

        // Top strip (full width, above the door)
        mesh.AddQuad(new(outerX, doorHalfH, outerZ), new(-outerX, doorHalfH, outerZ),
                     new(-outerX, outerY,   outerZ), new(outerX,  outerY,   outerZ), hullColor);
        AddInwardQuad(mesh, new(outerX, doorHalfH, innerZ), new(-outerX, doorHalfH, innerZ),
                     new(-outerX, outerY,   innerZ), new(outerX,  outerY,   innerZ),
                     Vector3.UnitZ, interiorColor);

        // Bottom strip (full width, below the door)
        mesh.AddQuad(new(outerX, -outerY,  outerZ), new(-outerX, -outerY,  outerZ),
                     new(-outerX, -doorHalfH, outerZ), new(outerX, -doorHalfH, outerZ), hullColor);
        AddInwardQuad(mesh, new(outerX, -outerY,  innerZ), new(-outerX, -outerY,  innerZ),
                     new(-outerX, -doorHalfH, innerZ), new(outerX, -doorHalfH, innerZ),
                     Vector3.UnitZ, interiorColor);

        // Left strip (door height only, avoids overlapping the top/bottom corners)
        mesh.AddQuad(new(-doorHalfW, -doorHalfH, outerZ), new(-outerX, -doorHalfH, outerZ),
                     new(-outerX, doorHalfH, outerZ), new(-doorHalfW, doorHalfH, outerZ), hullColor);
        AddInwardQuad(mesh, new(-doorHalfW, -doorHalfH, innerZ), new(-outerX, -doorHalfH, innerZ),
                     new(-outerX, doorHalfH, innerZ), new(-doorHalfW, doorHalfH, innerZ),
                     Vector3.UnitZ, interiorColor);

        // Right strip
        mesh.AddQuad(new(outerX, -doorHalfH, outerZ), new(doorHalfW, -doorHalfH, outerZ),
                     new(doorHalfW, doorHalfH, outerZ), new(outerX, doorHalfH, outerZ), hullColor);
        AddInwardQuad(mesh, new(outerX, -doorHalfH, innerZ), new(doorHalfW, -doorHalfH, innerZ),
                     new(doorHalfW, doorHalfH, innerZ), new(outerX, doorHalfH, innerZ),
                     Vector3.UnitZ, interiorColor);

        // ── Chamfer bevel — all 12 edges + 8 corners. The door hole sits well inside the -Z
        // face, so the box's outer silhouette (and this bevel) is completely unaffected by it.
        StationDecorator.AddChamferEdgeTrim(mesh, h, chamfer, trimColor);

        // ── Interior walls — the hollow cavity, open at -Z (the door; ships pass straight
        // through, no wall there). Side walls span the full interior length from the door
        // threshold to the inset back wall; the back wall (+Z) closes the far end.
        float cx = h.X - t, cy = h.Y - t;
        float backZ = h.Z - t;

        AddInwardQuad(mesh, new(-cx,-cy,backZ), new(cx,-cy,backZ), new(cx,cy,backZ), new(-cx,cy,backZ),
                      -Vector3.UnitZ, interiorColor);                                          // back wall
        AddInwardQuad(mesh, new(h.X-t,-cy,-h.Z), new(h.X-t,cy,-h.Z), new(h.X-t,cy,backZ), new(h.X-t,-cy,backZ),
                      -Vector3.UnitX, interiorColor);                                          // +X side
        AddInwardQuad(mesh, new(-(h.X-t),-cy,-h.Z), new(-(h.X-t),cy,-h.Z), new(-(h.X-t),cy,backZ), new(-(h.X-t),-cy,backZ),
                      Vector3.UnitX, interiorColor);                                           // -X side
        AddInwardQuad(mesh, new(-cx,h.Y-t,-h.Z), new(cx,h.Y-t,-h.Z), new(cx,h.Y-t,backZ), new(-cx,h.Y-t,backZ),
                      -Vector3.UnitY, interiorColor);                                          // +Y side
        AddInwardQuad(mesh, new(-cx,-(h.Y-t),-h.Z), new(cx,-(h.Y-t),-h.Z), new(cx,-(h.Y-t),backZ), new(-cx,-(h.Y-t),backZ),
                      Vector3.UnitY, interiorColor);                                           // -Y side

        // BaseFaceCount intentionally left at 0 (the default). StationDecorator.ComputeFaces
        // and ApplyAmbientOcclusion both key off BaseFaceCount for MeshFactory modules — leaving
        // it unset makes every face-iterating decoration pass a no-op for this module, which is
        // exactly the "no decoration" MVP scope.
        return mesh;
    }

    // Adds a quad, reversing vertex order if the naive winding doesn't match the expected
    // (inward-facing) normal — same discipline as ChamferedBox.WindFace/StationModuleMesh.AddDiscCap,
    // rather than hand-deriving winding per interior wall.
    private static void AddInwardQuad(StationModuleMesh mesh, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3,
                                       Vector3 expectedNormal, Color color)
    {
        Vector3 n = Vector3.Cross(v1 - v0, v2 - v0);
        if (Vector3.Dot(n, expectedNormal) < 0)
            mesh.AddQuad(v0, v3, v2, v1, color);
        else
            mesh.AddQuad(v0, v1, v2, v3, color);
    }
}
