using Inferior.Game.Containers;
using Inferior.Rendering;
using Microsoft.Xna.Framework;

namespace Inferior.Game.StationGen;

public static partial class StationDecorator
{
    // ── Landing pad geometry ──────────────────────────────────────────────────

    // Adds a flat visual landing pad at each IsDocking port of every docking module.
    // No physics or interaction — purely cosmetic for Step 1.
    private static void GenerateLandingPads(IReadOnlyList<PlacedModule> modules, System.Random rng)
    {
        int bayNumber = 1;
        foreach (var mod in modules)
        {
            if (mod.Definition.Category != "docking") continue;
            mod.Mesh ??= new StationModuleMesh { Texture = TextureFor(mod.Definition.Category) };
            mod.Mesh.CurrentDecorClass = DecorClass.LandingPadMarkings;
            var faces = ComputeFaces(mod);
            foreach (var port in mod.Definition.Ports)
            {
                if (!port.IsDocking) continue;
                FaceInfo? padFace = null;
                foreach (var f in faces)
                {
                    if (Vector3.Dot(f.LocalNormal, port.OutwardNormal) > 0.9f)
                    { padFace = f; break; }
                }
                if (padFace is not FaceInfo face) continue;
                AddLandingPadGeometry(mod.Mesh, face, mod, bayNumber++, rng);
            }
        }
    }

    private static void AddLandingPadGeometry(StationModuleMesh mesh, FaceInfo face,
        PlacedModule mod, int bayNumber, System.Random rng)
    {
        float padW = face.Width  - 1.0f;
        float padH = face.Height - 1.0f;
        if (padW < 1f || padH < 1f) return;

        const float raise = 0.06f;
        Vector3 normal = face.LocalNormal;
        Vector3 up     = face.LocalUp;
        Vector3 origin = face.LocalCenter + normal * raise;

        Color padColor    = new Color(158, 153, 145);   // concrete grey
        Color stripeColor = new Color(215, 148, 12);    // amber-yellow
        Color markColor   = new Color(228, 223, 213);   // off-white

        // Pad surface
        mesh.AddQuad(origin, normal, up, padW, padH, padColor);

        // Perimeter stripe — 4 border segments at raise + 1 cm
        float   stripeW = MathF.Max(0.28f, padW * 0.08f);
        float   innerH  = padH - stripeW * 2f;
        Vector3 sp      = origin + normal * 0.01f;
        mesh.AddQuad(sp + up * ( padH * 0.5f - stripeW * 0.5f), normal, up, padW,   stripeW, stripeColor);
        mesh.AddQuad(sp + up * (-padH * 0.5f + stripeW * 0.5f), normal, up, padW,   stripeW, stripeColor);
        mesh.AddQuad(sp + face.LocalRight * ( padW * 0.5f - stripeW * 0.5f), normal, up, stripeW, innerH, stripeColor);
        mesh.AddQuad(sp + face.LocalRight * (-padW * 0.5f + stripeW * 0.5f), normal, up, stripeW, innerH, stripeColor);

        // Central cross marking
        float crossBarW = padW * 0.11f;
        float crossBarL = padH * 0.29f;
        Vector3 cp = origin + normal * 0.02f;
        mesh.AddQuad(cp, normal, up, crossBarW, crossBarL, markColor);   // vertical bar
        mesh.AddQuad(cp, normal, up, crossBarL, crossBarW, markColor);   // horizontal bar

        // Direction arrow — stem + head pointing +up, positioned in upper half
        float   stemW = padW * 0.08f;
        float   stemH = padH * 0.17f;
        float   headW = padW * 0.16f;
        float   headH = padH * 0.09f;
        Vector3 ap    = origin + normal * 0.025f + up * (padH * 0.15f);
        mesh.AddQuad(ap + up * (stemH * 0.5f),           normal, up, stemW, stemH, markColor);
        mesh.AddQuad(ap + up * (stemH + headH * 0.5f),   normal, up, headW, headH, markColor);

        // Corner SlowPulse amber lights — at pad corners
        Color   amber  = new Color(255, 148, 10);
        Color   amberH = new Color(40, 30, 8);
        float   cU     = padW * 0.44f;
        float   cV     = padH * 0.44f;
        float   phase0 = (float)rng.NextDouble();
        (Vector3, float)[] corners =
        [
            (face.LocalCenter + face.LocalRight *  cU + face.LocalUp *  cV, phase0),
            (face.LocalCenter + face.LocalRight * -cU + face.LocalUp *  cV, (phase0 + 0.25f) % 1f),
            (face.LocalCenter + face.LocalRight * -cU + face.LocalUp * -cV, (phase0 + 0.50f) % 1f),
            (face.LocalCenter + face.LocalRight *  cU + face.LocalUp * -cV, (phase0 + 0.75f) % 1f),
        ];
        foreach (var (lpos, lphase) in corners)
        {
            var (_, glowPos) = AddLight(mesh, lpos + normal * (raise + 0.10f), normal, 0.22f, amberH, amber);
            mod.GlowLights.Add(new StationLightInfo(
                WorldPosition: Vector3.Transform(glowPos, mod.Transform),
                Colour:        amber,
                Type:          GlowType.AmbientMarker,
                BaseIntensity: 0.65f,
                Rate:          0.8f,
                Phase:         lphase,
                Pattern:       LightPattern.SlowPulse));
        }

        // Threshold Strobe white lights — two at the approach end (bottom edge)
        Color   white  = new Color(228, 238, 255);
        Color   whiteH = new Color(34, 34, 42);
        (Vector3, float)[] thresholds =
        [
            (face.LocalCenter + face.LocalUp * -cV + face.LocalRight *  (padW * 0.22f), 0.0f),
            (face.LocalCenter + face.LocalUp * -cV + face.LocalRight * -(padW * 0.22f), 0.5f),
        ];
        foreach (var (lpos, lphase) in thresholds)
        {
            var (_, glowPos) = AddLight(mesh, lpos + normal * (raise + 0.10f), normal, 0.22f, whiteH, white);
            mod.GlowLights.Add(new StationLightInfo(
                WorldPosition: Vector3.Transform(glowPos, mod.Transform),
                Colour:        white,
                Type:          GlowType.WarningStrobe,
                BaseIntensity: 0.80f,
                Rate:          1.2f,
                Phase:         lphase,
                Pattern:       LightPattern.Strobe));
        }
    }

    // Returns N ring vertices for one concentric band of the parabolic dish.

    // ── Solar panels ─────────────────────────────────────────────────────────

    private static void RunSolarPanelPass(IReadOnlyList<PlacedModule> modules, System.Random rng)
    {
        if (rng.NextDouble() > 0.20) return;

        Vector3 sunDir = SceneLighting.SunDirection;
        var candidates = new List<(PlacedModule mod, FaceInfo face, float dot)>();

        foreach (var mod in modules)
        {
            if (mod.Definition.Category is not ("core" or "connector")) continue;
            mod.Transform.Decompose(out _, out Quaternion rot, out _);
            foreach (var face in ComputeFaces(mod))
            {
                if (!face.IsExposed) continue;
                Vector3 worldN = Vector3.Normalize(Vector3.Transform(face.LocalNormal, rot));
                float dot = Vector3.Dot(worldN, sunDir);
                if (dot > 0.25f) candidates.Add((mod, face, dot));
            }
        }

        if (candidates.Count == 0) return;
        candidates.Sort((a, b) => b.dot.CompareTo(a.dot));

        int count = Math.Min(rng.Next(1, 4), candidates.Count);
        for (int i = 0; i < count; i++)
        {
            var (mod, face, _) = candidates[i];
            mod.Mesh ??= new StationModuleMesh();
            mod.Mesh.CurrentDecorClass = DecorClass.SolarPanels;
            AddSolarPanelArray(mod.Mesh, face, rng);
        }
    }

    private static void AddSolarPanelArray(StationModuleMesh mesh, FaceInfo face, System.Random rng)
    {
        Color frameCol = new(60, 60, 70);
        Color cellCol  = new(30, 50, 100);

        float armLen = (float)(rng.NextDouble() * 3.0 + 4.0);
        Vector3 armCenter = face.LocalCenter + face.LocalNormal * (armLen * 0.5f + 0.5f);
        mesh.AddOrientedBox(armCenter, face.LocalNormal, armLen, 0.3f, 0.3f, frameCol);

        float panelW = (float)(rng.NextDouble() * 4.0 + 6.0);
        float panelH = (float)(rng.NextDouble() * 1.5 + 2.0);
        Vector3 panelCenter = face.LocalCenter + face.LocalNormal * (armLen + 0.8f);

        var t = new Matrix(
            face.LocalRight.X,  face.LocalRight.Y,  face.LocalRight.Z,  0,
            face.LocalUp.X,     face.LocalUp.Y,     face.LocalUp.Z,     0,
            face.LocalNormal.X, face.LocalNormal.Y, face.LocalNormal.Z, 0,
            panelCenter.X,      panelCenter.Y,      panelCenter.Z,      1
        );
        mesh.AddOrientedBox(t, new Vector3(panelW, panelH, 0.10f), frameCol);

        int   cellCols = Math.Max(1, (int)(panelW / 2f));
        int   cellRows = Math.Max(1, (int)(panelH / 1f));
        float stepU    = panelW / cellCols;
        float stepV    = panelH / cellRows;
        float startCU  = -(cellCols - 1) * stepU * 0.5f;
        float startCV  = -(cellRows - 1) * stepV * 0.5f;
        float cellW    = stepU * 0.88f;
        float cellH    = stepV * 0.88f;

        for (int r = 0; r < cellRows; r++)
        for (int c = 0; c < cellCols; c++)
        {
            Vector3 cc = panelCenter
                + face.LocalRight * (startCU + c * stepU)
                + face.LocalUp    * (startCV + r * stepV)
                + face.LocalNormal * 0.06f;
            mesh.AddQuad(cc, face.LocalNormal, face.LocalUp, cellW, cellH, cellCol);
        }

        for (int r = 0; r < cellRows - 1; r++)
        {
            Vector3 dc = panelCenter
                + face.LocalUp    * (startCV + (r + 0.5f) * stepV)
                + face.LocalNormal * 0.07f;
            mesh.AddQuad(dc, face.LocalNormal, face.LocalUp, panelW * 0.98f, 0.07f, frameCol);
        }
        for (int c = 0; c < cellCols - 1; c++)
        {
            Vector3 dc = panelCenter
                + face.LocalRight  * (startCU + (c + 0.5f) * stepU)
                + face.LocalNormal * 0.07f;
            mesh.AddQuad(dc, face.LocalNormal, face.LocalUp, 0.07f, panelH * 0.98f, frameCol);
        }
    }

}
