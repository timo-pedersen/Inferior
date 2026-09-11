using Inferior.Rendering;
using Microsoft.Xna.Framework;

namespace Inferior.Game.StationGen;

public static partial class StationDecorator
{
    // ── Pass 7: Lights ────────────────────────────────────────────────────────

    private static void GenerateLights(PlacedModule mod, FaceInfo[] faces,
        System.Random rng, StationModuleMesh mesh)
    {
        PlaceNavigationLights  (mod, mesh);
        PlaceWarningStrobes    (mod, mesh, rng);
        PlaceJunctionStrips    (mod, faces, mesh);
        PlaceBayGuidanceLights (mod, faces, mesh);
    }

    // Returns the lens quad's vertex base (for AnimTags) and the position callers should
    // register as StationLightInfo.WorldPosition for the glow sprite. That position is
    // deliberately a little proud of the lens quad itself (see glowForwardBias below),
    // not merely coincident with it — registering the flush `position`, or even the
    // lens's own exact surface position, left the glow sprite sitting at essentially the
    // same depth as the glass geometry it's meant to shine through. DepthRead (added
    // once real depth-testing was needed) then clipped the sprite against that glass
    // from head-on angles, where perspective gives the flat glass quad's surface a
    // depth that's equal to or nearer than the sprite's single point at some pixels —
    // "gradually clips out of glass moving to the side" is exactly that near-equal-
    // depth ambiguity resolving itself as the viewing angle changes. Sitting reliably
    // in front of the glass avoids the ambiguity entirely rather than relying on a
    // razor-thin coincident-depth comparison.
    private static (int vb, Vector3 glowPosition) AddLight(StationModuleMesh mesh,
        Vector3 position, Vector3 normal, float size, Color housing, Color lens)
    {
        const float depth = 0.15f;
        // Shifts the housing off the surface — position sits flush with the hull/panel
        // behind it, so an unshifted housing's outer face (at position, zero offset)
        // z-fights with that panel. raise also doubles as the lens-proud-of-housing gap
        // below, preserving the original 0.01m lens-proud-of-flush-housing relationship
        // now that the housing's outer face itself sits at position + raise, not position.
        const float raise = 0.01f;
        const float glowForwardBias = 0.05f; // clear of the glass, not just coincident with it
        mesh.AddOrientedBox(position + normal * (raise - depth * 0.5f), normal,
            depth, size * 1.4f, size * 1.4f, housing);
        Vector3 lensCenter = position + normal * (raise * 2f);
        int vb = mesh.AddQuad(lensCenter, normal, TangentFrame(normal).up, size, size, lens);
        return (vb, lensCenter + normal * glowForwardBias);
    }

    private static void PlaceNavigationLights(PlacedModule mod, StationModuleMesh mesh)
    {
        Vector3 bb   = mod.Definition.BoundingBox;
        Vector3 half = bb * 0.5f;

        (Vector3 normal, Vector3 pos, Color lens)[] navLights =
        [
            ( Vector3.UnitX,  new Vector3(+half.X, 0, 0),  new Color( 60, 230,  80)),
            (-Vector3.UnitX,  new Vector3(-half.X, 0, 0),  new Color(230,  55,  55)),
            (-Vector3.UnitZ,  new Vector3(0, half.Y * 0.5f, -half.Z), new Color(210, 220, 255)),
        ];

        Color housing = new(40, 40, 40);
        foreach (var (normal, pos, lens) in navLights)
        {
            if (IsFaceBlocked(mod, normal)) continue;
            var (vb, glowPos) = AddLight(mesh, pos, normal, 0.4f, housing, lens);
            mesh.AnimTags.Add(new AnimTag
            {
                Type       = AnimType.Steady,
                VertexBase = vb,
                OnColor    = lens,
                OffColor   = DarkenColor(lens, 0.1f),
                Period     = 1f,
            });
            mod.GlowLights.Add(new StationLightInfo(
                WorldPosition: Vector3.Transform(glowPos, mod.Transform),
                Colour:        lens,
                Type:          GlowType.NavigationLight,
                BaseIntensity: 0.80f,
                Rate:          0f,
                Phase:         0f,
                Pattern:       LightPattern.Continuous));
        }
    }

    private static void PlaceWarningStrobes(PlacedModule mod, StationModuleMesh mesh,
        System.Random rng)
    {
        if (mod.Definition.Category != "docking") return;

        Vector3 bb   = mod.Definition.BoundingBox;
        Vector3 half = bb * 0.5f;
        Color amber   = new(255, 160, 0);
        Color housing = new(40, 40, 40);

        float[] offsets = [-half.Z * 0.4f, half.Z * 0.4f];
        float phase = 0f;
        foreach (float zOff in offsets)
        {
            Vector3 pos = new(0, half.Y, zOff);
            var (vb, glowPos) = AddLight(mesh, pos, Vector3.UnitY, 0.5f, housing, amber);
            mesh.AnimTags.Add(new AnimTag
            {
                Type       = AnimType.Strobe,
                VertexBase = vb,
                OnColor    = amber,
                OffColor   = new Color(30, 12, 0),
                Period     = 1.4f,
                Phase      = phase,
            });
            mod.GlowLights.Add(new StationLightInfo(
                WorldPosition: Vector3.Transform(glowPos, mod.Transform),
                Colour:        amber,
                Type:          GlowType.WarningStrobe,
                BaseIntensity: 0.80f,
                Rate:          1f / 1.4f,
                Phase:         phase,
                Pattern:       LightPattern.Strobe));
            phase += 0.5f;
        }
    }

    private static void PlaceJunctionStrips(PlacedModule mod, FaceInfo[] faces,
        StationModuleMesh mesh)
    {
        Color amber   = new(200, 130, 20);
        Color housing = new(35, 35, 35);

        foreach (var face in faces)
        {
            if (face.IsExposed) continue;

            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 pos = face.LocalCenter
                    + face.LocalRight * (side * face.Width * 0.25f)
                    + face.LocalNormal * 0.05f;

                var (vb, _) = AddLight(mesh, pos, face.LocalNormal, 0.3f, housing, amber);
                mesh.AnimTags.Add(new AnimTag
                {
                    Type       = AnimType.Steady,
                    VertexBase = vb,
                    OnColor    = amber,
                    OffColor   = DarkenColor(amber, 0.05f),
                    Period     = 1f,
                });
            }
        }
    }

    private static void PlaceBayGuidanceLights(PlacedModule mod, FaceInfo[] faces,
        StationModuleMesh mesh)
    {
        if (mod.Definition.Category != "docking") return;

        Color white   = new(230, 240, 255);
        Color housing = new(35, 35, 35);

        foreach (var port in mod.Definition.Ports)
        {
            if (!port.IsDocking) continue;

            FaceInfo? dockFace = null;
            foreach (var f in faces)
            {
                if (Vector3.Dot(f.LocalNormal, port.OutwardNormal) > 0.9f)
                { dockFace = f; break; }
            }
            if (dockFace is not FaceInfo df) continue;

            for (int i = 0; i < 4; i++)
            {
                float u = ((float)i / 3f - 0.5f) * df.Width * 0.7f;
                Vector3 pos = df.LocalCenter
                    + df.LocalRight * u
                    - df.LocalUp * (df.Height * 0.35f)
                    + df.LocalNormal * 0.05f;

                var (vb, _) = AddLight(mesh, pos, df.LocalNormal, 0.35f, housing, white);
                mesh.AnimTags.Add(new AnimTag
                {
                    Type       = AnimType.Pulse,
                    VertexBase = vb,
                    OnColor    = white,
                    OffColor   = new Color(20, 22, 30),
                    Period     = 2f,
                    Phase      = (float)i / 4f,
                });
            }
        }
    }

    // Guidance lights + hazard signage around the docking-bay's door. Can't reuse
    // PlaceBayGuidanceLights as-is: that function finds its target via the generic
    // ComputeFaces/base-face list, but the docking-bay's door is a framed opening, not a
    // solid base face, so it never appears in that list. The door's own geometry (position,
    // size) is known directly from the definition instead, so this calls AddLight the same
    // way, just without going through the face lookup.
    private static void PlaceDockingBayDoorDecoration(PlacedModule mod, StationModuleMesh mesh)
    {
        if (mod.Definition.Category != "docking-bay") return;

        float halfDepth = mod.Definition.BoundingBox.Z * 0.5f;
        float doorW     = mod.Definition.DoorOpening.X;
        float doorH     = mod.Definition.DoorOpening.Y;

        Vector3 doorNormal = -Vector3.UnitZ;
        Vector3 doorCenter = new(0, 0, -halfDepth);
        var (right, up)    = TangentFrame(doorNormal);

        // Guidance lights — 4 pulsing white lights above the opening and 4 below, mounted flush
        // on the door THROAT (the recessed prism wall between the frame's outer and inner
        // faces — see DockingBayHull.AddDoorThroat), not on the frame's outward-facing strips.
        // A light flush with the frame face only really reads face-on from outside; recessed
        // into the throat, facing inward (perpendicular to the door's own normal), it's visible
        // looking down the tunnel from either side. Z sits at the throat's approximate
        // mid-depth — NominalWallThickness is the same non-seeded approximation already used to
        // size the envelope (StationModuleRegistry.CreateDockingBay), not the real per-module
        // seeded thickness, which this decoration pass has no access to and doesn't need to
        // match exactly. u stays well inside the flat strip, away from the chamfered corners.
        Color white   = new(230, 240, 255);
        Color housing = new(35, 35, 35);
        float throatMidZ = -halfDepth + StationModuleRegistry.NominalWallThickness * 0.5f;
        foreach (float rowSign in new[] { 1f, -1f })
        {
            Vector3 mountNormal = -up * rowSign;   // top row faces down into the throat, bottom row faces up
            for (int i = 0; i < 4; i++)
            {
                float u = ((float)i / 3f - 0.5f) * doorW * 0.7f;
                Vector3 pos = right * u + up * rowSign * (doorH * 0.5f)
                            + new Vector3(0, 0, throatMidZ) + mountNormal * 0.05f;

                var (vb, glowPos) = AddLight(mesh, pos, mountNormal, 0.35f, housing, white);
                mesh.AnimTags.Add(new AnimTag
                {
                    Type       = AnimType.Pulse,
                    VertexBase = vb,
                    OnColor    = white,
                    OffColor   = new Color(20, 22, 30),
                    Period     = 2f,
                    Phase      = (float)i / 4f,
                });
                // Missing previously — the pulsing housing/lens geometry existed, but with no
                // GlowLights entry the billboard glow sprite (ComputeGlowIntensity /
                // SystemSpaceState.Stations.cs) never drew, so the light never actually "shone".
                mod.GlowLights.Add(new StationLightInfo(
                    WorldPosition: Vector3.Transform(glowPos, mod.Transform),
                    Colour:        white,
                    Type:          GlowType.DockGuidance,
                    BaseIntensity: 1.0f,
                    Rate:          1f / 2f,
                    Phase:         (float)i / 4f,
                    Pattern:       LightPattern.SlowPulse));
            }
        }

        // Hazard signage — a color-coded placard on the frame above the opening, reusing the
        // same per-pixel bitmap-font geometry already proven on shipping containers rather
        // than a new rendering capability. The guidance lights no longer sit on this face (moved
        // into the throat above), so this offset is just clearance from the opening itself now.
        const string text      = "CAUTION - BAY";
        Color        signColor = new(230, 180, 40);
        const float signMargin = 0.6f;
        float pixelSize = System.Math.Clamp(
            doorW * 0.6f / (text.Length * (BitmapFonts.CharW + 1)), 0.05f, 0.30f);
        float textW = text.Length * (BitmapFonts.CharW + 1) * pixelSize;
        Vector3 textOrigin = doorCenter - right * (textW * 0.5f)
                            + up * (doorH * 0.5f + signMargin)
                            + doorNormal * 0.02f;   // proud of the frame surface, avoids z-fighting

        PlanarTextGeometry.Add(mesh, text, textOrigin,
            surfaceNormal: doorNormal, readingDirection: right, pixelSize, signColor);
    }

    // ── Pass 8: Ambient position markers ─────────────────────────────────────

    private static void RegisterModuleAmbientLights(PlacedModule mod, FaceInfo[] faces,
        System.Random rng)
    {
        if (rng.NextDouble() > 0.60) return;

        int count    = rng.Next(1, 3);
        var eligible = faces.Where(f => f.IsExposed).ToList();
        if (eligible.Count == 0) return;

        for (int i = 0; i < count && i < eligible.Count; i++)
        {
            var face  = eligible[rng.Next(eligible.Count)];
            float cu  = ((float)rng.NextDouble() - 0.5f) * face.Width  * 0.6f;
            float cv  = ((float)rng.NextDouble() - 0.5f) * face.Height * 0.6f;
            Color col = PickMarkerColour(mod.Definition.Category, rng);
            float intensity = 0.18f + (float)rng.NextDouble() * 0.14f;

            mod.GlowLights.Add(new StationLightInfo(
                WorldPosition: StationPoint(mod, face, cu, cv, 0.05f),
                Colour:        col,
                Type:          GlowType.AmbientMarker,
                BaseIntensity: intensity,
                Rate:          0f,
                Phase:         0f,
                Pattern:       LightPattern.Continuous));
        }
    }

    private static Color PickMarkerColour(string category, System.Random rng)
    {
        double r = rng.NextDouble();
        return category switch
        {
            "science"  => r < 0.50 ? new Color(150, 200, 255)
                        : r < 0.80 ? new Color(200, 240, 255)
                        :            new Color(130, 255, 160),
            "docking"  => r < 0.60 ? new Color(255, 200, 80)
                        :            new Color(200, 220, 255),
            "military" => r < 0.70 ? new Color(255, 80, 80)
                        :            new Color(200, 200, 200),
            "hab" or "luxury"
                       => r < 0.50 ? new Color(255, 240, 200)
                        : r < 0.75 ? new Color(255, 220, 140)
                        :            new Color(200, 220, 255),
            _          => r < 0.40 ? new Color(255, 220, 140)
                        : r < 0.70 ? new Color(220, 225, 255)
                        :            new Color(255, 255, 240),
        };
    }

}
